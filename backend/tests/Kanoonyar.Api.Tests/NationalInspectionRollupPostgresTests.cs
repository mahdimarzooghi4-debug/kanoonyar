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

        for (var i = 1; i <= 22; i++)
        {
            var programId = prefix + "-" + i.ToString("D2", System.Globalization.CultureInfo.InvariantCulture);
            await using var capture = new NpgsqlCommand(
                """
                SELECT programs.capture_national_issuance_review_intent(
                  @request,@program,1,ARRAY['center-a']::text[],
                  'UNVERIFIED-s84-ci-fixture','Synthetic API pagination only',
                  @audit,@event,@correlation)
                """, admin, transaction);
            capture.Parameters.AddWithValue("request", Guid.NewGuid());
            capture.Parameters.AddWithValue("program", programId);
            capture.Parameters.AddWithValue("audit", Guid.NewGuid());
            capture.Parameters.AddWithValue("event", Guid.NewGuid());
            capture.Parameters.AddWithValue("correlation", Guid.NewGuid().ToString("N"));
            await capture.ExecuteNonQueryAsync();
        }
        await transaction.CommitAsync();
        return prefix;
    }

    private static async Task<JsonElement> Page(IConfiguration configuration, string? beforeProgramId = null,
        int? beforeVersion = null)
    {
        var context = new DefaultHttpContext();
        if (beforeProgramId is not null && beforeVersion.HasValue)
            context.Request.QueryString = QueryString.Create(new[]
            {
                new KeyValuePair<string,string?>("beforeProgramId",beforeProgramId),
                new KeyValuePair<string,string?>("beforeNationalVersion",
                    beforeVersion.Value.ToString(System.Globalization.CultureInfo.InvariantCulture))
            });
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
            foreach (var row in fixtures)
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
            Assert.True(pages >= 2);
        }
        finally
        {
            await CleanupTestReader(AdminUrl);
        }
    }
}
