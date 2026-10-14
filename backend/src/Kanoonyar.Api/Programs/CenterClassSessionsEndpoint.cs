using System.Security.Claims;
using Kanoonyar.Api.Security;
using Npgsql;
using NpgsqlTypes;

namespace Kanoonyar.Api.Programs;

/// <summary>
/// Original S12 class-session read, scoped to one real owner class.
/// No parallel records, inferred attendance or new operation grant.
/// </summary>
public static class CenterClassSessionsEndpoint
{
    private const int PageSize = 20;

    public static async Task<IResult> List(
        ClaimsPrincipal principal, IConfiguration configuration,
        ICenterSourceCatalogReadAuthorization authorization,
        string centerId, Guid classId, Guid? beforeSourceId,
        CancellationToken cancellationToken)
    {
        if (principal.Identity?.IsAuthenticated != true)
            return Results.Problem(statusCode: 401, title: "Authentication required");
        var subject = principal.FindFirst("sub")?.Value;
        if (string.IsNullOrWhiteSpace(subject))
            return Results.Problem(statusCode: 403, title: "Verified subject required");
        if (string.IsNullOrWhiteSpace(centerId) || classId == Guid.Empty ||
            (beforeSourceId.HasValue && beforeSourceId.Value == Guid.Empty))
            return Results.Problem(statusCode: 400, title: "Invalid class session query");

        bool permitted;
        try { permitted = await authorization.IsAuthorizedAsync(principal, centerId, cancellationToken); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch { permitted = false; }
        if (!permitted)
            return Results.Problem(statusCode: 403, title: "Independent S12 source catalogue grant required");

        var url = configuration["CENTER_DRAFT_READ_DATABASE_URL"];
        if (string.IsNullOrWhiteSpace(url))
            return Results.Problem(statusCode: 503, title: "Source session storage unavailable");
        try
        {
            await using var db = new NpgsqlConnection(url);
            await db.OpenAsync(cancellationToken);
            await using var tx = await db.BeginTransactionAsync(cancellationToken);
            if (!await RestrictedDraftDatabaseRole.VerifyReadOnlyAsync(db, tx, cancellationToken))
                return Results.Problem(statusCode: 503, title: "Unsafe class-session read role");

            await using (var setScope = new NpgsqlCommand(
                "SELECT set_config('kanoonyar.center_id',@center,true)", db, tx))
            {
                setScope.Parameters.AddWithValue("center", centerId);
                await setScope.ExecuteNonQueryAsync(cancellationToken);
            }

            // A real class with no sessions is distinguishable from a nonexistent,
            // revoked or wrong-center class, without fabricating a session.
            const string ownerSql = """
                SELECT c.title FROM learning.center_classes c
                WHERE c.center_id=@center AND c.class_id=@class
                  AND EXISTS (
                    SELECT 1 FROM centers.memberships m
                    JOIN centers.registry registry ON registry.center_id=m.center_id
                    WHERE m.center_id=c.center_id AND m.subject_id=@subject
                      AND m.is_active AND registry.is_active
                  )
                """;
            string? classTitle;
            await using (var owner = new NpgsqlCommand(ownerSql, db, tx))
            {
                owner.Parameters.AddWithValue("center", centerId);
                owner.Parameters.AddWithValue("class", classId);
                owner.Parameters.AddWithValue("subject", subject);
                classTitle = await owner.ExecuteScalarAsync(cancellationToken) as string;
            }
            if (classTitle is null)
                return Results.Problem(statusCode: 404, title: "Class source not found");

            const string sessionsSql = """
                SELECT s.session_id,s.session_title,s.scheduled_at
                FROM learning.class_sessions s
                WHERE s.center_id=@center AND s.class_id=@class
                  AND (@before::uuid IS NULL OR s.session_id<@before::uuid)
                ORDER BY s.session_id DESC LIMIT 21
                """;
            var ids = new List<Guid>();
            var records = new List<object>();
            await using (var command = new NpgsqlCommand(sessionsSql, db, tx))
            {
                command.Parameters.AddWithValue("center", centerId);
                command.Parameters.AddWithValue("class", classId);
                command.Parameters.Add("before", NpgsqlDbType.Uuid).Value =
                    beforeSourceId.HasValue ? (object)beforeSourceId.Value : DBNull.Value;
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    var sessionId = reader.GetGuid(0);
                    ids.Add(sessionId);
                    if (records.Count < PageSize)
                        records.Add(new {
                            sourceId = sessionId,
                            classId,
                            title = reader.GetString(1),
                            classTitle,
                            scheduledAt = reader.IsDBNull(2) ? (DateTime?)null : reader.GetDateTime(2)
                        });
                }
            }
            await tx.CommitAsync(cancellationToken);
            return Results.Ok(new {
                sourceKind = "CLASS_SESSION",
                classId,
                classTitle,
                records,
                nextBeforeSourceId = ids.Count > PageSize ? ids[PageSize - 1] : (Guid?)null
            });
        }
        catch (NpgsqlException)
        {
            return Results.Problem(statusCode: 503, title: "Source session storage unavailable");
        }
        catch (TimeoutException)
        {
            return Results.Problem(statusCode: 503, title: "Source session storage unavailable");
        }
    }
}
