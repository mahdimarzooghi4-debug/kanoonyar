using System.Security.Claims;
using System.Text.Json;
using Kanoonyar.Api.Programs;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Xunit;

namespace Kanoonyar.Api.Tests;

/// <summary>
/// Real handler -> Npgsql -> disposable PostgreSQL integration. The bypassing permission
/// adapters are test-only and never registered in the application or production.
/// This suite does NOT claim real OIDC/browser authorization or production readiness.
/// </summary>
public sealed class LocalDraftPostgresIntegrationTests
{
    private static readonly ILocalPlanDraftWriteAuthorization TestWriter = new TestOnlyWriteGrant();
    private static readonly ILocalPlanDraftReadAuthorization TestReader = new TestOnlyReadGrant();

    private static string? RuntimeConnection =>
        Environment.GetEnvironmentVariable("CENTER_DRAFT_INTEGRATION_DATABASE_URL");
    private static string? ReadConnection =>
        Environment.GetEnvironmentVariable("CENTER_DRAFT_INTEGRATION_READ_DATABASE_URL");
    private static string? AdminConnection =>
        Environment.GetEnvironmentVariable("CENTER_DRAFT_INTEGRATION_ADMIN_URL");
    private static string? EscalationConnection =>
        Environment.GetEnvironmentVariable("CENTER_DRAFT_INTEGRATION_ESCALATION_DATABASE_URL");

    private static bool Enabled()
    {
        var required = Environment.GetEnvironmentVariable("CENTER_DRAFT_INTEGRATION_REQUIRED") == "1";
        if (required)
        {
            Assert.False(string.IsNullOrWhiteSpace(RuntimeConnection));
            Assert.False(string.IsNullOrWhiteSpace(ReadConnection));
            Assert.False(string.IsNullOrWhiteSpace(AdminConnection));
            Assert.False(string.IsNullOrWhiteSpace(EscalationConnection));
        }
        return !string.IsNullOrWhiteSpace(RuntimeConnection) && !string.IsNullOrWhiteSpace(ReadConnection) &&
            !string.IsNullOrWhiteSpace(AdminConnection) &&
            !string.IsNullOrWhiteSpace(EscalationConnection);
    }

    private static IConfiguration Configuration() =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["CENTER_DRAFT_WRITE_DATABASE_URL"] = RuntimeConnection,
            ["CENTER_DRAFT_READ_DATABASE_URL"] = ReadConnection
        }).Build();

    private static ClaimsPrincipal Principal(string subject) =>
        new(new ClaimsIdentity(new[] { new Claim("sub", subject) }, "integration-fixture"));

    private static LocalPlanDraftRevisionRequest Request(Guid draftId, long expectedRevision = 0) =>
        new(draftId, expectedRevision, "national-1", 1, "Local plan", ["Activity"]);

    private static async Task<IResult> Write(Guid draftId, string subject = "actor-a",
        string center = "center-a", long expectedRevision = 0, bool useDefaultDeny = false,
        IConfiguration? configuration = null) =>
        await LocalPlanDraftWriteEndpoint.Append(
            Principal(subject), configuration ?? Configuration(),
            useDefaultDeny ? new UnapprovedLocalPlanDraftWriteAuthorization() : TestWriter,
            center, Request(draftId, expectedRevision), CancellationToken.None);

    private static async Task<IResult> Read(string subject = "actor-a",
        string center = "center-a", bool useDefaultDeny = false) =>
        await LocalPlanDraftReadEndpoint.ListMine(Principal(subject), Configuration(),
            useDefaultDeny ? new UnapprovedLocalPlanDraftReadAuthorization() : TestReader,
            center, CancellationToken.None);

    private static int? Status(IResult result) => (result as IStatusCodeHttpResult)?.StatusCode;

    private static async Task<(long Revisions, long Audits, long Events)> Counts(Guid draft)
    {
        await using var connection = new NpgsqlConnection(AdminConnection);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            SELECT
              (SELECT count(*) FROM programs.center_plan_draft_revisions WHERE draft_id=@id),
              (SELECT count(*) FROM governance.command_audit WHERE target_id=@target),
              (SELECT count(*) FROM governance.outbox_events WHERE aggregate_id=@target)
            """, connection);
        command.Parameters.AddWithValue("id", draft);
        command.Parameters.AddWithValue("target", draft.ToString("D"));
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return (reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2));
    }

    private static JsonElement Drafts(IResult result)
    {
        Assert.Equal(200, Status(result));
        var payload = Assert.IsAssignableFrom<IValueHttpResult>(result).Value;
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(payload));
        return document.RootElement.GetProperty("drafts").Clone();
    }

    [Fact]
    public async Task Real_limited_role_roundtrip_is_versioned_and_audited()
    {
        if (!Enabled()) return;
        var draft = Guid.NewGuid();
        Assert.Equal(200, Status(await Write(draft)));
        var stored = Drafts(await Read());
        Assert.Contains(stored.EnumerateArray(), item =>
            item.GetProperty("draftId").GetGuid() == draft &&
            item.GetProperty("revision").GetInt64() == 1);
        Assert.Equal(403, Status(await Read(useDefaultDeny: true)));

        Assert.Equal(200, Status(await Write(draft, expectedRevision: 1)));
        Assert.Equal(409, Status(await Write(draft, expectedRevision: 1)));
        Assert.Equal((2L, 2L, 2L), await Counts(draft));
    }

    [Fact]
    public async Task Independent_permission_and_live_membership_prevent_cross_center_and_revoked_writes()
    {
        if (!Enabled()) return;
        var draft = Guid.NewGuid();
        Assert.Equal(403, Status(await Write(draft, useDefaultDeny: true)));
        Assert.Equal(403, Status(await Write(draft, subject: "actor-b")));
        Assert.Equal(403, Status(await Write(draft, subject: "actor-revoked")));
        Assert.Equal(403, Status(await Write(draft, center: "center-b")));
        Assert.Equal((0L, 0L, 0L), await Counts(draft));
    }

    [Fact]
    public async Task Read_requires_independent_permission_and_live_center_membership()
    {
        if (!Enabled()) return;
        var draft = Guid.NewGuid();
        Assert.Equal(200, Status(await Write(draft)));
        Assert.Equal(403, Status(await Read(useDefaultDeny: true)));
        Assert.Empty(Drafts(await Read(subject: "actor-b")).EnumerateArray());
        Assert.Empty(Drafts(await Read(subject: "actor-revoked")).EnumerateArray());
        Assert.Empty(Drafts(await Read(center: "center-b")).EnumerateArray());
        Assert.Contains(Drafts(await Read()).EnumerateArray(), item =>
            item.GetProperty("draftId").GetGuid() == draft);
    }

    [Fact]
    public async Task Simultaneous_expected_revision_zero_accepts_only_one_with_matching_audit()
    {
        if (!Enabled()) return;
        var draft = Guid.NewGuid();
        var results = await Task.WhenAll(Write(draft), Write(draft));
        Assert.Equal(1, results.Count(result => Status(result) == 200));
        Assert.Equal(1, results.Count(result => Status(result) == 409));
        Assert.Equal((1L, 1L, 1L), await Counts(draft));
    }

    [Fact]
    public async Task Privileged_database_read_connection_is_refused()
    {
        if (!Enabled()) return;
        var config = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["CENTER_DRAFT_READ_DATABASE_URL"] = AdminConnection }).Build();
        var response = await LocalPlanDraftReadEndpoint.ListMine(
            Principal("actor-a"), config, TestReader, "center-a", CancellationToken.None);
        Assert.Equal(503, Status(response));
    }

    [Fact]
    public async Task Privileged_database_connection_is_refused_before_any_command()
    {
        if (!Enabled()) return;
        var draft = Guid.NewGuid();
        var privileged = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["CENTER_DRAFT_WRITE_DATABASE_URL"] = AdminConnection }).Build();
        Assert.Equal(503, Status(await Write(draft, configuration: privileged)));
        Assert.Equal((0L, 0L, 0L), await Counts(draft));
    }

    [Fact]
    public async Task Read_only_login_rejects_direct_insert_and_function_escalation()
    {
        if (!Enabled()) return;
        await using var db = new NpgsqlConnection(ReadConnection);
        await db.OpenAsync();
        var draft = Guid.NewGuid();
        await using (var insert = new NpgsqlCommand(
            """
            INSERT INTO programs.center_plan_draft_revisions
              (center_id,draft_id,revision,program_id,national_version,local_title,local_activities,actor_subject)
            VALUES ('center-a',@draft,1,'national-1',1,'Bypassed','[]','forged-actor')
            """, db))
        {
            insert.Parameters.AddWithValue("draft", draft);
            var denied = await Assert.ThrowsAsync<PostgresException>(
                () => insert.ExecuteNonQueryAsync());
            Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, denied.SqlState);
        }
        await using (var escalate = new NpgsqlCommand(
            "SET ROLE kanoonyar_draft_api_ci", db))
        {
            var denied = await Assert.ThrowsAsync<PostgresException>(
                () => escalate.ExecuteNonQueryAsync());
            Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, denied.SqlState);
        }
        await using (var directFunction = new NpgsqlCommand(
            """
            SELECT programs.append_local_plan_draft_revision(
              'center-a',@draft,0,'national-1',1,'Forged','[]'::jsonb,'forged-actor',
              gen_random_uuid(),gen_random_uuid(),'forged')
            """, db))
        {
            directFunction.Parameters.AddWithValue("draft", draft);
            var denied = await Assert.ThrowsAsync<PostgresException>(
                () => directFunction.ExecuteNonQueryAsync());
            Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, denied.SqlState);
        }
        Assert.Equal((0L, 0L, 0L), await Counts(draft));
    }

    [Fact]
    public async Task Read_handler_refuses_a_direct_write_capable_role_even_with_a_test_grant()
    {
        if (!Enabled()) return;
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> {
                ["CENTER_DRAFT_READ_DATABASE_URL"] = RuntimeConnection
            }).Build();
        var response = await LocalPlanDraftReadEndpoint.ListMine(
            Principal("actor-a"), configuration, TestReader, "center-a", CancellationToken.None);
        Assert.Equal(503, Status(response));
    }

    [Fact]
    public async Task Read_handler_refuses_login_that_can_set_role_to_writer()
    {
        if (!Enabled()) return;
        var config = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> {
                ["CENTER_DRAFT_READ_DATABASE_URL"] = EscalationConnection
            }).Build();
        var response = await LocalPlanDraftReadEndpoint.ListMine(
            Principal("actor-a"), config, TestReader, "center-a", CancellationToken.None);
        Assert.Equal(503, Status(response));
    }

    private static async Task<IResult> History(Guid draft, string subject = "actor-a",
        string center = "center-a", long? before = null, bool useDefaultDeny = false) =>
        await LocalPlanDraftHistoryEndpoint.ListHistory(Principal(subject), Configuration(),
            useDefaultDeny ? new UnapprovedLocalPlanDraftReadAuthorization() : TestReader,
            center, draft, before, CancellationToken.None);

    private static (JsonElement Items, long? NextBefore) HistoryPage(IResult result)
    {
        Assert.Equal(200, Status(result));
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(
            Assert.IsAssignableFrom<IValueHttpResult>(result).Value));
        var root = document.RootElement;
        return (root.GetProperty("revisions").Clone(),
            root.GetProperty("nextBeforeRevision").ValueKind == JsonValueKind.Null
                ? null : root.GetProperty("nextBeforeRevision").GetInt64());
    }

    [Fact]
    public async Task Persisted_revision_history_paginates_without_duplicates_or_cross_center_leaks()
    {
        if (!Enabled()) return;
        var draft = Guid.NewGuid();
        for (long revision = 0; revision < 22; revision++)
            Assert.Equal(200, Status(await Write(draft, expectedRevision: revision)));

        var first = HistoryPage(await History(draft));
        Assert.Equal(20, first.Items.GetArrayLength());
        Assert.Equal(22, first.Items[0].GetProperty("revision").GetInt64());
        Assert.Equal(3, first.Items[19].GetProperty("revision").GetInt64());
        Assert.Equal(3L, first.NextBefore);

        var second = HistoryPage(await History(draft, before: first.NextBefore));
        Assert.Equal(2, second.Items.GetArrayLength());
        Assert.Equal(2, second.Items[0].GetProperty("revision").GetInt64());
        Assert.Equal(1, second.Items[1].GetProperty("revision").GetInt64());
        Assert.Null(second.NextBefore);
        Assert.Equal((22L, 22L, 22L), await Counts(draft));

        Assert.Empty(HistoryPage(await History(draft, center: "center-b")).Items.EnumerateArray());
        Assert.Empty(HistoryPage(await History(draft, subject: "actor-b")).Items.EnumerateArray());
        Assert.Empty(HistoryPage(await History(draft, subject: "actor-revoked")).Items.EnumerateArray());
        Assert.Equal(403, Status(await History(draft, useDefaultDeny: true)));
        Assert.Equal(400, Status(await History(draft, before: 0)));
        Assert.Empty(HistoryPage(await History(Guid.NewGuid())).Items.EnumerateArray());
    }

    [Fact]
    public async Task History_rejects_writer_privileged_and_set_role_escalation_connections()
    {
        if (!Enabled()) return;
        foreach (var connection in new[] { RuntimeConnection, AdminConnection, EscalationConnection })
        {
            var config = new ConfigurationBuilder().AddInMemoryCollection(
                new Dictionary<string, string?> { ["CENTER_DRAFT_READ_DATABASE_URL"] = connection }).Build();
            var result = await LocalPlanDraftHistoryEndpoint.ListHistory(
                Principal("actor-a"), config, TestReader, "center-a", Guid.NewGuid(), null,
                CancellationToken.None);
            Assert.Equal(503, Status(result));
        }
    }

    [Fact]
    public async Task Command_login_cannot_insert_unaudited_history_or_assume_definer_role()
    {
        if (!Enabled()) return;
        var draft = Guid.NewGuid();
        await using var db = new NpgsqlConnection(RuntimeConnection);
        await db.OpenAsync();
        await using (var insert = new NpgsqlCommand(
            """
            INSERT INTO programs.center_plan_draft_revisions
              (center_id,draft_id,revision,program_id,national_version,
               local_title,local_activities,actor_subject)
            VALUES ('center-a',@draft,1,'national-1',1,'Unaudited','[]','actor-a')
            """, db))
        {
            insert.Parameters.AddWithValue("draft", draft);
            var denied = await Assert.ThrowsAsync<PostgresException>(() => insert.ExecuteNonQueryAsync());
            Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, denied.SqlState);
        }
        await using (var audit = new NpgsqlCommand(
            """
            INSERT INTO governance.command_audit
              (audit_id,occurred_at,actor_subject_id,center_id,action_code,
               target_type,target_id,correlation_id,metadata)
            VALUES (gen_random_uuid(),now(),'actor-a','center-a',
                    'FORGED','draft',@draft,'forged','{}')
            """, db))
        {
            audit.Parameters.AddWithValue("draft", draft.ToString("D"));
            var denied = await Assert.ThrowsAsync<PostgresException>(() => audit.ExecuteNonQueryAsync());
            Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, denied.SqlState);
        }
        await using (var switchRole = new NpgsqlCommand(
            "SET ROLE kanoonyar_draft_command_owner", db))
        {
            var denied = await Assert.ThrowsAsync<PostgresException>(() => switchRole.ExecuteNonQueryAsync());
            Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, denied.SqlState);
        }
        Assert.Equal((0L, 0L, 0L), await Counts(draft));
    }

    [Fact]
    public async Task Command_rejects_scope_mismatch_and_inactive_member_without_partial_records()
    {
        if (!Enabled()) return;
        var draft = Guid.NewGuid();
        await using var db = new NpgsqlConnection(RuntimeConnection);
        await db.OpenAsync();
        async Task<PostgresException> TryDirectCommand(string center, string actor)
        {
            await using var command = new NpgsqlCommand(
                """
                SELECT programs.append_local_plan_draft_revision(
                  @center,@draft,0,'national-1',1,'Rejected','[]'::jsonb,
                  @actor,gen_random_uuid(),gen_random_uuid(),'raw-sql')
                """, db);
            command.Parameters.AddWithValue("center", center);
            command.Parameters.AddWithValue("draft", draft);
            command.Parameters.AddWithValue("actor", actor);
            return await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteScalarAsync());
        }
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege,
            (await TryDirectCommand("center-a", "actor-a")).SqlState);
        await using (var scope = new NpgsqlCommand(
            "SELECT set_config('kanoonyar.center_id','center-a',false)", db))
            await scope.ExecuteNonQueryAsync();
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege,
            (await TryDirectCommand("center-b", "actor-b")).SqlState);
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege,
            (await TryDirectCommand("center-a", "actor-revoked")).SqlState);
        Assert.Equal((0L, 0L, 0L), await Counts(draft));
    }

    [Fact]
    public async Task Function_owner_is_nonlogin_and_required_function_is_security_definer()
    {
        if (!Enabled()) return;
        await using var db = new NpgsqlConnection(AdminConnection);
        await db.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            SELECT NOT owner.rolcanlogin AND NOT owner.rolsuper AND NOT owner.rolbypassrls
                   AND p.prosecdef
                   AND p.proconfig @> ARRAY['search_path=pg_catalog, pg_temp']
            FROM pg_proc p
            JOIN pg_roles owner ON owner.oid=p.proowner
            WHERE p.oid='programs.append_local_plan_draft_revision(
              text,uuid,bigint,text,integer,text,jsonb,text,uuid,uuid,text)'::regprocedure
            """, db);
        Assert.Equal(true, await command.ExecuteScalarAsync());
    }

    [Fact]
    public async Task Real_issuance_trace_preserves_original_revision_and_rejects_other_centers()
    {
        if (!Enabled()) return;
        var draft = Guid.NewGuid();
        var issuance = Guid.NewGuid();
        var link = Guid.NewGuid();
        Assert.Equal(200, Status(await Write(draft)));
        await using (var db = new NpgsqlConnection(AdminConnection))
        {
            await db.OpenAsync();
            await using (var issue = new NpgsqlCommand(
                """
                INSERT INTO programs.national_program_issuances
                 (issuance_id,center_id,program_id,national_version,issued_by_subject,issued_at)
                VALUES (@issuance,'center-a','national-1',1,'fixture',now())
                ON CONFLICT (center_id,program_id,national_version) DO NOTHING
                """, db))
            {
                issue.Parameters.AddWithValue("issuance", issuance);
                await issue.ExecuteNonQueryAsync();
            }
            // Tests share the same disposable database: issuance is unique by
            // center+program+version, independent of the randomly generated UUID.
            await using (var lookup = new NpgsqlCommand(
                """
                SELECT issuance_id FROM programs.national_program_issuances
                WHERE center_id='center-a' AND program_id='national-1'
                  AND national_version=1
                """, db))
            {
                issuance = Assert.IsType<Guid>(await lookup.ExecuteScalarAsync());
            }
            await using (var record = new NpgsqlCommand(
                """
                SELECT programs.record_center_plan_issuance_link(
                  @link,'center-a',@draft,1,@issuance,'national-1',1,'fixture',
                  @audit,@event,'integration-trace')
                """, db))
            {
                record.Parameters.AddWithValue("link", link);
                record.Parameters.AddWithValue("draft", draft);
                record.Parameters.AddWithValue("issuance", issuance);
                record.Parameters.AddWithValue("audit", Guid.NewGuid());
                record.Parameters.AddWithValue("event", Guid.NewGuid());
                Assert.Equal(link, await record.ExecuteScalarAsync());
            }
        }

        async Task<IResult> Trace(string actor, string center,
            ILocalPlanDraftReadAuthorization? grant = null)
            => await CenterExecutionTraceEndpoint.GetTrace(
                Principal(actor), Configuration(), grant ?? TestReader, center, draft,
                CancellationToken.None);
        JsonElement Payload(IResult result)
        {
            Assert.Equal(200, Status(result));
            using var json = JsonDocument.Parse(JsonSerializer.Serialize(
                Assert.IsAssignableFrom<IValueHttpResult>(result).Value));
            return json.RootElement.GetProperty("trace").Clone();
        }
        var first = Payload(await Trace("actor-a", "center-a"));
        Assert.Equal(link, first.GetProperty("linkId").GetGuid());
        Assert.Equal(issuance, first.GetProperty("issuanceId").GetGuid());
        Assert.Equal(1, first.GetProperty("pinnedRevision").GetInt64());
        Assert.Equal(1, first.GetProperty("latestRevision").GetInt64());
        Assert.Equal(200, Status(await Write(draft, expectedRevision: 1)));
        var updated = Payload(await Trace("actor-a", "center-a"));
        Assert.Equal(1, updated.GetProperty("pinnedRevision").GetInt64());
        Assert.Equal(2, updated.GetProperty("latestRevision").GetInt64());
        Assert.Equal(JsonValueKind.Null, Payload(await Trace("actor-b", "center-a")).ValueKind);
        Assert.Equal(JsonValueKind.Null, Payload(await Trace("actor-revoked", "center-a")).ValueKind);
        Assert.Equal(JsonValueKind.Null, Payload(await Trace("actor-a", "center-b")).ValueKind);
        Assert.Equal(403, Status(await Trace("actor-a", "center-a",
            new UnapprovedLocalPlanDraftReadAuthorization())));
        Assert.Equal((2L, 2L, 2L), await Counts(draft));
        await using var admin = new NpgsqlConnection(AdminConnection);
        await admin.OpenAsync();
        await using var cardinality = new NpgsqlCommand(
            """
            SELECT (SELECT count(*) FROM governance.command_audit WHERE target_id=@id),
                   (SELECT count(*) FROM governance.outbox_events WHERE aggregate_id=@id)
            """, admin);
        cardinality.Parameters.AddWithValue("id", link.ToString("D"));
        await using var reader = await cardinality.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(1L, reader.GetInt64(0));
        Assert.Equal(1L, reader.GetInt64(1));
    }

    [Fact]
    public async Task Trace_refuses_a_privileged_or_command_capable_read_login()
    {
        if (!Enabled()) return;
        foreach (var url in new[] { AdminConnection, RuntimeConnection, EscalationConnection })
        {
            var cfg = new ConfigurationBuilder().AddInMemoryCollection(
                new Dictionary<string,string?> { ["CENTER_DRAFT_READ_DATABASE_URL"] = url }).Build();
            var result = await CenterExecutionTraceEndpoint.GetTrace(
                Principal("actor-a"), cfg, TestReader, "center-a",
                Guid.NewGuid(), CancellationToken.None);
            Assert.Equal(503, Status(result));
        }
    }

    [Fact]
    public async Task Unverified_reports_have_real_paginated_postgres_lineage_and_are_not_cross_center_visible()
    {
        if (!Enabled()) return;
        var draft = Guid.NewGuid();
        var issuance = Guid.NewGuid();
        var link = Guid.NewGuid();
        Assert.Equal(200, Status(await Write(draft)));
        await using (var db = new NpgsqlConnection(AdminConnection))
        {
            await db.OpenAsync();
            await using (var issue = new NpgsqlCommand(
                """
                INSERT INTO programs.national_program_issuances
                 (issuance_id,center_id,program_id,national_version,issued_by_subject,issued_at)
                VALUES(@id,'center-a','national-1',1,'fixture',now())
                ON CONFLICT (center_id,program_id,national_version) DO NOTHING
                """, db))
            {
                issue.Parameters.AddWithValue("id", issuance);
                await issue.ExecuteNonQueryAsync();
            }
            // Tests share the same disposable database: issuance is unique by
            // center+program+version, independent of the randomly generated UUID.
            await using (var lookup = new NpgsqlCommand(
                """
                SELECT issuance_id FROM programs.national_program_issuances
                WHERE center_id='center-a' AND program_id='national-1'
                  AND national_version=1
                """, db))
            {
                issuance = Assert.IsType<Guid>(await lookup.ExecuteScalarAsync());
            }
            await using (var attach = new NpgsqlCommand(
                """
                SELECT programs.record_center_plan_issuance_link(
                 @link,'center-a',@draft,1,@issuance,'national-1',1,
                 'fixture',@audit,@event,'report-link')
                """, db))
            {
                attach.Parameters.AddWithValue("link", link);
                attach.Parameters.AddWithValue("draft", draft);
                attach.Parameters.AddWithValue("issuance", issuance);
                attach.Parameters.AddWithValue("audit", Guid.NewGuid());
                attach.Parameters.AddWithValue("event", Guid.NewGuid());
                Assert.Equal(link, await attach.ExecuteScalarAsync());
            }
            for (int i=0;i<22;i++)
            {
                await using var report = new NpgsqlCommand(
                    """
                    SELECT programs.record_center_activity_report(
                     @report,'center-a',@link,@draft,'class',@source,
                     @evidence,'Unverified reported activity',NULL,'fixture',
                     @audit,@event,'report-event')
                    """, db);
                var reportId = Guid.NewGuid();
                report.Parameters.AddWithValue("report", reportId);
                report.Parameters.AddWithValue("link", link);
                report.Parameters.AddWithValue("draft", draft);
                report.Parameters.AddWithValue("source", "synthetic-class-" + i);
                report.Parameters.AddWithValue("evidence", "synthetic-evidence-" + i);
                report.Parameters.AddWithValue("audit", Guid.NewGuid());
                report.Parameters.AddWithValue("event", Guid.NewGuid());
                Assert.Equal(reportId, await report.ExecuteScalarAsync());
            }
        }
        async Task<IResult> ReadReport(string subject = "actor-a", string center = "center-a",
            long? before = null, ICenterActivityReportReadAuthorization? auth = null,
            IConfiguration? config = null) =>
            await CenterActivityReportReadEndpoint.ListReports(Principal(subject),
                config ?? Configuration(), auth ?? new TestOnlyActivityReadGrant(),
                center, draft, before, CancellationToken.None);
        (JsonElement Items, long? Next) Page(IResult result)
        {
            Assert.Equal(200, Status(result));
            using var json = JsonDocument.Parse(JsonSerializer.Serialize(
                Assert.IsAssignableFrom<IValueHttpResult>(result).Value));
            var data = json.RootElement;
            Assert.Equal(draft, data.GetProperty("draftId").GetGuid());
            var next = data.GetProperty("nextBeforeSequence");
            return (data.GetProperty("reports").Clone(),
                next.ValueKind == JsonValueKind.Null ? null : next.GetInt64());
        }
        var first = Page(await ReadReport());
        Assert.Equal(20, first.Items.GetArrayLength());
        Assert.True(first.Next.HasValue);
        Assert.All(first.Items.EnumerateArray(), item => {
            Assert.Equal("UNVERIFIED", item.GetProperty("verificationStatus").GetString());
            Assert.Equal(JsonValueKind.Null, item.GetProperty("claimedOccurredAt").ValueKind);
            Assert.Equal(link, item.GetProperty("linkId").GetGuid());
        });
        var second = Page(await ReadReport(before:first.Next));
        Assert.Equal(2, second.Items.GetArrayLength());
        Assert.Null(second.Next);
        Assert.True(first.Items[19].GetProperty("reportSequence").GetInt64() >
            second.Items[0].GetProperty("reportSequence").GetInt64());
        Assert.Empty(Page(await ReadReport(subject:"actor-b")).Items.EnumerateArray());
        Assert.Empty(Page(await ReadReport(subject:"actor-revoked")).Items.EnumerateArray());
        Assert.Empty(Page(await ReadReport(center:"center-b")).Items.EnumerateArray());
        Assert.Equal(403, Status(await ReadReport(
            auth:new UnapprovedCenterActivityReportReadAuthorization())));
        Assert.Equal(400, Status(await ReadReport(before:0)));
        foreach (var restricted in new [] { AdminConnection, RuntimeConnection, EscalationConnection })
        {
            var config = new ConfigurationBuilder().AddInMemoryCollection(
                new Dictionary<string,string?> {
                    ["CENTER_DRAFT_READ_DATABASE_URL"] = restricted
                }).Build();
            Assert.Equal(503, Status(await ReadReport(config:config)));
        }
        await using var verification = new NpgsqlConnection(AdminConnection);
        await verification.OpenAsync();
        await using var count = new NpgsqlCommand(
            """
            SELECT (SELECT count(*) FROM programs.center_activity_reports WHERE draft_id=@draft),
                   (SELECT count(*) FROM governance.command_audit
                     WHERE target_type='center_activity_report' AND target_id IN
                       (SELECT report_id::text FROM programs.center_activity_reports WHERE draft_id=@draft)),
                   (SELECT count(*) FROM governance.outbox_events
                     WHERE aggregate_type='center_activity_report' AND aggregate_id IN
                       (SELECT report_id::text FROM programs.center_activity_reports WHERE draft_id=@draft))
            """, verification);
        count.Parameters.AddWithValue("draft", draft);
        await using var reader = await count.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(22L, reader.GetInt64(0));
        Assert.Equal(22L, reader.GetInt64(1));
        Assert.Equal(22L, reader.GetInt64(2));
    }

    private sealed class TestOnlyActivityReadGrant : ICenterActivityReportReadAuthorization
    {
        public ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal actor, string centerId,
            CancellationToken ct) => ValueTask.FromResult(true);
    }

    [Fact]
    public async Task Real_source_owner_tables_are_paginated_and_isolated_at_http_handler_boundary()
    {
        if (!Enabled()) return;
        var centerClass = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var prefix = Guid.NewGuid().ToString("N")[..8];
        var sessions = Enumerable.Range(1, 22).Select(i =>
            Guid.Parse($"{prefix}-0000-4000-8000-{i:000000000000}")).ToArray();
        await using (var db = new NpgsqlConnection(AdminConnection))
        {
            await db.OpenAsync();
            await using (var createClass = new NpgsqlCommand(
                """
                INSERT INTO learning.center_classes(center_id,class_id,title)
                VALUES('center-a',@id,'Source class from owner')
                """,db))
            {
                createClass.Parameters.AddWithValue("id", centerClass);
                await createClass.ExecuteNonQueryAsync();
            }
            for (int i=0;i<sessions.Length;i++)
            {
                await using var createSession = new NpgsqlCommand(
                    """
                    INSERT INTO learning.class_sessions(
                      center_id,session_id,class_id,session_title,scheduled_at)
                    VALUES('center-a',@id,@class,@title,NULL)
                    """,db);
                createSession.Parameters.AddWithValue("id", sessions[i]);
                createSession.Parameters.AddWithValue("class", centerClass);
                createSession.Parameters.AddWithValue("title", $"Synthetic session {i+1}");
                await createSession.ExecuteNonQueryAsync();
            }
            await using var createEvent = new NpgsqlCommand(
                """
                INSERT INTO events.center_events(center_id,event_id,event_title,scheduled_at)
                VALUES('center-a',@id,'Source event from owner',NULL)
                """,db);
            createEvent.Parameters.AddWithValue("id", eventId);
            await createEvent.ExecuteNonQueryAsync();
        }

        async Task<IResult> ReadSources(string kind, string actor="actor-a",
            string center="center-a", Guid? before=null,
            ICenterSourceCatalogReadAuthorization? grant=null, IConfiguration? config=null)
            => await CenterSourceCatalogEndpoint.ListSources(
                Principal(actor),config ?? Configuration(),grant ?? new TestSourceGrant(),
                center,kind,before,CancellationToken.None);
        (JsonElement Rows, Guid? Next) Page(IResult result,string kind)
        {
            Assert.Equal(200,Status(result));
            using var doc=JsonDocument.Parse(JsonSerializer.Serialize(
                Assert.IsAssignableFrom<IValueHttpResult>(result).Value));
            Assert.Equal(kind,doc.RootElement.GetProperty("sourceKind").GetString());
            var next=doc.RootElement.GetProperty("nextBeforeSourceId");
            return (doc.RootElement.GetProperty("records").Clone(),
                next.ValueKind==JsonValueKind.Null ? null : next.GetGuid());
        }
        var first=Page(await ReadSources("class-sessions"),"CLASS_SESSION");
        Assert.Equal(20,first.Rows.GetArrayLength());
        Assert.Equal(sessions[21],first.Rows[0].GetProperty("sourceId").GetGuid());
        Assert.Equal(sessions[2],first.Next);
        Assert.All(first.Rows.EnumerateArray(),row=> {
            Assert.Equal(centerClass,row.GetProperty("classId").GetGuid());
            Assert.Equal(JsonValueKind.Null,row.GetProperty("scheduledAt").ValueKind);
        });
        var second=Page(await ReadSources("class-sessions",before:first.Next),"CLASS_SESSION");
        Assert.Equal(2,second.Rows.GetArrayLength());
        Assert.Equal(sessions[1],second.Rows[0].GetProperty("sourceId").GetGuid());
        Assert.Equal(sessions[0],second.Rows[1].GetProperty("sourceId").GetGuid());
        Assert.Null(second.Next);
        var events=Page(await ReadSources("events"),"CENTER_EVENT");
        Assert.Single(events.Rows.EnumerateArray());
        Assert.Equal(eventId,events.Rows[0].GetProperty("sourceId").GetGuid());
        Assert.Equal(JsonValueKind.Null,events.Rows[0].GetProperty("classId").ValueKind);
        Assert.Empty(Page(await ReadSources("events",actor:"actor-revoked"),"CENTER_EVENT").Rows.EnumerateArray());
        Assert.Empty(Page(await ReadSources("class-sessions",actor:"actor-b"),"CLASS_SESSION").Rows.EnumerateArray());
        Assert.Empty(Page(await ReadSources("events",center:"center-b"),"CENTER_EVENT").Rows.EnumerateArray());
        Assert.Equal(403,Status(await ReadSources("events",grant:new UnapprovedCenterSourceCatalogReadAuthorization())));
        Assert.Equal(400,Status(await ReadSources("not-registered")));
        Assert.Equal(400,Status(await ReadSources("events",before:Guid.Empty)));
        foreach(var unsafeDb in new []{AdminConnection,RuntimeConnection,EscalationConnection})
        {
            var config=new ConfigurationBuilder().AddInMemoryCollection(
                new Dictionary<string,string?> {
                    ["CENTER_DRAFT_READ_DATABASE_URL"]=unsafeDb
                }).Build();
            Assert.Equal(503,Status(await ReadSources("events",config:config)));
        }
    }

    private sealed class TestSourceGrant : ICenterSourceCatalogReadAuthorization
    {
        public ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal principal, string centerId,
            CancellationToken ct) => ValueTask.FromResult(true);
    }

    private sealed class TestOnlyWriteGrant : ILocalPlanDraftWriteAuthorization
    {
        public ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal principal, string centerId,
            Guid draftId, CancellationToken ct) => ValueTask.FromResult(true);
    }
    private sealed class TestOnlyReadGrant : ILocalPlanDraftReadAuthorization
    {
        public ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal principal, string centerId,
            CancellationToken ct) => ValueTask.FromResult(true);
    }
}
