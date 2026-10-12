using System.Security.Claims;
using Kanoonyar.Api.Security;
using Npgsql;
using NpgsqlTypes;

namespace Kanoonyar.Api.Programs;

/// <summary>Source catalogue authorization is independent of draft or activity-report read grants.</summary>
public interface ICenterSourceCatalogReadAuthorization
{
    ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal actor, string centerId,
        CancellationToken cancellationToken);
}
public sealed class UnapprovedCenterSourceCatalogReadAuthorization :
    ICenterSourceCatalogReadAuthorization
{
    public ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal actor, string centerId,
        CancellationToken cancellationToken) => ValueTask.FromResult(false);
}

/// <summary>Owner modules' planned source identities only; never proof of delivery.</summary>
public static class CenterSourceCatalogEndpoint
{
    private const int PageSize = 20;
    public static async Task<IResult> ListSources(
        ClaimsPrincipal principal, IConfiguration configuration,
        ICenterSourceCatalogReadAuthorization authorization,
        string centerId, string sourceKind, Guid? beforeSourceId,
        CancellationToken cancellationToken)
    {
        if (principal.Identity?.IsAuthenticated != true)
            return Results.Problem(statusCode: 401, title: "Authentication required");
        var subject = principal.FindFirst("sub")?.Value;
        if (string.IsNullOrWhiteSpace(subject))
            return Results.Problem(statusCode: 403, title: "Verified subject required");
        if (string.IsNullOrWhiteSpace(centerId) ||
            (sourceKind != "class-sessions" && sourceKind != "events") ||
            (beforeSourceId.HasValue && beforeSourceId.Value == Guid.Empty))
            return Results.Problem(statusCode: 400, title: "Invalid source catalogue request");

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
            return Results.Problem(statusCode: 403, title: "Independent source catalogue read grant required");

        var url = configuration["CENTER_DRAFT_READ_DATABASE_URL"];
        if (string.IsNullOrWhiteSpace(url))
            return Results.Problem(statusCode: 503, title: "Source catalogue storage unavailable");
        try
        {
            await using var connection = new NpgsqlConnection(url);
            await connection.OpenAsync(cancellationToken);
            await using var tx = await connection.BeginTransactionAsync(cancellationToken);
            if (!await RestrictedDraftDatabaseRole.VerifyReadOnlyAsync(connection, tx, cancellationToken))
                return Results.Problem(statusCode: 503, title: "Unsafe source catalogue SQL role");

            await using (var scope = new NpgsqlCommand(
                "SELECT set_config('kanoonyar.center_id',@center,true)", connection, tx))
            {
                scope.Parameters.AddWithValue("center", centerId);
                await scope.ExecuteNonQueryAsync(cancellationToken);
            }
            // SQL is a fixed allowlist: no table or column identifier from the client.
            const string sessionsSql = """
                SELECT s.session_id,s.session_title,s.class_id,c.title,s.scheduled_at
                FROM learning.class_sessions s
                JOIN learning.center_classes c ON c.center_id=s.center_id AND c.class_id=s.class_id
                WHERE s.center_id=@center AND (@before::uuid IS NULL OR s.session_id<@before::uuid)
                  AND EXISTS (
                    SELECT 1 FROM centers.memberships m JOIN centers.registry registry
                      ON registry.center_id=m.center_id
                    WHERE m.center_id=s.center_id AND m.subject_id=@subject
                      AND m.is_active AND registry.is_active
                  )
                ORDER BY s.session_id DESC LIMIT 21
                """;
            const string eventsSql = """
                SELECT e.event_id,e.event_title,NULL::uuid,NULL::text,e.scheduled_at
                FROM events.center_events e
                WHERE e.center_id=@center AND (@before::uuid IS NULL OR e.event_id<@before::uuid)
                  AND EXISTS (
                    SELECT 1 FROM centers.memberships m JOIN centers.registry registry
                      ON registry.center_id=m.center_id
                    WHERE m.center_id=e.center_id AND m.subject_id=@subject
                      AND m.is_active AND registry.is_active
                  )
                ORDER BY e.event_id DESC LIMIT 21
                """;
            await using var command = new NpgsqlCommand(
                sourceKind == "class-sessions" ? sessionsSql : eventsSql, connection, tx);
            command.Parameters.AddWithValue("center", centerId);
            command.Parameters.AddWithValue("subject", subject);
            command.Parameters.Add("before", NpgsqlDbType.Uuid).Value =
                beforeSourceId.HasValue ? (object)beforeSourceId.Value : DBNull.Value;
            var records = new List<object>();
            var ids = new List<Guid>();
            await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            {
                while (await reader.ReadAsync(cancellationToken))
                {
                    var id = reader.GetGuid(0);
                    ids.Add(id);
                    if (records.Count < PageSize)
                    {
                        records.Add(new {
                            sourceId = id,
                            title = reader.GetString(1),
                            classId = reader.IsDBNull(2) ? (Guid?)null : reader.GetGuid(2),
                            classTitle = reader.IsDBNull(3) ? null : reader.GetString(3),
                            scheduledAt = reader.IsDBNull(4) ? (DateTime?)null : reader.GetDateTime(4)
                        });
                    }
                }
            }
            await tx.CommitAsync(cancellationToken);
            Guid? nextBeforeSourceId = ids.Count > PageSize ? ids[PageSize - 1] : null;
            return Results.Ok(new {
                sourceKind = sourceKind == "class-sessions" ? "CLASS_SESSION" : "CENTER_EVENT",
                records, nextBeforeSourceId
            });
        }
        catch (NpgsqlException)
        {
            return Results.Problem(statusCode: 503, title: "Source catalogue storage unavailable");
        }
        catch (TimeoutException)
        {
            return Results.Problem(statusCode: 503, title: "Source catalogue storage unavailable");
        }
    }
}
