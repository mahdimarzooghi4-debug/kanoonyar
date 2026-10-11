using System.Security.Claims;
using System.Text.Json;
using Kanoonyar.Api.Security;
using Npgsql;

namespace Kanoonyar.Api.Programs;

/// <summary>Authenticated read-only draft history. No membership-derived write authority.</summary>
public static class LocalPlanDraftReadEndpoint
{
    public static async Task<IResult> ListMine(ClaimsPrincipal user, IConfiguration configuration,
        string centerId, CancellationToken cancellationToken)
    {
        if (user.Identity?.IsAuthenticated != true)
            return Results.Problem(statusCode: 401, title: "Authentication required");
        var subject = user.FindFirst("sub")?.Value;
        if (string.IsNullOrWhiteSpace(subject))
            return Results.Problem(statusCode: 403, title: "Subject claim required");
        if (string.IsNullOrWhiteSpace(centerId))
            return Results.BadRequest();
        var connectionString = configuration["CENTER_DRAFT_READ_DATABASE_URL"];
        if (string.IsNullOrWhiteSpace(connectionString))
            return Results.Problem(statusCode: 503, title: "Draft storage unavailable");
        try
        {
            var drafts = new List<object>();
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            if (!await RestrictedDraftDatabaseRole.VerifyAsync(connection, transaction, cancellationToken))
                return Results.Problem(statusCode: 503, title: "Unsafe draft database role");
            // Tenant scope is deliberately transaction-local; the caller DB role must not be able to bypass RLS.
            // This alone does NOT prove center membership. The query additionally checks live membership evidence.
            await using (var scope = new NpgsqlCommand(
                "SELECT set_config('kanoonyar.center_id', @center_id, true)", connection, transaction))
            {
                scope.Parameters.AddWithValue("center_id", centerId);
                await scope.ExecuteNonQueryAsync(cancellationToken);
            }
            await using var command = new NpgsqlCommand(
                """
                SELECT d.draft_id, d.revision, d.program_id, d.national_version,
                       d.local_title, d.local_activities::text
                FROM programs.center_plan_draft_revisions d
                WHERE d.center_id = @center_id
                  AND EXISTS (
                    SELECT 1 FROM centers.memberships m
                    JOIN centers.registry c ON c.center_id = m.center_id
                    WHERE m.center_id = d.center_id AND m.subject_id = @subject
                      AND m.is_active AND c.is_active
                  )
                  AND d.revision = (SELECT max(latest.revision)
                      FROM programs.center_plan_draft_revisions latest
                      WHERE latest.center_id = d.center_id AND latest.draft_id = d.draft_id)
                ORDER BY d.recorded_at DESC, d.draft_id
                LIMIT 200
                """, connection, transaction);
            command.Parameters.AddWithValue("center_id", centerId);
            command.Parameters.AddWithValue("subject", subject);
            await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            {
                while (await reader.ReadAsync(cancellationToken))
                    drafts.Add(new { draftId = reader.GetGuid(0), revision = reader.GetInt64(1),
                        programId = reader.GetString(2), nationalVersion = reader.GetInt32(3),
                        localTitle = reader.GetString(4), localActivities = JsonSerializer.Deserialize<JsonElement>(reader.GetString(5)) });
            }
            await transaction.CommitAsync(cancellationToken);
            return Results.Ok(new { drafts });
        }
        catch (NpgsqlException)
        {
            return Results.Problem(statusCode: 503, title: "Draft storage unavailable");
        }
        catch (TimeoutException)
        {
            return Results.Problem(statusCode: 503, title: "Draft storage unavailable");
        }
    }
}
