using System.Security.Claims;
using System.Text.Json;
using Kanoonyar.Api.Security;
using Npgsql;

namespace Kanoonyar.Api.Programs;

/// <summary>
/// Pinned national issuance -> original local draft revision -> latest planned
/// draft revision. Read-only lineage, never proof of performed activities.
/// </summary>
public static class CenterExecutionTraceEndpoint
{
    public static async Task<IResult> GetTrace(
        ClaimsPrincipal user, IConfiguration configuration,
        ILocalPlanDraftReadAuthorization authorization,
        string centerId, Guid draftId, CancellationToken cancellationToken)
    {
        if (user.Identity?.IsAuthenticated != true)
            return Results.Problem(statusCode: 401, title: "Authentication required");
        var subject = user.FindFirst("sub")?.Value;
        if (string.IsNullOrWhiteSpace(subject))
            return Results.Problem(statusCode: 403, title: "Subject claim required");
        if (string.IsNullOrWhiteSpace(centerId) || draftId == Guid.Empty)
            return Results.Problem(statusCode: 400, title: "Invalid plan trace identity");
        bool granted;
        try
        {
            granted = await authorization.IsAuthorizedAsync(user, centerId, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            granted = false;
        }
        if (!granted)
            return Results.Problem(statusCode: 403, title: "Independent plan read grant required");

        var connectionString = configuration["CENTER_DRAFT_READ_DATABASE_URL"];
        if (string.IsNullOrWhiteSpace(connectionString))
            return Results.Problem(statusCode: 503, title: "Plan trace storage unavailable");
        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var tx = await connection.BeginTransactionAsync(cancellationToken);
            if (!await RestrictedDraftDatabaseRole.VerifyReadOnlyAsync(connection, tx, cancellationToken))
                return Results.Problem(statusCode: 503, title: "Unsafe plan trace reader role");

            await using (var scope = new NpgsqlCommand(
                "SELECT set_config('kanoonyar.center_id',@center,true)", connection, tx))
            {
                scope.Parameters.AddWithValue("center", centerId);
                await scope.ExecuteNonQueryAsync(cancellationToken);
            }

            object? trace = null;
            await using (var query = new NpgsqlCommand(
                """
                SELECT l.link_id, l.issuance_id, l.pinned_revision,
                       l.program_id, l.national_version,
                       d.revision, d.local_title, d.local_activities::text
                FROM programs.center_plan_issuance_links l
                JOIN LATERAL (
                  SELECT v.revision, v.local_title, v.local_activities
                  FROM programs.center_plan_draft_revisions v
                  WHERE v.center_id=l.center_id AND v.draft_id=l.draft_id
                  ORDER BY v.revision DESC LIMIT 1
                ) d ON true
                WHERE l.center_id=@center AND l.draft_id=@draft
                  AND EXISTS (
                    SELECT 1 FROM centers.memberships m
                    JOIN centers.registry c ON c.center_id=m.center_id
                    WHERE m.center_id=l.center_id AND m.subject_id=@subject
                      AND m.is_active AND c.is_active
                  )
                """, connection, tx))
            {
                query.Parameters.AddWithValue("center", centerId);
                query.Parameters.AddWithValue("draft", draftId);
                query.Parameters.AddWithValue("subject", subject);
                await using var reader = await query.ExecuteReaderAsync(cancellationToken);
                if (await reader.ReadAsync(cancellationToken))
                {
                    trace = new
                    {
                        linkId = reader.GetGuid(0),
                        issuanceId = reader.GetGuid(1),
                        pinnedRevision = reader.GetInt64(2),
                        programId = reader.GetString(3),
                        nationalVersion = reader.GetInt32(4),
                        latestRevision = reader.GetInt64(5),
                        localTitle = reader.GetString(6),
                        plannedActivities = JsonSerializer.Deserialize<JsonElement>(reader.GetString(7))
                    };
                }
            }
            await tx.CommitAsync(cancellationToken);
            // Null means no visible trace; never reveal whether another center has it.
            return Results.Ok(new { draftId, trace });
        }
        catch (NpgsqlException)
        {
            return Results.Problem(statusCode: 503, title: "Plan trace storage unavailable");
        }
        catch (TimeoutException)
        {
            return Results.Problem(statusCode: 503, title: "Plan trace storage unavailable");
        }
    }
}
