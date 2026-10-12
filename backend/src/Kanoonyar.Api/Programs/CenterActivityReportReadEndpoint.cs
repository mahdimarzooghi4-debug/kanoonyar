using System.Security.Claims;
using Kanoonyar.Api.Security;
using Npgsql;
using NpgsqlTypes;

namespace Kanoonyar.Api.Programs;

/// <summary>Separate read gate: a local plan read grant never implies report/evidence access.</summary>
public interface ICenterActivityReportReadAuthorization
{
    ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal principal, string centerId,
        CancellationToken cancellationToken);
}
public sealed class UnapprovedCenterActivityReportReadAuthorization :
    ICenterActivityReportReadAuthorization
{
    public ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal principal, string centerId,
        CancellationToken cancellationToken) => ValueTask.FromResult(false);
}

/// <summary>
/// Shows submitted but UNVERIFIED activity claims. Never counts these as
/// actual delivery, verified evidence, attendance, compliance or outcomes.
/// </summary>
public static class CenterActivityReportReadEndpoint
{
    private const int PageSize = 20;

    public static async Task<IResult> ListReports(ClaimsPrincipal principal,
        IConfiguration configuration, ICenterActivityReportReadAuthorization authorization,
        string centerId, Guid draftId, long? beforeSequence, CancellationToken cancellationToken)
    {
        if (principal.Identity?.IsAuthenticated != true)
            return Results.Problem(statusCode: 401, title: "Authentication required");
        var subject = principal.FindFirst("sub")?.Value;
        if (string.IsNullOrWhiteSpace(subject))
            return Results.Problem(statusCode: 403, title: "Verified subject required");
        if (string.IsNullOrWhiteSpace(centerId) || draftId == Guid.Empty ||
            (beforeSequence.HasValue && beforeSequence.Value <= 0))
            return Results.Problem(statusCode: 400, title: "Invalid report request");

        bool allowed;
        try
        {
            allowed = await authorization.IsAuthorizedAsync(principal, centerId, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            allowed = false;
        }
        if (!allowed)
            return Results.Problem(statusCode: 403, title: "Activity report read permission not granted");

        var cs = configuration["CENTER_DRAFT_READ_DATABASE_URL"];
        if (string.IsNullOrWhiteSpace(cs))
            return Results.Problem(statusCode: 503, title: "Activity report storage unavailable");
        try
        {
            await using var connection = new NpgsqlConnection(cs);
            await connection.OpenAsync(cancellationToken);
            await using var tx = await connection.BeginTransactionAsync(cancellationToken);
            if (!await RestrictedDraftDatabaseRole.VerifyReadOnlyAsync(connection, tx, cancellationToken))
                return Results.Problem(statusCode: 503, title: "Unsafe activity report reader role");

            await using (var setScope = new NpgsqlCommand(
                "SELECT set_config('kanoonyar.center_id',@center,true)", connection, tx))
            {
                setScope.Parameters.AddWithValue("center", centerId);
                await setScope.ExecuteNonQueryAsync(cancellationToken);
            }

            await using var command = new NpgsqlCommand(
                """
                SELECT r.report_sequence,r.report_id,r.link_id,
                       r.source_kind,r.source_record_ref,r.evidence_ref,
                       r.reported_summary,r.claimed_occurred_at,r.recorded_at
                FROM programs.center_activity_reports r
                JOIN programs.center_plan_issuance_links l
                  ON l.center_id=r.center_id AND l.link_id=r.link_id AND l.draft_id=r.draft_id
                WHERE r.center_id=@center AND r.draft_id=@draft
                  AND (@before::bigint IS NULL OR r.report_sequence<@before::bigint)
                  AND EXISTS (
                    SELECT 1 FROM centers.memberships m
                    JOIN centers.registry c ON c.center_id=m.center_id
                    WHERE m.center_id=r.center_id AND m.subject_id=@subject
                      AND m.is_active AND c.is_active
                  )
                ORDER BY r.report_sequence DESC
                LIMIT 21
                """, connection, tx);
            command.Parameters.AddWithValue("center", centerId);
            command.Parameters.AddWithValue("draft", draftId);
            command.Parameters.AddWithValue("subject", subject);
            command.Parameters.Add("before", NpgsqlDbType.Bigint).Value =
                beforeSequence.HasValue ? beforeSequence.Value : DBNull.Value;

            var reports = new List<object>();
            var sequences = new List<long>();
            await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            {
                while (await reader.ReadAsync(cancellationToken))
                {
                    var sequence = reader.GetInt64(0);
                    sequences.Add(sequence);
                    if (reports.Count < PageSize)
                    {
                        reports.Add(new
                        {
                            reportSequence = sequence,
                            reportId = reader.GetGuid(1),
                            linkId = reader.GetGuid(2),
                            sourceKind = reader.GetString(3),
                            sourceRecordRef = reader.GetString(4),
                            evidenceRef = reader.GetString(5),
                            reportedSummary = reader.GetString(6),
                            claimedOccurredAt = reader.IsDBNull(7) ? (DateTime?)null : reader.GetDateTime(7),
                            recordedAt = reader.GetDateTime(8),
                            verificationStatus = "UNVERIFIED"
                        });
                    }
                }
            }
            await tx.CommitAsync(cancellationToken);
            long? nextBeforeSequence = sequences.Count > PageSize ? sequences[PageSize - 1] : null;
            return Results.Ok(new { draftId, reports, nextBeforeSequence });
        }
        catch (NpgsqlException)
        {
            return Results.Problem(statusCode: 503, title: "Activity report storage unavailable");
        }
        catch (TimeoutException)
        {
            return Results.Problem(statusCode: 503, title: "Activity report storage unavailable");
        }
    }
}
