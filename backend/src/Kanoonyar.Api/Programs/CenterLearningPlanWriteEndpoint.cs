using System.Security.Claims;
using Kanoonyar.Api.Security;
using Npgsql;
using NpgsqlTypes;

namespace Kanoonyar.Api.Programs;

public sealed record CreateCenterClassRequest(Guid ClassId, string Title);
public sealed record CreateCenterSessionRequest(Guid SessionId, Guid ClassId,
    string Title, DateTimeOffset? ScheduledAt);

/// <summary>
/// Must independently verify a revocable (center, actor, resource, operation) grant.
/// Mere JWT role or center membership must never authorize a write.
/// </summary>
public interface ICenterLearningPlanWriteAuthorization
{
    ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal actor,string centerId,
        string operation,Guid resourceId,CancellationToken cancellationToken);
}
public sealed class UnapprovedCenterLearningPlanWriteAuthorization :
    ICenterLearningPlanWriteAuthorization
{
    public ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal actor,string centerId,
        string operation,Guid resourceId,CancellationToken cancellationToken) =>
        ValueTask.FromResult(false);
}

public static class CenterLearningPlanWriteEndpoint
{
    public static Task<IResult> CreateClass(ClaimsPrincipal actor,IConfiguration configuration,
        ICenterLearningPlanWriteAuthorization grant,string centerId,
        CreateCenterClassRequest? request,CancellationToken cancellationToken)
    {
        return Record(actor,configuration,grant,centerId,"CLASS_PLAN_CREATE",
            request?.ClassId ?? Guid.Empty,Guid.Empty,request?.Title,
            null,cancellationToken);
    }

    public static Task<IResult> ScheduleSession(ClaimsPrincipal actor,IConfiguration configuration,
        ICenterLearningPlanWriteAuthorization grant,string centerId,
        CreateCenterSessionRequest? request,CancellationToken cancellationToken)
    {
        if (request is not null &&
            (request.ClassId==Guid.Empty ||
             (request.ScheduledAt.HasValue &&
              request.ScheduledAt.Value.Offset!=TimeSpan.Zero)))
            return Task.FromResult<IResult>(Results.Problem(statusCode:400,
                title:"Class identity and explicit UTC schedule required"));
        return Record(actor,configuration,grant,centerId,"CLASS_SESSION_PLAN_CREATE",
            request?.SessionId ?? Guid.Empty,request?.ClassId ?? Guid.Empty,
            request?.Title,request?.ScheduledAt,cancellationToken);
    }

    private static async Task<IResult> Record(ClaimsPrincipal actor,
        IConfiguration configuration,ICenterLearningPlanWriteAuthorization grant,
        string centerId,string operation,Guid resourceId,Guid classId,
        string? title,DateTimeOffset? scheduledAt,CancellationToken ct)
    {
        if (actor.Identity?.IsAuthenticated!=true)
            return Results.Problem(statusCode:401,title:"Authentication required");
        var subject=actor.FindFirst("sub")?.Value;
        if(string.IsNullOrWhiteSpace(subject))
            return Results.Problem(statusCode:403,title:"Verified subject required");
        if(string.IsNullOrWhiteSpace(centerId)||resourceId==Guid.Empty||
           string.IsNullOrWhiteSpace(title) || title.Length>512)
            return Results.Problem(statusCode:400,title:"Invalid class/session planning request");
        bool authorized;
        try { authorized=await grant.IsAuthorizedAsync(actor,centerId,operation,resourceId,ct); }
        catch (OperationCanceledException) when(ct.IsCancellationRequested) { throw; }
        catch { authorized=false; }
        if(!authorized)
            return Results.Problem(statusCode:403,title:"Independent learning write grant required");

        var url=configuration["CENTER_LEARNING_WRITE_DATABASE_URL"];
        if(string.IsNullOrWhiteSpace(url))
            return Results.Problem(statusCode:503,title:"Learning storage unavailable");
        try
        {
            await using var db=new NpgsqlConnection(url);
            await db.OpenAsync(ct);
            await using var tx=await db.BeginTransactionAsync(ct);
            if(!await RestrictedLearningCommandRole.VerifyAsync(db,tx,ct))
                return Results.Problem(statusCode:503,title:"Unsafe learning command SQL role");
            await using(var scope=new NpgsqlCommand(
                "SELECT set_config('kanoonyar.center_id',@center,true)",db,tx))
            {
                scope.Parameters.AddWithValue("center",centerId);
                await scope.ExecuteNonQueryAsync(ct);
            }
            await using(var membership=new NpgsqlCommand(
                """
                SELECT EXISTS(
                  SELECT 1 FROM centers.memberships m
                  JOIN centers.registry c ON c.center_id=m.center_id
                  WHERE m.center_id=@center AND m.subject_id=@subject
                    AND m.is_active AND c.is_active)
                """,db,tx))
            {
                membership.Parameters.AddWithValue("center",centerId);
                membership.Parameters.AddWithValue("subject",subject);
                if(await membership.ExecuteScalarAsync(ct) is not true)
                    return Results.Problem(statusCode:403,title:"Active center membership required");
            }

            var sql=operation=="CLASS_PLAN_CREATE" ?
                """
                SELECT learning.record_class_plan(
                  @center,@resource,@title,@actor,@audit,@event,@correlation)
                """ :
                """
                SELECT learning.record_class_session_plan(
                  @center,@resource,@class_id,@title,@scheduled,@actor,@audit,@event,@correlation)
                """;
            await using var cmd=new NpgsqlCommand(sql,db,tx);
            cmd.Parameters.AddWithValue("center",centerId);
            cmd.Parameters.AddWithValue("resource",resourceId);
            cmd.Parameters.AddWithValue("title",title.Trim());
            cmd.Parameters.AddWithValue("actor",subject);
            cmd.Parameters.AddWithValue("audit",Guid.NewGuid());
            cmd.Parameters.AddWithValue("event",Guid.NewGuid());
            cmd.Parameters.AddWithValue("correlation",Guid.NewGuid().ToString("N"));
            if(operation=="CLASS_SESSION_PLAN_CREATE")
            {
                cmd.Parameters.AddWithValue("class_id",classId);
                cmd.Parameters.Add("scheduled",NpgsqlDbType.TimestampTz).Value =
                    scheduledAt.HasValue ? (object)scheduledAt.Value : DBNull.Value;
            }
            var stored=(Guid)(await cmd.ExecuteScalarAsync(ct)
                ?? throw new InvalidOperationException("Missing persisted class/session identity"));
            await tx.CommitAsync(ct);
            return operation=="CLASS_PLAN_CREATE"
                ? Results.Ok(new { classId=stored })
                : Results.Ok(new { sessionId=stored,classId,scheduledAt });
        }
        catch(PostgresException e) when(e.SqlState is "23505" or "23503" or "23514" or "40001")
        {
            return Results.Problem(statusCode:409,title:"Class/session identity conflict or nonexistent class");
        }
        catch(PostgresException e) when(e.SqlState=="42501")
        {
            return Results.Problem(statusCode:403,title:"Class/session center membership or scope denied");
        }
        catch(PostgresException e) when(e.SqlState=="22023")
        {
            return Results.Problem(statusCode:400,title:"Invalid class/session command");
        }
        catch(NpgsqlException)
        {
            return Results.Problem(statusCode:503,title:"Learning storage unavailable");
        }
        catch(TimeoutException)
        {
            return Results.Problem(statusCode:503,title:"Learning storage unavailable");
        }
    }
}
