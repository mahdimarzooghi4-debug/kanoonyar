using System.Data;
using System.Globalization;
using System.Security.Claims;
using Kanoonyar.Api.Security;
using Microsoft.AspNetCore.Http;
using Npgsql;
using NpgsqlTypes;

namespace Kanoonyar.Api.Programs;

public interface INationalInspectionDossierReadAuthorization
{
    ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal actor, CancellationToken cancellationToken);
}

/// <summary>No approved headquarters dossier-read policy exists; deny every runtime request.</summary>
public sealed class UnapprovedNationalInspectionDossierReadAuthorization
    : INationalInspectionDossierReadAuthorization
{
    public ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal actor, CancellationToken cancellationToken) =>
        ValueTask.FromResult(false);
}

/// <summary>Read-only technical drill-down for immutable national inspection notes.</summary>
public static class NationalInspectionDossierEndpoint
{
    private const int PageSize = 50;
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
        bool IssuanceExecuted);

    public static async Task<IResult> List(
        HttpContext context,
        ClaimsPrincipal actor,
        IConfiguration configuration,
        INationalInspectionDossierReadAuthorization authorization,
        CancellationToken cancellationToken)
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

        var hasBeforeAt = query.ContainsKey("beforeInspectedAt");
        var hasBeforeId = query.ContainsKey("beforeNoteId");
        if (hasBeforeAt != hasBeforeId || query.Count != 2 + (hasBeforeAt ? 2 : 0))
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
                SELECT request_id,note_id,inspection_recorded_at,source_changed_since_request,
                  original_request_integrity_valid,observation_integrity_valid,
                  audit_and_outbox_integrity_valid,current_source_matches_observation,
                  publication_state_changed,center_registry_change_count,
                  exact_issuance_change_count,source_observation_comparable,
                  human_review_recorded,issuance_authorized,issuance_executed
                FROM programs.national_issuance_review_inspection_dossier
                WHERE program_id=@program_id AND national_version=@national_version
                  AND (@before_at::timestamptz IS NULL
                    OR (inspection_recorded_at,note_id)<(@before_at::timestamptz,@before_note_id::uuid))
                ORDER BY inspection_recorded_at DESC,note_id DESC
                LIMIT @row_limit
                """, connection, transaction);
            command.Parameters.Add("program_id", NpgsqlDbType.Text).Value = target.ProgramId;
            command.Parameters.Add("national_version", NpgsqlDbType.Integer).Value = target.NationalVersion;
            command.Parameters.Add("before_at", NpgsqlDbType.TimestampTz).Value =
                cursor is null ? DBNull.Value : cursor.InspectedAt;
            command.Parameters.Add("before_note_id", NpgsqlDbType.Uuid).Value =
                cursor is null ? DBNull.Value : cursor.NoteId;
            command.Parameters.Add("row_limit", NpgsqlDbType.Integer).Value = PageSize + 1;

            var rows = new List<Inspection>(PageSize + 1);
            await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            {
                while (await reader.ReadAsync(cancellationToken))
                {
                    rows.Add(new Inspection(
                        reader.GetGuid(0), reader.GetGuid(1),
                        reader.GetDateTime(2).ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
                        reader.GetBoolean(3), reader.GetBoolean(4), reader.GetBoolean(5),
                        reader.GetBoolean(6), reader.GetBoolean(7),
                        reader.IsDBNull(8) ? null : reader.GetBoolean(8),
                        reader.IsDBNull(9) ? null : reader.GetInt64(9),
                        reader.IsDBNull(10) ? null : reader.GetInt64(10),
                        reader.IsDBNull(11) ? null : reader.GetBoolean(11),
                        reader.GetBoolean(12), reader.GetBoolean(13), reader.GetBoolean(14)));
                }
            }

            var hasMore = rows.Count > PageSize;
            var page = hasMore ? rows.Take(PageSize).ToArray() : rows.ToArray();
            var last = hasMore ? page[^1] : null;
            await transaction.CommitAsync(cancellationToken);
            return Results.Ok(new
            {
                contractVersion = "S101_NATIONAL_INSPECTION_DOSSIER_V1",
                programId = target.ProgramId,
                nationalVersion = target.NationalVersion,
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
}
