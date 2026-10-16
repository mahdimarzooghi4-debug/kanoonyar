using System.Data;
using System.Security.Claims;
using System.Text.Json;
using Kanoonyar.Api.Programs;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Xunit;

namespace Kanoonyar.Api.Tests;

/// <summary>End-to-end S84 reader exercise against disposable synthetic PostgreSQL only.</summary>
[CollectionDefinition("National inspection rollup PostgreSQL", DisableParallelization = true)]
public sealed class NationalInspectionRollupPostgresCollection { }

[Collection("National inspection rollup PostgreSQL")]
public sealed class NationalInspectionRollupPostgresTests
{
    private const string ReaderRole = "kanoonyar_s84_rollup_reader_ci";
    private const string ReadPolicy = "s84_rollup_test_read";
    private static string? AdminUrl => Environment.GetEnvironmentVariable("CENTER_DRAFT_INTEGRATION_ADMIN_URL");
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private sealed class TestOnlyAllowAuthorization : INationalInspectionRollupReadAuthorization
    {
        public ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal actor, CancellationToken cancellationToken) =>
            ValueTask.FromResult(true);
    }

    private static ClaimsPrincipal Actor() =>
        new(new ClaimsIdentity([new Claim("sub", "s84-synthetic-reader")], "synthetic-test"));

    private static string DatabaseIdentifier(string database) =>
        "\"" + database.Replace("\"", "\"\"") + "\"";

    private static async Task ProvisionTestReader(string adminUrl)
    {
        var builder = new NpgsqlConnectionStringBuilder(adminUrl);
        var dbIdentifier = DatabaseIdentifier(builder.Database);
        await using var admin = new NpgsqlConnection(adminUrl);
        await admin.OpenAsync();
        await using var command = new NpgsqlCommand($$"""
            DO $role$ BEGIN
              IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname='{{ReaderRole}}') THEN
                CREATE ROLE {{ReaderRole}} LOGIN NOINHERIT NOSUPERUSER NOBYPASSRLS
                  NOCREATEDB NOCREATEROLE NOREPLICATION;
              END IF;
            END;$role$;
            GRANT CONNECT ON DATABASE {{dbIdentifier}} TO {{ReaderRole}};
            GRANT USAGE ON SCHEMA programs,centers,governance,learning,events TO {{ReaderRole}};
            GRANT SELECT ON ALL TABLES IN SCHEMA programs,centers,governance TO {{ReaderRole}};
            DO $policies$ DECLARE r record;
            BEGIN
              FOR r IN SELECT n.nspname,c.relname
                FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
                WHERE n.nspname=ANY(ARRAY['programs','centers','governance'])
                  AND c.relkind IN ('r','p') AND c.relrowsecurity
              LOOP
                EXECUTE format('DROP POLICY IF EXISTS %I ON %I.%I',
                  '{{ReadPolicy}}',r.nspname,r.relname);
                EXECUTE format('CREATE POLICY %I ON %I.%I FOR SELECT TO %I USING (true)',
                  '{{ReadPolicy}}',r.nspname,r.relname,'{{ReaderRole}}');
              END LOOP;
            END;$policies$;
            """, admin);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task CleanupTestReader(string adminUrl)
    {
        await using var admin = new NpgsqlConnection(adminUrl);
        await admin.OpenAsync();
        await using var command = new NpgsqlCommand($$"""
            DO $policies$ DECLARE r record;
            BEGIN
              FOR r IN SELECT schemaname,tablename FROM pg_policies
                WHERE policyname='{{ReadPolicy}}'
              LOOP
                EXECUTE format('DROP POLICY IF EXISTS %I ON %I.%I',
                  '{{ReadPolicy}}',r.schemaname,r.tablename);
              END LOOP;
            END;$policies$;
            DROP OWNED BY {{ReaderRole}};
            DROP ROLE IF EXISTS {{ReaderRole}};
            """, admin);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<string> SeedSyntheticProgramVersions(string adminUrl)
    {
        var prefix = "s84-api-" + Guid.NewGuid().ToString("N");
        await using var admin = new NpgsqlConnection(adminUrl);
        await admin.OpenAsync();
        await using var transaction = await admin.BeginTransactionAsync(IsolationLevel.RepeatableRead);
        var centerId = prefix + "-center";
        await using (var center = new NpgsqlCommand(
            """
            INSERT INTO centers.registry
              (center_id,display_name,is_active,registered_by_subject,registered_at)
            VALUES (@center,'S92 synthetic drift center',true,'s92-ci-fixture',now())
            """, admin, transaction))
        {
            center.Parameters.AddWithValue("center", centerId);
            await center.ExecuteNonQueryAsync();
        }
        await using (var versions = new NpgsqlCommand(
            """
            INSERT INTO programs.national_program_versions
              (program_id,version,title,national_objectives,mandatory_requirements,
               created_by_subject,created_at)
            SELECT @prefix||'-'||lpad(n::text,2,'0'),1,'S84 synthetic API fixture',
              '["synthetic objective"]'::jsonb,'["synthetic requirement"]'::jsonb,
              's84-ci-fixture',now()
            FROM generate_series(1,22) n;
            INSERT INTO programs.national_program_publications
              (program_id,version,publisher_subject,published_at)
            SELECT @prefix||'-'||lpad(n::text,2,'0'),1,'s84-ci-fixture',now()
            FROM generate_series(1,22) n;
            """, admin, transaction))
        {
            versions.Parameters.AddWithValue("prefix", prefix);
            await versions.ExecuteNonQueryAsync();
        }

        Guid driftRequestId = Guid.Empty;
        Guid findingRequestId = Guid.Empty;
        for (var i = 1; i <= 22; i++)
        {
            var programId = prefix + "-" + i.ToString("D2", System.Globalization.CultureInfo.InvariantCulture);
            var requestId = Guid.NewGuid();
            if (i == 1) driftRequestId = requestId;
            if (i == 2) findingRequestId = requestId;
            await using var capture = new NpgsqlCommand(
                """
                SELECT programs.capture_national_issuance_review_intent(
                  @request,@program,1,ARRAY[@center]::text[],
                  'UNVERIFIED-s84-ci-fixture','Synthetic API pagination only',
                  @audit,@event,@correlation)
                """, admin, transaction);
            capture.Parameters.AddWithValue("request", requestId);
            capture.Parameters.AddWithValue("program", programId);
            capture.Parameters.AddWithValue("center", centerId);
            capture.Parameters.AddWithValue("audit", Guid.NewGuid());
            capture.Parameters.AddWithValue("event", Guid.NewGuid());
            capture.Parameters.AddWithValue("correlation", Guid.NewGuid().ToString("N"));
            await capture.ExecuteNonQueryAsync();
        }
        if (driftRequestId == Guid.Empty || findingRequestId == Guid.Empty)
            throw new InvalidOperationException("S92 synthetic requests were not captured");
        await using (var note = new NpgsqlCommand(
            """
            SELECT programs.capture_national_issuance_review_inspection_note(
              @note,@request,'UNVERIFIED-s92-ci-fixture',
              'Synthetic source freshness observation only',@audit,@event,@correlation)
            """, admin, transaction))
        {
            note.Parameters.AddWithValue("note", Guid.NewGuid());
            note.Parameters.AddWithValue("request", driftRequestId);
            note.Parameters.AddWithValue("audit", Guid.NewGuid());
            note.Parameters.AddWithValue("event", Guid.NewGuid());
            note.Parameters.AddWithValue("correlation", Guid.NewGuid().ToString("N"));
            await note.ExecuteNonQueryAsync();
        }

        await using (var invalidEvidence = new NpgsqlCommand(
            """
            INSERT INTO programs.national_issuance_review_inspection_notes
              (note_id,request_id,pinned_request_digest_sha256,observed_source_snapshot,
               observed_digest_sha256,source_changed_since_request,
               unverified_inspector_subject,technical_note)
            SELECT @note,request_id,source_digest_sha256,original_source_snapshot,
              source_digest_sha256,false,'UNVERIFIED-s93-ci-fixture',
              'Synthetic missing-audit finding fixture'
            FROM programs.national_issuance_review_intents
            WHERE request_id=@request
            """, admin, transaction))
        {
            invalidEvidence.Parameters.AddWithValue("note", Guid.NewGuid());
            invalidEvidence.Parameters.AddWithValue("request", findingRequestId);
            Assert.Equal(1, await invalidEvidence.ExecuteNonQueryAsync());
        }

        await transaction.CommitAsync();
        await using (var drift = new NpgsqlCommand(
            "UPDATE centers.registry SET is_active=false WHERE center_id=@center", admin))
        {
            drift.Parameters.AddWithValue("center", centerId);
            Assert.Equal(1, await drift.ExecuteNonQueryAsync());
        }
        return prefix;
    }

    private static async Task<JsonElement> Page(IConfiguration configuration, string? beforeProgramId = null,
        int? beforeVersion = null, bool findingsOnly = false)
    {
        var context = new DefaultHttpContext();
        var query = new List<KeyValuePair<string,string?>>();
        if (beforeProgramId is not null && beforeVersion.HasValue)
        {
            query.Add(new("beforeProgramId", beforeProgramId));
            query.Add(new("beforeNationalVersion",
                beforeVersion.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }
        if (findingsOnly) query.Add(new("findingsOnly", "true"));
        if (query.Count > 0) context.Request.QueryString = QueryString.Create(query);
        var result = await NationalInspectionRollupEndpoint.List(context, Actor(), configuration,
            new TestOnlyAllowAuthorization(), CancellationToken.None);
        Assert.Equal(200, (result as IStatusCodeHttpResult)?.StatusCode);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(
            Assert.IsAssignableFrom<IValueHttpResult>(result).Value, JsonOptions));
        return json.RootElement.Clone();
    }

    [Fact]
    public async Task Restricted_API_reader_paginates_all_rollup_groups_without_private_fields()
    {
        var required = Environment.GetEnvironmentVariable("CENTER_DRAFT_INTEGRATION_REQUIRED") == "1";
        if (required) Assert.False(string.IsNullOrWhiteSpace(AdminUrl));
        if (string.IsNullOrWhiteSpace(AdminUrl)) return;

        await ProvisionTestReader(AdminUrl);
        try
        {
            var prefix = await SeedSyntheticProgramVersions(AdminUrl);
            var readBuilder = new NpgsqlConnectionStringBuilder(AdminUrl)
            {
                Username = ReaderRole,
                Pooling = false
            };
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["NATIONAL_INSPECTION_ROLLUP_READ_DATABASE_URL"] = readBuilder.ConnectionString
                }).Build();

            var all = new List<JsonElement>();
            string? beforeId = null;
            int? beforeVersion = null;
            var pages = 0;
            do
            {
                pages++;
                Assert.True(pages <= 10, "Keyset pagination repeated or failed to advance");
                var response = await Page(configuration, beforeId, beforeVersion);
                var rows = response.GetProperty("rollups").EnumerateArray().ToArray();
                Assert.InRange(rows.Length, 1, 20);
                foreach (var row in rows)
                {
                    Assert.Equal("S90_NATIONAL_INSPECTION_ROLLUP_V2",
                        row.GetProperty("rollupContractVersion").GetString());
                    Assert.False(row.GetProperty("humanReviewRecorded").GetBoolean());
                    Assert.False(row.GetProperty("issuanceAuthorized").GetBoolean());
                    Assert.False(row.GetProperty("issuanceExecuted").GetBoolean());
                    foreach (var privateField in new[]
                    {
                        "centerId","requestId","noteId","explicitCenterIds",
                        "originalSourceSnapshot","observedSourceSnapshot",
                        "technicalNote","unverifiedInspectorSubject"
                    })
                        Assert.False(row.TryGetProperty(privateField, out _));
                }
                all.AddRange(rows);
                var next = response.GetProperty("nextCursor");
                if (next.ValueKind == JsonValueKind.Null)
                {
                    beforeId = null;
                    beforeVersion = null;
                    break;
                }
                beforeId = next.GetProperty("beforeProgramId").GetString();
                beforeVersion = next.GetProperty("beforeNationalVersion").GetInt32();
            } while (beforeId is not null);

            var fixtures = all.Where(x => x.GetProperty("programId").GetString()!.StartsWith(
                prefix + "-", StringComparison.Ordinal)).ToArray();
            Assert.Equal(22, fixtures.Length);
            Assert.Equal(22, fixtures.Select(x => x.GetProperty("programId").GetString())
                .Distinct(StringComparer.Ordinal).Count());
            var driftRow = Assert.Single(fixtures.Where(x =>
                x.GetProperty("programId").GetString() == prefix + "-01"));
            Assert.Equal(1, driftRow.GetProperty("reviewRequestCount").GetInt64());
            Assert.Equal(1, driftRow.GetProperty("inspectionNoteCount").GetInt64());
            Assert.Equal(0, driftRow.GetProperty("currentSourceMatchNoteCount").GetInt64());
            Assert.Equal(1, driftRow.GetProperty("currentSourceDriftNoteCount").GetInt64());
            Assert.NotEqual(JsonValueKind.Null,
                driftRow.GetProperty("latestTechnicalObservationAt").ValueKind);
            Assert.False(driftRow.GetProperty("humanReviewRecorded").GetBoolean());
            Assert.False(driftRow.GetProperty("issuanceAuthorized").GetBoolean());
            Assert.False(driftRow.GetProperty("issuanceExecuted").GetBoolean());
            foreach (var row in fixtures.Where(x =>
                x.GetProperty("programId").GetString() != prefix + "-02"))
            {
                foreach (var componentCount in new[]
                {
                    "sourceSnapshotDigestInvalidRequestCount","sourceEnvelopeInvalidRequestCount",
                    "centerSelectionMismatchRequestCount","authorityFlagsNotClearRequestCount",
                    "auditEvidenceInvalidRequestCount","outboxEvidenceInvalidRequestCount",
                    "originalRequestIntegrityInvalidNoteCount","observationIntegrityInvalidNoteCount",
                    "inspectionAuditEvidenceInvalidNoteCount","sourceObservationNotComparableNoteCount"
                }) Assert.Equal(0, row.GetProperty(componentCount).GetInt64());
            }
            var finding = Assert.Single(fixtures.Where(x =>
                x.GetProperty("programId").GetString() == prefix + "-02"));
            Assert.Equal(1, finding.GetProperty("inspectionAuditEvidenceInvalidNoteCount").GetInt64());
            Assert.True(pages >= 2);

            var filtered = new List<JsonElement>();
            string? filterBeforeId = null;
            int? filterBeforeVersion = null;
            var filteredPages = 0;
            do
            {
                filteredPages++;
                Assert.True(filteredPages <= 10, "Filtered pagination repeated or failed to advance");
                var response = await Page(configuration, filterBeforeId, filterBeforeVersion, findingsOnly: true);
                var rows = response.GetProperty("rollups").EnumerateArray().ToArray();
                Assert.InRange(rows.Length, 1, 20);
                foreach (var row in rows)
                {
                    var hasCode = new[]
                    {
                        "sourceSnapshotDigestInvalidRequestCount","sourceEnvelopeInvalidRequestCount",
                        "centerSelectionMismatchRequestCount","authorityFlagsNotClearRequestCount",
                        "auditEvidenceInvalidRequestCount","outboxEvidenceInvalidRequestCount",
                        "originalRequestIntegrityInvalidNoteCount","observationIntegrityInvalidNoteCount",
                        "inspectionAuditEvidenceInvalidNoteCount","sourceObservationNotComparableNoteCount"
                    }.Any(field => row.GetProperty(field).GetInt64() > 0);
                    Assert.True(hasCode, "Findings-only API returned a clean group");
                    Assert.False(row.GetProperty("humanReviewRecorded").GetBoolean());
                    Assert.False(row.GetProperty("issuanceAuthorized").GetBoolean());
                    Assert.False(row.GetProperty("issuanceExecuted").GetBoolean());
                }
                filtered.AddRange(rows);
                var next = response.GetProperty("nextCursor");
                if (next.ValueKind == JsonValueKind.Null)
                {
                    filterBeforeId = null;
                    filterBeforeVersion = null;
                    break;
                }
                filterBeforeId = next.GetProperty("beforeProgramId").GetString();
                filterBeforeVersion = next.GetProperty("beforeNationalVersion").GetInt32();
            } while (filterBeforeId is not null);

            var filteredFinding = Assert.Single(filtered.Where(x =>
                x.GetProperty("programId").GetString() == prefix + "-02"));
            Assert.Equal(1, filteredFinding.GetProperty("inspectionAuditEvidenceInvalidNoteCount").GetInt64());
            Assert.DoesNotContain(filtered, x =>
                x.GetProperty("programId").GetString() == prefix + "-01");
        }
        finally
        {
            await CleanupTestReader(AdminUrl);
        }
    }
}
