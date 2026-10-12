using System.Security.Claims;
using Kanoonyar.Api.Security;
using Npgsql;
using NpgsqlTypes;

namespace Kanoonyar.Api.Programs;

/// <summary>Independent, revocable operational grant; membership/JWT role is never enough.</summary>
public interface ICenterSessionReportLineageAuthorization
{
    ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal user, string centerId, Guid sessionId,
        CancellationToken cancellationToken);
}

public sealed class UnapprovedCenterSessionReportLineageAuthorization :
    ICenterSessionReportLineageAuthorization
{
    public ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal user, string centerId, Guid sessionId,
        CancellationToken cancellationToken) => ValueTask.FromResult(false);
}

/// <summary>
/// Reuses source-owned session/class and only the exact, typed, immutable activity
/// report bindings. Neither reported activity nor opaque evidence is verified.
/// Does not query or expose participant / attendance data or aggregate a KPI.
/// </summary>
public static class CenterSessionReportLineageEndpoint
{
    private const int PageSize = 20;

    public static async Task<IResult> Get(
        ClaimsPrincipal user, IConfiguration configuration,
        ICenterSessionReportLineageAuthorization authorization,
        string centerId, Guid sessionId, long? beforeSequence, CancellationToken cancellationToken)
    {
        if (user.Identity?.IsAuthenticated != true)
            return Results.Problem(statusCode: 401, title: "Authentication required");
        var subject = user.FindFirst("sub")?.Value;
        if (string.IsNullOrWhiteSpace(subject))
            return Results.Problem(statusCode: 403, title: "Verified subject required");
        if (string.IsNullOrWhiteSpace(centerId) || sessionId == Guid.Empty ||
            (beforeSequence.HasValue && beforeSequence.Value <= 0))
            return Results.Problem(statusCode: 400, title: "Invalid session report request");

        bool allowed;
        try
        {
            allowed = await authorization.IsAuthorizedAsync(
                user, centerId, sessionId, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch { allowed = false; }
        if (!allowed)
            return Results.Problem(statusCode: 403, title: "Independent session report lineage grant required");

        var connectionString = configuration["CENTER_DRAFT_READ_DATABASE_URL"];
        if (string.IsNullOrWhiteSpace(connectionString))
            return Results.Problem(statusCode: 503, title: "Session report lineage unavailable");

        try
        {
            await using var db = new NpgsqlConnection(connectionString);
            await db.OpenAsync(cancellationToken);
            await using var tx = await db.BeginTransactionAsync(cancellationToken);
            if (!await RestrictedDraftDatabaseRole.VerifyReadOnlyAsync(db, tx, cancellationToken))
                return Results.Problem(statusCode: 503, title: "Unsafe session report SQL role");

            await using (var scope = new NpgsqlCommand(
                "SELECT set_config('kanoonyar.center_id',@center,true)", db, tx))
            {
                scope.Parameters.AddWithValue("center", centerId);
                await scope.ExecuteNonQueryAsync(cancellationToken);
            }

            const string sourceSql = """
                SELECT s.class_id,s.session_title,c.title,s.scheduled_at
                FROM learning.class_sessions s
                JOIN learning.center_classes c
                  ON c.center_id=s.center_id AND c.class_id=s.class_id
                WHERE s.center_id=@center AND s.session_id=@session
                  AND EXISTS (
                    SELECT 1 FROM centers.memberships m
                    JOIN centers.registry registry ON registry.center_id=m.center_id
                    WHERE m.center_id=s.center_id AND m.subject_id=@subject
                      AND m.is_active AND registry.is_active
                  )
                """;
            Guid classId;
            string sessionTitle, classTitle;
            DateTime? scheduledAt;
            await using (var source = new NpgsqlCommand(sourceSql, db, tx))
            {
                source.Parameters.AddWithValue("center", centerId);
                source.Parameters.AddWithValue("session", sessionId);
                source.Parameters.AddWithValue("subject", subject);
                await using var reader = await source.ExecuteReaderAsync(cancellationToken);
                if (!await reader.ReadAsync(cancellationToken))
                    return Results.Problem(statusCode: 404, title: "Session not found in authorized center");
                classId = reader.GetGuid(0);
                sessionTitle = reader.GetString(1);
                classTitle = reader.GetString(2);
                scheduledAt = reader.IsDBNull(3) ? null : reader.GetDateTime(3);
            }

            const string lineageSql = """
                SELECT r.report_sequence,r.report_id,r.draft_id,r.link_id,
                       l.issuance_id,l.program_id,l.national_version,l.pinned_revision,
                       r.reported_summary,r.evidence_ref,r.claimed_occurred_at,r.recorded_at
                FROM programs.activity_report_source_bindings b
                JOIN programs.center_activity_reports r
                  ON r.center_id=b.center_id AND r.report_id=b.report_id
                 AND r.source_kind=b.source_kind AND r.source_record_ref=b.source_record_ref
                JOIN programs.center_plan_issuance_links l
                  ON l.center_id=r.center_id AND l.link_id=r.link_id
                 AND l.draft_id=r.draft_id
                WHERE b.center_id=@center AND b.source_kind='CLASS_SESSION'
                  AND b.class_session_id=@session
                  AND (@before::bigint IS NULL OR r.report_sequence<@before::bigint)
                  AND EXISTS (
                    SELECT 1 FROM centers.memberships m
                    JOIN centers.registry registry ON registry.center_id=m.center_id
                    WHERE m.center_id=b.center_id AND m.subject_id=@subject
                      AND m.is_active AND registry.is_active
                  )
                ORDER BY r.report_sequence DESC LIMIT 21
                """;
            await using var command = new NpgsqlCommand(lineageSql, db, tx);
            command.Parameters.AddWithValue("center", centerId);
            command.Parameters.AddWithValue("session", sessionId);
            command.Parameters.AddWithValue("subject", subject);
            command.Parameters.Add("before", NpgsqlDbType.Bigint).Value =
                beforeSequence.HasValue ? beforeSequence.Value : DBNull.Value;
            var reports = new List<object>();
            long? nextBeforeSequence = null;
            var count = 0;
            await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            {
                while (await reader.ReadAsync(cancellationToken))
                {
                    if (count < PageSize)
                    {
                        nextBeforeSequence = reader.GetInt64(0);
                        reports.Add(new
                        {
                            reportSequence = reader.GetInt64(0),
                            reportId = reader.GetGuid(1),
                            draftId = reader.GetGuid(2),
                            linkId = reader.GetGuid(3),
                            issuanceId = reader.GetGuid(4),
                            programId = reader.GetString(5),
                            nationalVersion = reader.GetInt32(6),
                            pinnedRevision = reader.GetInt64(7),
                            reportedSummary = reader.GetString(8),
                            evidenceRef = reader.GetString(9),
                            claimedOccurredAt = reader.IsDBNull(10) ? (DateTime?)null : reader.GetDateTime(10),
                            recordedAt = reader.GetDateTime(11),
                            verificationStatus = "UNVERIFIED"
                        });
                    }
                    count++;
                }
            }
            await tx.CommitAsync(cancellationToken);
            if (count <= PageSize) nextBeforeSequence = null;
            return Results.Ok(new
            {
                sessionId, classId, sessionTitle, classTitle, scheduledAt,
                activityReports = reports, nextBeforeSequence
            });
        }
        catch (NpgsqlException)
        {
            return Results.Problem(statusCode: 503, title: "Session report lineage unavailable");
        }
        catch (TimeoutException)
        {
            return Results.Problem(statusCode: 503, title: "Session report lineage unavailable");
        }
    }
}
