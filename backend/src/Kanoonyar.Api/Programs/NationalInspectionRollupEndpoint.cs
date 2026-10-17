using System.Data;
using System.Globalization;
using System.Security.Claims;
using System.Text;
using Kanoonyar.Api.Security;
using Microsoft.AspNetCore.Http;
using Npgsql;
using NpgsqlTypes;

namespace Kanoonyar.Api.Programs;

/// <summary>
/// Paginated read path for aggregate national inspection evidence.
/// Access remains unavailable until a separately approved HQ read policy exists.
/// </summary>
public interface INationalInspectionRollupReadAuthorization
{
    ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal actor, CancellationToken cancellationToken);
}

/// <summary>No approved HQ review-read policy exists; deny every runtime request.</summary>
public sealed class UnapprovedNationalInspectionRollupReadAuthorization
    : INationalInspectionRollupReadAuthorization
{
    public ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal actor, CancellationToken cancellationToken) =>
        ValueTask.FromResult(false);
}

public static class NationalInspectionRollupEndpoint
{
    private const int PageSize = 20;
    private const int MaximumCsvRows = 10_000;
    private static readonly HashSet<string> FindingCodes = new(StringComparer.Ordinal)
    {
        "SOURCE_SNAPSHOT_DIGEST_INVALID",
        "SOURCE_ENVELOPE_INVALID",
        "CENTER_SELECTION_MISMATCH",
        "AUTHORITY_FLAGS_NOT_CLEAR",
        "AUDIT_EVIDENCE_INVALID",
        "OUTBOX_EVIDENCE_INVALID",
        "ORIGINAL_REQUEST_INTEGRITY_INVALID",
        "OBSERVATION_INTEGRITY_INVALID",
        "INSPECTION_AUDIT_EVIDENCE_INVALID",
        "SOURCE_OBSERVATION_NOT_COMPARABLE"
    };

    private sealed record Cursor(string ProgramId, int NationalVersion);
    private sealed record Target(string ProgramId, int NationalVersion);
    private sealed record Row(
        string RollupContractVersion,
        string ProgramId,
        int NationalVersion,
        long ReviewRequestCount,
        long RequestsWithoutInspectionCount,
        long RequestsWithInspectionCount,
        long InspectionNoteCount,
        long IntegrityConsistentNoteCount,
        long IntegrityInconsistentNoteCount,
        long SourceComparisonUnavailableNoteCount,
        long CurrentSourceMatchNoteCount,
        long CurrentSourceDriftNoteCount,
        long SourceFreshnessUnclassifiedNoteCount,
        long SourceSnapshotDigestInvalidRequestCount,
        long SourceEnvelopeInvalidRequestCount,
        long CenterSelectionMismatchRequestCount,
        long AuthorityFlagsNotClearRequestCount,
        long AuditEvidenceInvalidRequestCount,
        long OutboxEvidenceInvalidRequestCount,
        long OriginalRequestIntegrityInvalidNoteCount,
        long ObservationIntegrityInvalidNoteCount,
        long InspectionAuditEvidenceInvalidNoteCount,
        long SourceObservationNotComparableNoteCount,
        DateTime? LatestTechnicalObservationAt,
        bool HumanReviewRecorded,
        bool IssuanceAuthorized,
        bool IssuanceExecuted);

    public static Task<IResult> List(
        HttpContext context,
        ClaimsPrincipal actor,
        IConfiguration configuration,
        INationalInspectionRollupReadAuthorization authorization,
        CancellationToken cancellationToken) =>
        Execute(context, actor, configuration, authorization, cancellationToken, exportCsv: false);

    public static Task<IResult> ExportCsv(
        HttpContext context,
        ClaimsPrincipal actor,
        IConfiguration configuration,
        INationalInspectionRollupReadAuthorization authorization,
        CancellationToken cancellationToken) =>
        Execute(context, actor, configuration, authorization, cancellationToken, exportCsv: true);

    private static async Task<IResult> Execute(
        HttpContext context,
        ClaimsPrincipal actor,
        IConfiguration configuration,
        INationalInspectionRollupReadAuthorization authorization,
        CancellationToken cancellationToken,
        bool exportCsv)
    {
        if (actor.Identity?.IsAuthenticated != true)
            return Results.Problem(statusCode: 401, title: "Authentication required");
        if (string.IsNullOrWhiteSpace(actor.FindFirst("sub")?.Value))
            return Results.Problem(statusCode: 403, title: "Verified subject required");

        var cursorResult = ParseCursor(context.Request.Query, exportCsv, out var cursor, out var findingsOnly, out var findingCode, out var target);
        if (cursorResult is not null) return cursorResult;

        bool permitted;
        try
        {
            permitted = await authorization.IsAuthorizedAsync(actor, cancellationToken);
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
            return Results.Problem(statusCode: 403, title: "Independent national inspection read grant required");

        var connectionString = configuration["NATIONAL_INSPECTION_ROLLUP_READ_DATABASE_URL"];
        if (string.IsNullOrWhiteSpace(connectionString))
            return Results.Problem(statusCode: 503, title: "National inspection rollup unavailable");

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

            const string sql = """
                SELECT rollup_contract_version,program_id,national_version,
                  review_request_count,requests_without_inspection_count,
                  requests_with_inspection_count,inspection_note_count,
                  integrity_consistent_note_count,integrity_inconsistent_note_count,
                  source_comparison_unavailable_note_count,current_source_match_note_count,
                  current_source_drift_note_count,source_freshness_unclassified_note_count,
                  source_snapshot_digest_invalid_request_count,source_envelope_invalid_request_count,
                  center_selection_mismatch_request_count,authority_flags_not_clear_request_count,
                  audit_evidence_invalid_request_count,outbox_evidence_invalid_request_count,
                  original_request_integrity_invalid_note_count,observation_integrity_invalid_note_count,
                  inspection_audit_evidence_invalid_note_count,source_observation_not_comparable_note_count,
                  latest_technical_observation_at,human_review_recorded,
                  issuance_authorized,issuance_executed
                FROM programs.national_issuance_review_national_inspection_rollup_v2
                WHERE (@before_program_id::text IS NULL
                       OR (program_id,national_version)<(@before_program_id::text,@before_version::integer))
                  AND (@findings_only = false
                    OR source_snapshot_digest_invalid_request_count > 0
                    OR source_envelope_invalid_request_count > 0
                    OR center_selection_mismatch_request_count > 0
                    OR authority_flags_not_clear_request_count > 0
                    OR audit_evidence_invalid_request_count > 0
                    OR outbox_evidence_invalid_request_count > 0
                    OR original_request_integrity_invalid_note_count > 0
                    OR observation_integrity_invalid_note_count > 0
                    OR inspection_audit_evidence_invalid_note_count > 0
                    OR source_observation_not_comparable_note_count > 0)
                  AND (@finding_code::text IS NULL
                    OR (@finding_code='SOURCE_SNAPSHOT_DIGEST_INVALID' AND source_snapshot_digest_invalid_request_count > 0)
                    OR (@finding_code='SOURCE_ENVELOPE_INVALID' AND source_envelope_invalid_request_count > 0)
                    OR (@finding_code='CENTER_SELECTION_MISMATCH' AND center_selection_mismatch_request_count > 0)
                    OR (@finding_code='AUTHORITY_FLAGS_NOT_CLEAR' AND authority_flags_not_clear_request_count > 0)
                    OR (@finding_code='AUDIT_EVIDENCE_INVALID' AND audit_evidence_invalid_request_count > 0)
                    OR (@finding_code='OUTBOX_EVIDENCE_INVALID' AND outbox_evidence_invalid_request_count > 0)
                    OR (@finding_code='ORIGINAL_REQUEST_INTEGRITY_INVALID' AND original_request_integrity_invalid_note_count > 0)
                    OR (@finding_code='OBSERVATION_INTEGRITY_INVALID' AND observation_integrity_invalid_note_count > 0)
                    OR (@finding_code='INSPECTION_AUDIT_EVIDENCE_INVALID' AND inspection_audit_evidence_invalid_note_count > 0)
                    OR (@finding_code='SOURCE_OBSERVATION_NOT_COMPARABLE' AND source_observation_not_comparable_note_count > 0))
                  AND (@target_program_id::text IS NULL OR program_id=@target_program_id::text)
                  AND (@target_national_version::integer IS NULL OR national_version=@target_national_version::integer)
                ORDER BY program_id DESC,national_version DESC
                LIMIT @row_limit
                """;
            await using var command = new NpgsqlCommand(sql, connection, transaction);
            command.Parameters.Add("before_program_id", NpgsqlDbType.Text).Value =
                cursor is null ? DBNull.Value : cursor.ProgramId;
            command.Parameters.Add("before_version", NpgsqlDbType.Integer).Value =
                cursor is null ? DBNull.Value : cursor.NationalVersion;
            command.Parameters.Add("findings_only", NpgsqlDbType.Boolean).Value = findingsOnly;
            command.Parameters.Add("finding_code", NpgsqlDbType.Text).Value =
                findingCode is null ? DBNull.Value : findingCode;
            command.Parameters.Add("target_program_id", NpgsqlDbType.Text).Value =
                target is null ? DBNull.Value : target.ProgramId;
            command.Parameters.Add("target_national_version", NpgsqlDbType.Integer).Value =
                target is null ? DBNull.Value : target.NationalVersion;
            command.Parameters.Add("row_limit", NpgsqlDbType.Integer).Value =
                exportCsv ? MaximumCsvRows + 1 : PageSize + 1;

            var rows = new List<Row>(exportCsv ? MaximumCsvRows + 1 : PageSize + 1);
            await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            {
                while (await reader.ReadAsync(cancellationToken))
                {
                    rows.Add(new Row(
                        reader.GetString(0), reader.GetString(1), reader.GetInt32(2),
                        reader.GetInt64(3), reader.GetInt64(4), reader.GetInt64(5),
                        reader.GetInt64(6), reader.GetInt64(7), reader.GetInt64(8),
                        reader.GetInt64(9), reader.GetInt64(10), reader.GetInt64(11),
                        reader.GetInt64(12),
                        reader.GetInt64(13), reader.GetInt64(14), reader.GetInt64(15),
                        reader.GetInt64(16), reader.GetInt64(17), reader.GetInt64(18),
                        reader.GetInt64(19), reader.GetInt64(20), reader.GetInt64(21),
                        reader.GetInt64(22),
                        reader.IsDBNull(23) ? null : reader.GetDateTime(23),
                        reader.GetBoolean(24), reader.GetBoolean(25), reader.GetBoolean(26)));
                }
            }

            if (exportCsv)
            {
                if (rows.Count > MaximumCsvRows)
                    return Results.Problem(statusCode: StatusCodes.Status413PayloadTooLarge,
                        title: "National inspection export exceeds the safe row limit");

                var csv = BuildCsv(rows);
                context.Response.Headers["X-National-Inspection-Export-Contract"] =
                    "S98_NATIONAL_INSPECTION_CSV_RECEIPT_V1";
                context.Response.Headers["X-National-Inspection-Export-Schema"] =
                    "S100_NATIONAL_INSPECTION_CSV_SCHEMA_V1";
                context.Response.Headers["X-National-Inspection-Export-Row-Count"] =
                    rows.Count.ToString(CultureInfo.InvariantCulture);
                context.Response.Headers["X-National-Inspection-Export-SHA256"] =
                    Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(csv)).ToLowerInvariant();
                await transaction.CommitAsync(cancellationToken);
                return Results.File(csv, "text/csv; charset=utf-8", "national-inspection-rollup.csv");
            }

            var hasMore = rows.Count > PageSize;
            var page = hasMore ? rows.Take(PageSize).ToArray() : rows.ToArray();
            var last = hasMore ? page[^1] : null;
            await transaction.CommitAsync(cancellationToken);

            return Results.Ok(new
            {
                rollups = page,
                nextCursor = last is null
                    ? null
                    : new { beforeProgramId = last.ProgramId, beforeNationalVersion = last.NationalVersion }
            });
        }
        catch (NpgsqlException)
        {
            return Results.Problem(statusCode: 503, title: "National inspection rollup unavailable");
        }
        catch (TimeoutException)
        {
            return Results.Problem(statusCode: 503, title: "National inspection rollup unavailable");
        }
    }

    private static IResult? ParseCursor(
        IQueryCollection query, bool exportCsv, out Cursor? cursor, out bool findingsOnly,
        out string? findingCode, out Target? target)
    {
        cursor = null;
        findingsOnly = false;
        findingCode = null;
        target = null;
        var hasFindingsOnly = query.ContainsKey("findingsOnly");
        if (hasFindingsOnly)
        {
            if (query["findingsOnly"].Count != 1) return Results.BadRequest();
            var rawFindingsOnly = query["findingsOnly"].ToString();
            if (rawFindingsOnly is not ("true" or "false")) return Results.BadRequest();
            findingsOnly = rawFindingsOnly == "true";
        }

        var hasFindingCode = query.ContainsKey("findingCode");
        if (hasFindingCode)
        {
            if (query["findingCode"].Count != 1) return Results.BadRequest();
            findingCode = query["findingCode"].ToString();
            if (!FindingCodes.Contains(findingCode)) return Results.BadRequest();
            if (hasFindingsOnly && !findingsOnly) return Results.BadRequest();
        }

        var hasTargetProgram = query.ContainsKey("targetProgramId");
        var hasTargetVersion = query.ContainsKey("targetNationalVersion");
        if (hasTargetProgram != hasTargetVersion) return Results.BadRequest();
        if (hasTargetProgram)
        {
            if (query["targetProgramId"].Count != 1 || query["targetNationalVersion"].Count != 1)
                return Results.BadRequest();
            var targetProgramId = query["targetProgramId"].ToString();
            var rawTargetVersion = query["targetNationalVersion"].ToString();
            if (string.IsNullOrWhiteSpace(targetProgramId) || targetProgramId != targetProgramId.Trim() ||
                targetProgramId.Length > 256 || targetProgramId.Any(char.IsControl) ||
                !int.TryParse(rawTargetVersion, NumberStyles.None, CultureInfo.InvariantCulture, out var targetVersion) ||
                targetVersion <= 0 || rawTargetVersion != targetVersion.ToString(CultureInfo.InvariantCulture))
                return Results.BadRequest();
            target = new Target(targetProgramId, targetVersion);
        }

        var hasProgram = query.ContainsKey("beforeProgramId");
        var hasVersion = query.ContainsKey("beforeNationalVersion");
        if (exportCsv && (hasProgram || hasVersion)) return Results.BadRequest();
        if (hasProgram != hasVersion || (hasProgram && hasTargetProgram)) return Results.BadRequest();
        var expectedParameters = (hasFindingsOnly ? 1 : 0) + (hasFindingCode ? 1 : 0) +
            (hasProgram ? 2 : 0) + (hasTargetProgram ? 2 : 0);
        if (query.Count != expectedParameters) return Results.BadRequest();
        if (!hasProgram) return null;
        if (query["beforeProgramId"].Count != 1 ||
            query["beforeNationalVersion"].Count != 1)
            return Results.BadRequest();

        var programId = query["beforeProgramId"].ToString();
        var rawVersion = query["beforeNationalVersion"].ToString();
        if (string.IsNullOrWhiteSpace(programId) || programId != programId.Trim() ||
            programId.Length > 256 || programId.Any(char.IsControl) ||
            !int.TryParse(rawVersion, NumberStyles.None, CultureInfo.InvariantCulture, out var version) ||
            version <= 0 || rawVersion != version.ToString(CultureInfo.InvariantCulture))
            return Results.BadRequest();

        cursor = new Cursor(programId, version);
        return null;
    }

    private static byte[] BuildCsv(IReadOnlyList<Row> rows)
    {
        var builder = new StringBuilder();
        builder.Append('\uFEFF');
        builder.AppendJoin(',', new[]
        {
            CsvCell("شناسه برنامه"), CsvCell("نسخه ملی"), CsvCell("درخواست"),
            CsvCell("بدون بازرسی"), CsvCell("با بازرسی"), CsvCell("یادداشت بازرسی"),
            CsvCell("یکپارچگی سازگار"), CsvCell("یکپارچگی ناسازگار"),
            CsvCell("مقایسه ناموجود"), CsvCell("منبع منطبق"), CsvCell("منبع متغیر"),
            CsvCell("تازگی نامشخص"), CsvCell("Digest نامعتبر درخواست"),
            CsvCell("ساختار نامعتبر درخواست"), CsvCell("عدم تطبیق انتخاب کانون"),
            CsvCell("پرچم اختیار نامشخص"), CsvCell("شواهد Audit نامعتبر درخواست"),
            CsvCell("شواهد Outbox نامعتبر درخواست"),
            CsvCell("یکپارچگی درخواست اصلی نامعتبر"), CsvCell("یکپارچگی مشاهده نامعتبر"),
            CsvCell("شواهد Audit بازرسی نامعتبر"), CsvCell("مشاهده غیرقابل‌مقایسه"),
            CsvCell("آخرین مشاهده فنی")
        });
        builder.Append("\r\n");

        foreach (var row in rows)
        {
            builder.AppendJoin(',', new[]
            {
                CsvCell(row.ProgramId),
                CsvCell(row.NationalVersion.ToString(CultureInfo.InvariantCulture)),
                CsvCell(row.ReviewRequestCount.ToString(CultureInfo.InvariantCulture)),
                CsvCell(row.RequestsWithoutInspectionCount.ToString(CultureInfo.InvariantCulture)),
                CsvCell(row.RequestsWithInspectionCount.ToString(CultureInfo.InvariantCulture)),
                CsvCell(row.InspectionNoteCount.ToString(CultureInfo.InvariantCulture)),
                CsvCell(row.IntegrityConsistentNoteCount.ToString(CultureInfo.InvariantCulture)),
                CsvCell(row.IntegrityInconsistentNoteCount.ToString(CultureInfo.InvariantCulture)),
                CsvCell(row.SourceComparisonUnavailableNoteCount.ToString(CultureInfo.InvariantCulture)),
                CsvCell(row.CurrentSourceMatchNoteCount.ToString(CultureInfo.InvariantCulture)),
                CsvCell(row.CurrentSourceDriftNoteCount.ToString(CultureInfo.InvariantCulture)),
                CsvCell(row.SourceFreshnessUnclassifiedNoteCount.ToString(CultureInfo.InvariantCulture)),
                CsvCell(row.SourceSnapshotDigestInvalidRequestCount.ToString(CultureInfo.InvariantCulture)),
                CsvCell(row.SourceEnvelopeInvalidRequestCount.ToString(CultureInfo.InvariantCulture)),
                CsvCell(row.CenterSelectionMismatchRequestCount.ToString(CultureInfo.InvariantCulture)),
                CsvCell(row.AuthorityFlagsNotClearRequestCount.ToString(CultureInfo.InvariantCulture)),
                CsvCell(row.AuditEvidenceInvalidRequestCount.ToString(CultureInfo.InvariantCulture)),
                CsvCell(row.OutboxEvidenceInvalidRequestCount.ToString(CultureInfo.InvariantCulture)),
                CsvCell(row.OriginalRequestIntegrityInvalidNoteCount.ToString(CultureInfo.InvariantCulture)),
                CsvCell(row.ObservationIntegrityInvalidNoteCount.ToString(CultureInfo.InvariantCulture)),
                CsvCell(row.InspectionAuditEvidenceInvalidNoteCount.ToString(CultureInfo.InvariantCulture)),
                CsvCell(row.SourceObservationNotComparableNoteCount.ToString(CultureInfo.InvariantCulture)),
                CsvCell(row.LatestTechnicalObservationAt?.ToString("O", CultureInfo.InvariantCulture) ?? "")
            });
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
