using System.Security.Claims;
using System.Text.Json;
using Kanoonyar.Api.Security;
using Npgsql;
using NpgsqlTypes;

namespace Kanoonyar.Api.Programs;

/// <summary>Bounded cursor read of immutable draft revisions; never a permission grant.</summary>
public static class LocalPlanDraftHistoryEndpoint
{
    private const int PageSize = 20;

    public static async Task<IResult> ListHistory(ClaimsPrincipal user, IConfiguration configuration,
        ILocalPlanDraftReadAuthorization authorization, string centerId, Guid draftId,
        long? beforeRevision, CancellationToken cancellationToken)
    {
        if (user.Identity?.IsAuthenticated != true)
            return Results.Problem(statusCode: 401, title: "Authentication required");
        var subject = user.FindFirst("sub")?.Value;
        if (string.IsNullOrWhiteSpace(subject))
            return Results.Problem(statusCode: 403, title: "Subject claim required");
        if (string.IsNullOrWhiteSpace(centerId) || draftId == Guid.Empty ||
            (beforeRevision.HasValue && beforeRevision.Value <= 0))
            return Results.Problem(statusCode: 400, title: "Invalid draft history request");

        bool allowed;
        try
        {
            allowed = await authorization.IsAuthorizedAsync(user, centerId, cancellationToken);
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
            return Results.Problem(statusCode: 403, title: "Local draft read permission not granted");

        var connectionString = configuration["CENTER_DRAFT_READ_DATABASE_URL"];
        if (string.IsNullOrWhiteSpace(connectionString))
            return Results.Problem(statusCode: 503, title: "Draft history storage unavailable");
        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            if (!await RestrictedDraftDatabaseRole.VerifyReadOnlyAsync(connection, transaction, cancellationToken))
                return Results.Problem(statusCode: 503, title: "Unsafe draft history database role");

            await using (var scope = new NpgsqlCommand(
                "SELECT set_config('kanoonyar.center_id', @center, true)", connection, transaction))
            {
                scope.Parameters.AddWithValue("center", centerId);
                await scope.ExecuteNonQueryAsync(cancellationToken);
            }
            await using var command = new NpgsqlCommand(
                """
                SELECT d.revision, d.program_id, d.national_version,
                       d.local_title, d.local_activities::text, d.recorded_at
                FROM programs.center_plan_draft_revisions d
                WHERE d.center_id=@center AND d.draft_id=@draft
                  AND (@before::bigint IS NULL OR d.revision < @before::bigint)
                  AND EXISTS (
                    SELECT 1 FROM centers.memberships m
                    JOIN centers.registry c ON c.center_id=m.center_id
                    WHERE m.center_id=d.center_id AND m.subject_id=@subject
                      AND m.is_active AND c.is_active
                  )
                ORDER BY d.revision DESC
                LIMIT 21
                """, connection, transaction);
            command.Parameters.AddWithValue("center", centerId);
            command.Parameters.AddWithValue("draft", draftId);
            command.Parameters.AddWithValue("subject", subject);
            command.Parameters.Add("before", NpgsqlDbType.Bigint).Value =
                beforeRevision.HasValue ? (object)beforeRevision.Value : DBNull.Value;
            var revisions = new List<object>();
            var revisionNumbers = new List<long>();
            await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            {
                while (await reader.ReadAsync(cancellationToken))
                {
                    var revision = reader.GetInt64(0);
                    revisionNumbers.Add(revision);
                    if (revisions.Count < PageSize)
                    {
                        revisions.Add(new
                        {
                            revision,
                            programId = reader.GetString(1),
                            nationalVersion = reader.GetInt32(2),
                            localTitle = reader.GetString(3),
                            localActivities = JsonSerializer.Deserialize<JsonElement>(reader.GetString(4)),
                            recordedAt = reader.GetDateTime(5)
                        });
                    }
                }
            }
            await transaction.CommitAsync(cancellationToken);
            long? nextBeforeRevision =
                revisionNumbers.Count > PageSize ? revisionNumbers[PageSize - 1] : null;
            return Results.Ok(new { draftId, revisions, nextBeforeRevision });
        }
        catch (NpgsqlException)
        {
            return Results.Problem(statusCode: 503, title: "Draft history storage unavailable");
        }
        catch (TimeoutException)
        {
            return Results.Problem(statusCode: 503, title: "Draft history storage unavailable");
        }
    }
}
