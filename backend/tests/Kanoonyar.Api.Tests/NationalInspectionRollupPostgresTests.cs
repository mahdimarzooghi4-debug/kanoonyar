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

    private sealed class TestOnlyAllowAuthorization : INationalInspectionReadAuthorization
    {
        public ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal actor, NationalInspectionReadScope scope,
            CancellationToken cancellationToken) =>
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
        for (var i = 1; i <= 22; i++)
        {
            var programId = prefix + "-" + i.ToString("D2", System.Globalization.CultureInfo.InvariantCulture);
            var requestId = Guid.NewGuid();
            if (i == 1) driftRequestId = requestId;
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
        if (driftRequestId == Guid.Empty)
            throw new InvalidOperationException("S92 synthetic request was not captured");
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

        // This intentionally malformed historical row exists only in disposable CI so the
        // diagnostic view can be tested. Skip the deferred evidence-pair trigger for this
        // single synthetic insert; restore normal trigger mode before committing other data.
        await using (var triggerMode = new NpgsqlCommand(
            "SET LOCAL session_replication_role = replica", admin, transaction))
            await triggerMode.ExecuteNonQueryAsync();

        await using (var invalidEvidence = new NpgsqlCommand(
            """
            INSERT INTO programs.national_issuance_review_inspection_notes
              (note_id,request_id,pinned_request_digest_sha256,observed_source_snapshot,
               observed_digest_sha256,source_changed_since_request,
               unverified_inspector_subject,technical_note)
            SELECT gen_random_uuid(),request_id,source_digest_sha256,original_source_snapshot,
              source_digest_sha256,false,'UNVERIFIED-s93-ci-fixture',
              'Synthetic missing-audit finding fixture'
            FROM programs.national_issuance_review_intents
            WHERE program_id LIKE @prefix || '-%'
              AND program_id <> @prefix || '-01'
            """, admin, transaction))
        {
            invalidEvidence.Parameters.AddWithValue("prefix", prefix);
            Assert.Equal(21, await invalidEvidence.ExecuteNonQueryAsync());
        }

        await using (var restoreTriggerMode = new NpgsqlCommand(
            "SET LOCAL session_replication_role = origin", admin, transaction))
            await restoreTriggerMode.ExecuteNonQueryAsync();

        await transaction.CommitAsync();
        await using (var drift = new NpgsqlCommand(
            "UPDATE centers.registry SET is_active=false WHERE center_id=@center", admin))
        {
            drift.Parameters.AddWithValue("center", centerId);
            Assert.Equal(1, await drift.ExecuteNonQueryAsync());
        }
        return prefix;
    }

    private static async Task SeedCsvLimitGroups(string adminUrl)
    {
        var prefix = "s98-export-cap-" + Guid.NewGuid().ToString("N");
        await using var admin = new NpgsqlConnection(adminUrl);
        await admin.OpenAsync();
        await using var transaction = await admin.BeginTransactionAsync();
        await using (var triggerMode = new NpgsqlCommand(
            "SET LOCAL session_replication_role = replica", admin, transaction))
            await triggerMode.ExecuteNonQueryAsync();

        await using var command = new NpgsqlCommand(
            """
            WITH generated AS (
              SELECT n,gen_random_uuid() AS request_id,
                jsonb_build_object(
                  'formatVersion','S60_SOURCE_SNAPSHOT_V1',
                  'programId',@prefix,
                  'nationalVersion',n,
                  'exactPublicationExists',false,
                  'centers',jsonb_build_array(jsonb_build_object(
                    'centerId','s98-unregistered-fixture-center',
                    'registryExists',false,'registryActive',false,
                    'exactIssuanceExists',false))) AS snapshot
              FROM generate_series(1,10001) AS series(n)
            )
            INSERT INTO programs.national_issuance_review_intents
              (request_id,program_id,national_version,explicit_center_ids,
               original_source_snapshot,source_digest_sha256,
               stated_review_reason,unverified_requester_subject)
            SELECT request_id,@prefix,n,ARRAY['s98-unregistered-fixture-center']::text[],
              snapshot,encode(sha256(convert_to(snapshot::text,'UTF8')),'hex'),
              'Synthetic export cap test','s98-ci-export-cap'
            FROM generated
            """, admin, transaction);
        command.Parameters.AddWithValue("prefix", prefix);
        Assert.Equal(10001, await command.ExecuteNonQueryAsync());

        // The fixture intentionally omits the matching audit/outbox evidence so
        // every generated group remains a diagnostic-only integrity finding.
        await using (var restoreTriggerMode = new NpgsqlCommand(
            "SET LOCAL session_replication_role = origin", admin, transaction))
            await restoreTriggerMode.ExecuteNonQueryAsync();
        await transaction.CommitAsync();
    }

    private static async Task<JsonElement> Page(IConfiguration configuration, string? beforeProgramId = null,
        int? beforeVersion = null, bool findingsOnly = false, string? findingCode = null,
        string? targetProgramId = null, int? targetNationalVersion = null)
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
        if (findingCode is not null) query.Add(new("findingCode", findingCode));
        if (targetProgramId is not null && targetNationalVersion.HasValue)
        {
            query.Add(new("targetProgramId", targetProgramId));
            query.Add(new("targetNationalVersion",
                targetNationalVersion.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }
        if (query.Count > 0) context.Request.QueryString = QueryString.Create(query);
        var result = await NationalInspectionRollupEndpoint.List(context, Actor(), configuration,
            new TestOnlyAllowAuthorization(), CancellationToken.None);
        Assert.Equal(200, (result as IStatusCodeHttpResult)?.StatusCode);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(
            Assert.IsAssignableFrom<IValueHttpResult>(result).Value, JsonOptions));
        return json.RootElement.Clone();
    }

    private static async Task<JsonElement> Dossier(IConfiguration configuration, string programId, int version,
        string? beforeInspectedAt = null, Guid? beforeNoteId = null)
    {
        var context = new DefaultHttpContext();
        var query = new List<KeyValuePair<string,string?>>
        {
            new("programId", programId),
            new("nationalVersion", version.ToString(System.Globalization.CultureInfo.InvariantCulture))
        };
        if (beforeInspectedAt is not null && beforeNoteId.HasValue)
        {
            query.Add(new("beforeInspectedAt", beforeInspectedAt));
            query.Add(new("beforeNoteId", beforeNoteId.Value.ToString("D")));
        }
        context.Request.QueryString = QueryString.Create(query);
        var result = await NationalInspectionDossierEndpoint.List(context, Actor(), configuration,
            new TestOnlyAllowAuthorization(), CancellationToken.None);
        Assert.Equal(200, (result as IStatusCodeHttpResult)?.StatusCode);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(
            Assert.IsAssignableFrom<IValueHttpResult>(result).Value, JsonOptions));
        return json.RootElement.Clone();
    }

    private static async Task SeedAdditionalInspectionNotes(string adminUrl, string programId, int count)
    {
        await using var connection = new NpgsqlConnection(adminUrl);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead);
        Guid requestId;
        await using (var find = new NpgsqlCommand(
            "SELECT request_id FROM programs.national_issuance_review_intents WHERE program_id=@program AND national_version=1",
            connection, transaction))
        {
            find.Parameters.AddWithValue("program", programId);
            requestId = (Guid)(await find.ExecuteScalarAsync()
                ?? throw new InvalidOperationException("S101 synthetic request was not found"));
        }
        for (var index = 0; index < count; index++)
        {
            await using var capture = new NpgsqlCommand(
                """
                SELECT programs.capture_national_issuance_review_inspection_note(
                  @note,@request,'UNVERIFIED-s101-ci-fixture',
                  'Synthetic technical dossier pagination only',@audit,@event,@correlation)
                """, connection, transaction);
            capture.Parameters.AddWithValue("note", Guid.NewGuid());
            capture.Parameters.AddWithValue("request", requestId);
            capture.Parameters.AddWithValue("audit", Guid.NewGuid());
            capture.Parameters.AddWithValue("event", Guid.NewGuid());
            capture.Parameters.AddWithValue("correlation", Guid.NewGuid().ToString("N"));
            await capture.ExecuteNonQueryAsync();
        }
        await transaction.CommitAsync();
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
                    ["NATIONAL_INSPECTION_ROLLUP_READ_DATABASE_URL"] = readBuilder.ConnectionString,
                    ["NATIONAL_INSPECTION_DOSSIER_READ_DATABASE_URL"] = readBuilder.ConnectionString
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
                x.GetProperty("programId").GetString() == prefix + "-01"))
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
            Assert.Equal(21, fixtures.Count(x =>
                x.GetProperty("inspectionAuditEvidenceInvalidNoteCount").GetInt64() == 1));
            Assert.True(pages >= 2);

            await SeedAdditionalInspectionNotes(AdminUrl, prefix + "-01", 50);
            var dossierFirst = await Dossier(configuration, prefix + "-01", 1);
            Assert.Equal("S101_NATIONAL_INSPECTION_DOSSIER_V1",
                dossierFirst.GetProperty("contractVersion").GetString());
            var dossierRows = dossierFirst.GetProperty("inspections").EnumerateArray().ToArray();
            Assert.Equal(50, dossierRows.Length);
            Assert.All(dossierRows, row =>
            {
                Assert.Equal(15, row.EnumerateObject().Count());
                Assert.Equal(prefix + "-01", dossierFirst.GetProperty("programId").GetString());
                Assert.True(row.GetProperty("originalRequestIntegrityValid").GetBoolean());
                Assert.True(row.GetProperty("observationIntegrityValid").GetBoolean());
                Assert.True(row.GetProperty("auditAndOutboxIntegrityValid").GetBoolean());
                Assert.False(row.GetProperty("humanReviewRecorded").GetBoolean());
                Assert.False(row.GetProperty("issuanceAuthorized").GetBoolean());
                Assert.False(row.GetProperty("issuanceExecuted").GetBoolean());
                foreach (var privateField in new[]
                {
                    "centerId","explicitCenterIds","originalSourceSnapshot","observedSourceSnapshot",
                    "technicalNote","unverifiedInspectorSubject","originalSourceDigestSha256",
                    "pinnedRequestDigestSha256","observedDigestSha256"
                }) Assert.False(row.TryGetProperty(privateField, out _));
            });
            Assert.Equal(50, dossierRows.Select(row => row.GetProperty("noteId").GetGuid()).Distinct().Count());
            var dossierCursor = dossierFirst.GetProperty("nextCursor");
            Assert.NotEqual(JsonValueKind.Null, dossierCursor.ValueKind);
            var dossierSecond = await Dossier(configuration, prefix + "-01", 1,
                dossierCursor.GetProperty("beforeInspectedAt").GetString(),
                dossierCursor.GetProperty("beforeNoteId").GetGuid());
            var lastDossierRows = dossierSecond.GetProperty("inspections").EnumerateArray().ToArray();
            Assert.Single(lastDossierRows);
            Assert.Equal(JsonValueKind.Null, dossierSecond.GetProperty("nextCursor").ValueKind);
            Assert.DoesNotContain(lastDossierRows[0].GetProperty("noteId").GetGuid(),
                dossierRows.Select(row => row.GetProperty("noteId").GetGuid()));

            var dossierExportContext = new DefaultHttpContext();
            dossierExportContext.Request.QueryString = new QueryString(
                "?programId=" + Uri.EscapeDataString(prefix + "-01") + "&nationalVersion=1");
            var dossierExport = Assert.IsType<FileContentHttpResult>(
                await NationalInspectionDossierEndpoint.ExportCsv(dossierExportContext, Actor(),
                    configuration, new TestOnlyAllowAuthorization(), CancellationToken.None));
            Assert.Equal("text/csv; charset=utf-8", dossierExport.ContentType);
            Assert.Equal("national-inspection-dossier.csv", dossierExport.FileDownloadName);
            var dossierCsv = System.Text.Encoding.UTF8.GetString(dossierExport.FileContents.ToArray());
            Assert.StartsWith("\uFEFF", dossierCsv);
            Assert.Contains("\"program_id\",\"national_version\",\"request_id\",\"note_id\"", dossierCsv);
            Assert.Contains("\"" + prefix + "-01\"", dossierCsv);
            Assert.DoesNotContain("centerId", dossierCsv);
            Assert.DoesNotContain("explicitCenterIds", dossierCsv);
            Assert.DoesNotContain("technicalNote", dossierCsv);
            Assert.DoesNotContain("UNVERIFIED-s101-ci-fixture", dossierCsv);
            var dossierCsvRecords = dossierCsv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
            Assert.Equal(52, dossierCsvRecords.Length);
            Assert.All(dossierCsvRecords, record => Assert.Equal(17, record.Split(',').Length));
            Assert.Equal("S103_NATIONAL_INSPECTION_DOSSIER_CSV_RECEIPT_V1",
                dossierExportContext.Response.Headers["X-National-Inspection-Dossier-Export-Contract"].ToString());
            Assert.Equal("S103_NATIONAL_INSPECTION_DOSSIER_CSV_V1",
                dossierExportContext.Response.Headers["X-National-Inspection-Dossier-Export-Schema"].ToString());
            Assert.Equal("51",
                dossierExportContext.Response.Headers["X-National-Inspection-Dossier-Export-Row-Count"].ToString());
            Assert.Equal(
                Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(dossierExport.FileContents.Span))
                    .ToLowerInvariant(),
                dossierExportContext.Response.Headers["X-National-Inspection-Dossier-Export-SHA256"].ToString());

            var exactTarget = await Page(configuration, targetProgramId: prefix + "-02", targetNationalVersion: 1);
            var exactTargetRow = Assert.Single(exactTarget.GetProperty("rollups").EnumerateArray());
            Assert.Equal(prefix + "-02", exactTargetRow.GetProperty("programId").GetString());
            Assert.Equal(1, exactTargetRow.GetProperty("nationalVersion").GetInt32());
            var exactTargetFinding = await Page(configuration, findingsOnly: true,
                findingCode: "INSPECTION_AUDIT_EVIDENCE_INVALID",
                targetProgramId: prefix + "-02", targetNationalVersion: 1);
            Assert.Single(exactTargetFinding.GetProperty("rollups").EnumerateArray());
            var missingTarget = await Page(configuration,
                targetProgramId: prefix + "-missing", targetNationalVersion: 1);
            Assert.Empty(missingTarget.GetProperty("rollups").EnumerateArray());

            var exportContext = new DefaultHttpContext();
            exportContext.Request.QueryString = QueryString.Create(new[]
            {
                new KeyValuePair<string,string?>("findingsOnly", "true"),
                new KeyValuePair<string,string?>("findingCode", "INSPECTION_AUDIT_EVIDENCE_INVALID"),
                new KeyValuePair<string,string?>("targetProgramId", prefix + "-02"),
                new KeyValuePair<string,string?>("targetNationalVersion", "1")
            });
            var exportResult = Assert.IsType<FileContentHttpResult>(
                await NationalInspectionRollupEndpoint.ExportCsv(exportContext, Actor(),
                    configuration, new TestOnlyAllowAuthorization(), CancellationToken.None));
            Assert.Equal("text/csv; charset=utf-8", exportResult.ContentType);
            Assert.Equal("national-inspection-rollup.csv", exportResult.FileDownloadName);
            var csv = System.Text.Encoding.UTF8.GetString(exportResult.FileContents.ToArray());
            Assert.StartsWith("\uFEFF", csv);
            Assert.Contains("\"شناسه برنامه\"", csv);
            Assert.Contains("\"" + prefix + "-02\"", csv);
            Assert.DoesNotContain("centerId", csv);
            Assert.DoesNotContain("technicalNote", csv);
            Assert.DoesNotContain("explicitCenterIds", csv);
            var csvRecords = csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
            Assert.Equal(2, csvRecords.Length);
            Assert.Equal(new[]
            {
                "\"شناسه برنامه\"", "\"نسخه ملی\"", "\"درخواست\"", "\"بدون بازرسی\"",
                "\"با بازرسی\"", "\"یادداشت بازرسی\"", "\"یکپارچگی سازگار\"",
                "\"یکپارچگی ناسازگار\"", "\"مقایسه ناموجود\"", "\"منبع منطبق\"",
                "\"منبع متغیر\"", "\"تازگی نامشخص\"", "\"Digest نامعتبر درخواست\"",
                "\"ساختار نامعتبر درخواست\"", "\"عدم تطبیق انتخاب کانون\"",
                "\"پرچم اختیار نامشخص\"", "\"شواهد Audit نامعتبر درخواست\"",
                "\"شواهد Outbox نامعتبر درخواست\"", "\"یکپارچگی درخواست اصلی نامعتبر\"",
                "\"یکپارچگی مشاهده نامعتبر\"", "\"شواهد Audit بازرسی نامعتبر\"",
                "\"مشاهده غیرقابل‌مقایسه\"", "\"آخرین مشاهده فنی\""
            }, csvRecords[0].TrimStart('\uFEFF').Split(','));
            Assert.All(csvRecords, record => Assert.Equal(23, record.Split(',').Length));
            Assert.Equal("S98_NATIONAL_INSPECTION_CSV_RECEIPT_V1",
                exportContext.Response.Headers["X-National-Inspection-Export-Contract"].ToString());
            Assert.Equal("S100_NATIONAL_INSPECTION_CSV_SCHEMA_V1",
                exportContext.Response.Headers["X-National-Inspection-Export-Schema"].ToString());
            Assert.Equal("1",
                exportContext.Response.Headers["X-National-Inspection-Export-Row-Count"].ToString());
            Assert.Equal(
                Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(exportResult.FileContents.Span))
                    .ToLowerInvariant(),
                exportContext.Response.Headers["X-National-Inspection-Export-SHA256"].ToString());

            await SeedCsvLimitGroups(AdminUrl);
            var overLimitContext = new DefaultHttpContext();
            overLimitContext.Request.QueryString = new QueryString(
                "?findingsOnly=true&findingCode=OUTBOX_EVIDENCE_INVALID");
            var overLimit = await NationalInspectionRollupEndpoint.ExportCsv(overLimitContext, Actor(),
                configuration, new TestOnlyAllowAuthorization(), CancellationToken.None);
            var overLimitProblem = Assert.IsType<ProblemHttpResult>(overLimit);
            Assert.Equal(413, overLimitProblem.StatusCode);

            var filtered = new List<JsonElement>();
            string? filterBeforeId = null;
            int? filterBeforeVersion = null;
            var filteredPages = 0;
            do
            {
                filteredPages++;
                Assert.True(filteredPages <= 10, "Filtered pagination repeated or failed to advance");
                var response = await Page(configuration, filterBeforeId, filterBeforeVersion, findingsOnly: true,
                    findingCode: "INSPECTION_AUDIT_EVIDENCE_INVALID");
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

            var filteredFindings = filtered.Where(x => x.GetProperty("programId").GetString()!
                .StartsWith(prefix + "-", StringComparison.Ordinal)).ToArray();
            Assert.Equal(21, filteredFindings.Length);
            Assert.True(filteredPages >= 2, "Filtered pagination did not advance to a second page");
            Assert.All(filteredFindings, row =>
                Assert.Equal(1, row.GetProperty("inspectionAuditEvidenceInvalidNoteCount").GetInt64()));
            Assert.DoesNotContain(filtered, x =>
                x.GetProperty("programId").GetString() == prefix + "-01");
        }
        finally
        {
            await CleanupTestReader(AdminUrl);
        }
    }
}
