using System.Data;
using System.Globalization;
using System.Security.Claims;
using System.Text;
using Kanoonyar.Api.Security;
using Microsoft.AspNetCore.Http;
using Npgsql;
using NpgsqlTypes;

namespace Kanoonyar.Api.Programs;

/// <summary>Read-only technical drill-down for immutable national inspection notes.</summary>
public static class NationalInspectionDossierEndpoint
{
    private const int PageSize = 50;
    private const int MaximumCsvRows = 10_000;
    private static readonly HashSet<string> FindingCodes = new(StringComparer.Ordinal)
    {
        "ORIGINAL_REQUEST_INTEGRITY_INVALID",
        "OBSERVATION_INTEGRITY_INVALID",
        "INSPECTION_AUDIT_EVIDENCE_INVALID",
        "SOURCE_OBSERVATION_NOT_COMPARABLE"
    };
    private sealed record Cursor(DateTimeOffset InspectedAt, Guid NoteId);
    private sealed record Target(string ProgramId, int NationalVersion);

    private sealed record Inspection(
        Guid RequestId,
        Guid NoteId,
        string InspectedAt,
        bool SourceChangedSinceRequest,
        bool OriginalRequestIntegrityValid,
        bool ObservationIntegrityValid,
        bool AuditAndOutboxIntegrityValid,
        bool CurrentSourceMatchesObservation,
        bool? PublicationStateChanged,
        long? CenterRegistryChangeCount,
        long? ExactIssuanceChangeCount,
        bool? SourceObservationComparable,
        bool HumanReviewRecorded,
        bool IssuanceAuthorized,
        bool IssuanceExecuted,
        string[] FindingCodes);

    public static Task<IResult> List(
        HttpContext context,
        ClaimsPrincipal actor,
        IConfiguration configuration,
        INationalInspectionReadAuthorization authorization,
        CancellationToken cancellationToken) =>
        Execute(context, actor, configuration, authorization, cancellationToken, exportCsv: false);

    public static Task<IResult> ExportCsv(
        HttpContext context,
        ClaimsPrincipal actor,
        IConfiguration configuration,
        INationalInspectionReadAuthorization authorization,
        CancellationToken cancellationToken) =>
        Execute(context, actor, configuration, authorization, cancellationToken, exportCsv: true);

    private static async Task<IResult> Execute(
        HttpContext context,
        ClaimsPrincipal actor,
        IConfiguration configuration,
        INationalInspectionReadAuthorization authorization,
        CancellationToken cancellationToken,
        bool exportCsv)
    {
        if (actor.Identity?.IsAuthenticated != true)
            return Results.Problem(statusCode: 401, title: "Authentication required");
        if (string.IsNullOrWhiteSpace(actor.FindFirst("sub")?.Value))
            return Results.Problem(statusCode: 403, title: "Verified subject required");

        var query = context.Request.Query;
        if (!query.ContainsKey("programId") || !query.ContainsKey("nationalVersion") ||
            query["programId"].Count != 1 || query["nationalVersion"].Count != 1)
            return Results.BadRequest();

        var programId = query["programId"].ToString();
        var rawVersion = query["nationalVersion"].ToString();
        if (string.IsNullOrWhiteSpace(programId) || programId != programId.Trim() ||
            programId.Length > 256 || programId.Any(char.IsControl) ||
            !int.TryParse(rawVersion, NumberStyles.None, CultureInfo.InvariantCulture, out var version) ||
            version < 1 || rawVersion != version.ToString(CultureInfo.InvariantCulture))
            return Results.BadRequest();
        var target = new Target(programId, version);

        var findingsOnly = false;
        if (query.ContainsKey("findingsOnly"))
        {
            if (query["findingsOnly"].Count != 1 || query["findingsOnly"] != "true")
                return Results.BadRequest();
            findingsOnly = true;
        }
        string? findingCode = null;
        if (query.ContainsKey("findingCode"))
        {
            if (query["findingCode"].Count != 1 || !FindingCodes.Contains(query["findingCode"].ToString()))
                return Results.BadRequest();
            findingCode = query["findingCode"].ToString();
            findingsOnly = true;
        }

        var hasBeforeAt = query.ContainsKey("beforeInspectedAt");
        var hasBeforeId = query.ContainsKey("beforeNoteId");
        if (hasBeforeAt != hasBeforeId || (exportCsv && (hasBeforeAt || findingsOnly)) ||
            query.Count != 2 + (hasBeforeAt ? 2 : 0) + (query.ContainsKey("findingsOnly") ? 1 : 0) +
                (query.ContainsKey("findingCode") ? 1 : 0))
            return Results.BadRequest();

        Cursor? cursor = null;
        if (hasBeforeAt)
        {
            var rawAt = query["beforeInspectedAt"].ToString();
            var rawId = query["beforeNoteId"].ToString();
            if (query["beforeInspectedAt"].Count != 1 || query["beforeNoteId"].Count != 1 ||
                !DateTimeOffset.TryParse(rawAt, CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var inspectedAt) ||
                inspectedAt.Offset != TimeSpan.Zero ||
                !Guid.TryParseExact(rawId, "D", out var noteId) ||
                rawId != noteId.ToString("D"))
                return Results.BadRequest();
            cursor = new Cursor(inspectedAt, noteId);
        }

        bool permitted;
        try
        {
            permitted = await authorization.IsAuthorizedAsync(actor,
                new NationalInspectionReadScope(
                    exportCsv ? NationalInspectionReadOperation.InspectionDossierCsvExport :
                        NationalInspectionReadOperation.InspectionDossierPage,
                    target.ProgramId,
                    target.NationalVersion,
                    FindingsOnly: findingsOnly,
                    FindingCode: findingCode),
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            permitted = false;
        }
        if (!permitted)
            return Results.Problem(statusCode: 403,
                title: "Independent national inspection read grant required");

        var connectionString = configuration["NATIONAL_INSPECTION_DOSSIER_READ_DATABASE_URL"];
        if (string.IsNullOrWhiteSpace(connectionString))
            return Results.Problem(statusCode: 503, title: "National inspection dossier unavailable");

        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(
                IsolationLevel.RepeatableRead, cancellationToken);
            await using (var readOnly = new NpgsqlCommand("SET TRANSACTION READ ONLY", connection, transaction))
                await readOnly.ExecuteNonQueryAsync(cancellationToken);
            if (!await RestrictedDraftDatabaseRole.VerifyReadOnlyAsync(
                    connection, transaction, cancellationToken))
                return Results.Problem(statusCode: 503, title: "Unsafe national inspection read role");

            await using var command = new NpgsqlCommand(
                """
                WITH filtered AS (
                  SELECT d.request_id,d.note_id,d.inspection_recorded_at,d.source_changed_since_request,
                    d.original_request_integrity_valid,d.observation_integrity_valid,
                    d.audit_and_outbox_integrity_valid,d.current_source_matches_observation,
                    d.publication_state_changed,d.center_registry_change_count,
                    d.exact_issuance_change_count,d.source_observation_comparable,
                    d.human_review_recorded,d.issuance_authorized,d.issuance_executed,
                    finding_codes.codes AS finding_codes
                  FROM programs.national_issuance_review_inspection_dossier d
                  CROSS JOIN LATERAL (
                    SELECT ARRAY(SELECT f.finding_code
                      FROM programs.national_issuance_review_inspection_findings f
                      WHERE f.program_id=d.program_id AND f.national_version=d.national_version
                        AND f.note_id=d.note_id ORDER BY f.finding_code) AS codes
                  ) finding_codes
                  WHERE d.program_id=@program_id AND d.national_version=@national_version
                    AND (@findings_only = false OR cardinality(finding_codes.codes) > 0)
                    AND (@finding_code::text IS NULL OR @finding_code = ANY(finding_codes.codes))
                ),
                paged AS (
                  SELECT * FROM filtered
                  WHERE (@before_at::timestamptz IS NULL
                    OR (inspection_recorded_at,note_id)<(@before_at::timestamptz,@before_note_id::uuid))
                  ORDER BY inspection_recorded_at DESC,note_id DESC
                  LIMIT @row_limit
                ),
                matching_count AS (SELECT COUNT(*) AS matching_note_count FROM filtered)
                SELECT p.request_id,p.note_id,p.inspection_recorded_at,p.source_changed_since_request,
                  p.original_request_integrity_valid,p.observation_integrity_valid,
                  p.audit_and_outbox_integrity_valid,p.current_source_matches_observation,
                  p.publication_state_changed,p.center_registry_change_count,
                  p.exact_issuance_change_count,p.source_observation_comparable,
                  p.human_review_recorded,p.issuance_authorized,p.issuance_executed,
                  p.finding_codes,matching_count.matching_note_count
                FROM matching_count
                LEFT JOIN paged p ON TRUE
                ORDER BY p.inspection_recorded_at DESC NULLS LAST,p.note_id DESC NULLS LAST
                """, connection, transaction);
            command.Parameters.Add("program_id", NpgsqlDbType.Text).Value = target.ProgramId;
            command.Parameters.Add("national_version", NpgsqlDbType.Integer).Value = target.NationalVersion;
            command.Parameters.Add("findings_only", NpgsqlDbType.Boolean).Value = findingsOnly;
            command.Parameters.Add("finding_code", NpgsqlDbType.Text).Value =
                findingCode is null ? DBNull.Value : findingCode;
            command.Parameters.Add("before_at", NpgsqlDbType.TimestampTz).Value =
                cursor is null ? DBNull.Value : cursor.InspectedAt;
            command.Parameters.Add("before_note_id", NpgsqlDbType.Uuid).Value =
                cursor is null ? DBNull.Value : cursor.NoteId;
            var maximumRows = exportCsv ? MaximumCsvRows : PageSize;
            command.Parameters.Add("row_limit", NpgsqlDbType.Integer).Value = maximumRows + 1;

            var rows = new List<Inspection>(maximumRows + 1);
            long matchingNoteCount = 0;
            await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            {
                while (await reader.ReadAsync(cancellationToken))
                {
                    matchingNoteCount = reader.GetInt64(16);
                    if (reader.IsDBNull(0))
                        continue;
                    rows.Add(new Inspection(
                        reader.GetGuid(0), reader.GetGuid(1),
                        reader.GetDateTime(2).ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
                        reader.GetBoolean(3), reader.GetBoolean(4), reader.GetBoolean(5),
                        reader.GetBoolean(6), reader.GetBoolean(7),
                        reader.IsDBNull(8) ? null : reader.GetBoolean(8),
                        reader.IsDBNull(9) ? null : reader.GetInt64(9),
                        reader.IsDBNull(10) ? null : reader.GetInt64(10),
                        reader.IsDBNull(11) ? null : reader.GetBoolean(11),
                        reader.GetBoolean(12), reader.GetBoolean(13), reader.GetBoolean(14),
                        reader.GetFieldValue<string[]>(15)));
                }
            }

            if (exportCsv)
            {
                if (rows.Count > MaximumCsvRows)
                    return Results.Problem(statusCode: StatusCodes.Status413PayloadTooLarge,
                        title: "National inspection dossier export exceeds the safe row limit");

                var csv = BuildCsv(target, rows);
                context.Response.Headers["X-National-Inspection-Dossier-Export-Contract"] =
                    "S105_NATIONAL_INSPECTION_DOSSIER_CSV_RECEIPT_V2";
                context.Response.Headers["X-National-Inspection-Dossier-Export-Schema"] =
                    "S105_NATIONAL_INSPECTION_DOSSIER_CSV_V2";
                context.Response.Headers["X-National-Inspection-Dossier-Export-Row-Count"] =
                    rows.Count.ToString(CultureInfo.InvariantCulture);
                context.Response.Headers["X-National-Inspection-Dossier-Export-SHA256"] =
                    Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(csv)).ToLowerInvariant();
                await transaction.CommitAsync(cancellationToken);
                return Results.File(csv, "text/csv; charset=utf-8", "national-inspection-dossier.csv");
            }

            var hasMore = rows.Count > PageSize;
            var page = hasMore ? rows.Take(PageSize).ToArray() : rows.ToArray();
            var last = hasMore ? page[^1] : null;
            await transaction.CommitAsync(cancellationToken);
            return Results.Ok(new
            {
                contractVersion = "S106_NATIONAL_INSPECTION_DOSSIER_V4",
                matchingNoteCount,
                programId = target.ProgramId,
                nationalVersion = target.NationalVersion,
                findingsOnly,
                findingCode,
                inspections = page,
                nextCursor = last is null ? null : new
                {
                    beforeInspectedAt = last.InspectedAt,
                    beforeNoteId = last.NoteId
                }
            });
        }
        catch (NpgsqlException)
        {
            return Results.Problem(statusCode: 503, title: "National inspection dossier unavailable");
        }
        catch (TimeoutException)
        {
            return Results.Problem(statusCode: 503, title: "National inspection dossier unavailable");
        }
    }

    private static byte[] BuildCsv(Target target, IReadOnlyList<Inspection> rows)
    {
        var builder = new StringBuilder();
        builder.Append('\uFEFF');
        builder.AppendJoin(',', new[]
        {
            "program_id", "national_version", "request_id", "note_id", "inspected_at",
            "source_changed_since_request", "original_request_integrity_valid",
            "observation_integrity_valid", "audit_and_outbox_integrity_valid",
            "current_source_matches_observation", "publication_state_changed",
            "center_registry_change_count", "exact_issuance_change_count",
            "source_observation_comparable", "human_review_recorded",
            "issuance_authorized", "issuance_executed", "finding_codes"
        }.Select(CsvCell));
        builder.Append("\r\n");

        foreach (var row in rows)
        {
            builder.AppendJoin(',', new[]
            {
                CsvCell(target.ProgramId),
                target.NationalVersion.ToString(CultureInfo.InvariantCulture),
                row.RequestId.ToString("D"),
                row.NoteId.ToString("D"),
                CsvCell(row.InspectedAt),
                row.SourceChangedSinceRequest.ToString().ToLowerInvariant(),
                row.OriginalRequestIntegrityValid.ToString().ToLowerInvariant(),
                row.ObservationIntegrityValid.ToString().ToLowerInvariant(),
                row.AuditAndOutboxIntegrityValid.ToString().ToLowerInvariant(),
                row.CurrentSourceMatchesObservation.ToString().ToLowerInvariant(),
                row.PublicationStateChanged?.ToString().ToLowerInvariant() ?? "",
                row.CenterRegistryChangeCount?.ToString(CultureInfo.InvariantCulture) ?? "",
                row.ExactIssuanceChangeCount?.ToString(CultureInfo.InvariantCulture) ?? "",
                row.SourceObservationComparable?.ToString().ToLowerInvariant() ?? "",
                row.HumanReviewRecorded.ToString().ToLowerInvariant(),
                row.IssuanceAuthorized.ToString().ToLowerInvariant(),
                row.IssuanceExecuted.ToString().ToLowerInvariant(),
                CsvCell(string.Join(';', row.FindingCodes))
            }.Select(CsvCell));
            builder.Append("\r\n");
        }

        return Encoding.UTF8.GetBytes(builder.ToString());
    }

    private static string CsvCell(string value)
    {
        var first = 0;
        while (first < value.Length && value[first] <= 0x20) first++;
        if (first < value.Length && value[first] is '=' or '+' or '-' or '@')
            value = "'" + value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
}
