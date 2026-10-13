using System.Security.Claims;
using Kanoonyar.Api.Security;
using Npgsql;
using NpgsqlTypes;

namespace Kanoonyar.Api.Programs;

/// <summary>Separately revocable class-owner directory grant; not S12 session-catalogue authority.</summary>
public interface ICenterClassCatalogueAuthorization
{
    ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal principal, string centerId,
        CancellationToken cancellationToken);
}
public sealed class UnapprovedCenterClassCatalogueAuthorization : ICenterClassCatalogueAuthorization
{
    public ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal principal, string centerId,
        CancellationToken cancellationToken) => ValueTask.FromResult(false);
}

/// <summary>Lists original center-owned S12 classes, including classes without sessions.
/// No admissions, enrollment, session-delivery, identity or capacity decisions.
/// </summary>
public static class CenterClassCatalogueEndpoint
{
    private const int PageSize = 20;

    public static async Task<IResult> List(
        ClaimsPrincipal principal, IConfiguration configuration,
        ICenterClassCatalogueAuthorization authorization,
        string centerId, Guid? beforeClassId, CancellationToken cancellationToken)
    {
        if (principal.Identity?.IsAuthenticated != true)
            return Results.Problem(statusCode: 401, title: "Authentication required");
        var subject = principal.FindFirst("sub")?.Value;
        if (string.IsNullOrWhiteSpace(subject))
            return Results.Problem(statusCode: 403, title: "Verified subject required");
        if (string.IsNullOrWhiteSpace(centerId) ||
            (beforeClassId.HasValue && beforeClassId.Value == Guid.Empty))
            return Results.Problem(statusCode: 400, title: "Invalid class catalogue request");

        bool permitted;
        try
        {
            permitted = await authorization.IsAuthorizedAsync(
                principal, centerId, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch { permitted = false; }
        if (!permitted)
            return Results.Problem(statusCode: 403, title: "Independent class catalogue grant required");

        var url = configuration["CENTER_DRAFT_READ_DATABASE_URL"];
        if (string.IsNullOrWhiteSpace(url))
            return Results.Problem(statusCode: 503, title: "Class catalogue storage unavailable");
        try
        {
            await using var db = new NpgsqlConnection(url);
            await db.OpenAsync(cancellationToken);
            await using var transaction = await db.BeginTransactionAsync(cancellationToken);
            if (!await RestrictedDraftDatabaseRole.VerifyReadOnlyAsync(db, transaction, cancellationToken))
                return Results.Problem(statusCode: 503, title: "Unsafe class catalogue read role");

            await using (var setScope = new NpgsqlCommand(
                "SELECT set_config('kanoonyar.center_id',@center,true)", db, transaction))
            {
                setScope.Parameters.AddWithValue("center", centerId);
                await setScope.ExecuteNonQueryAsync(cancellationToken);
            }

            // No join to class_sessions: class identity exists even with zero planned sessions.
            const string sql = """
                SELECT c.class_id,c.title
                FROM learning.center_classes c
                WHERE c.center_id=@center
                  AND (@before::uuid IS NULL OR c.class_id<@before::uuid)
                  AND EXISTS(
                    SELECT 1 FROM centers.memberships m
                    JOIN centers.registry registry ON registry.center_id=m.center_id
                    WHERE m.center_id=c.center_id AND m.subject_id=@subject
                      AND m.is_active AND registry.is_active
                  )
                ORDER BY c.class_id DESC LIMIT 21
                """;
            await using var cmd = new NpgsqlCommand(sql, db, transaction);
            cmd.Parameters.AddWithValue("center", centerId);
            cmd.Parameters.AddWithValue("subject", subject);
            cmd.Parameters.Add("before", NpgsqlDbType.Uuid).Value =
                beforeClassId.HasValue ? (object)beforeClassId.Value : DBNull.Value;

            var ids = new List<Guid>();
            var classes = new List<object>();
            await using (var reader = await cmd.ExecuteReaderAsync(cancellationToken))
            {
                while (await reader.ReadAsync(cancellationToken))
                {
                    var classId = reader.GetGuid(0);
                    ids.Add(classId);
                    if (classes.Count < PageSize)
                        classes.Add(new { classId, title = reader.GetString(1) });
                }
            }
            await transaction.CommitAsync(cancellationToken);
            return Results.Ok(new {
                classes,
                nextBeforeClassId = ids.Count > PageSize ? ids[PageSize - 1] : (Guid?)null
            });
        }
        catch (NpgsqlException)
        {
            return Results.Problem(statusCode: 503, title: "Class catalogue storage unavailable");
        }
        catch (TimeoutException)
        {
            return Results.Problem(statusCode: 503, title: "Class catalogue storage unavailable");
        }
    }
}
