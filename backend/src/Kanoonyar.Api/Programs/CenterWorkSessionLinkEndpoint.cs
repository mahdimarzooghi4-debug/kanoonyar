using System.Data;
using System.Security.Claims;
using Kanoonyar.Api.Security;
using Npgsql;
using NpgsqlTypes;

namespace Kanoonyar.Api.Programs;

public sealed record AppendWorkSessionLinkRequest(Guid SessionId);
public interface IWorkCardSessionLinkAuthorization
{
 ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal actor,string center,Guid card,
  string operation,CancellationToken ct);
}
public sealed class UnapprovedWorkCardSessionLinkAuthorization:IWorkCardSessionLinkAuthorization
{
 public ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal actor,string center,Guid card,
  string operation,CancellationToken ct)=>ValueTask.FromResult(false);
}
public static class CenterWorkSessionLinkEndpoint
{
 private static IResult? Identity(ClaimsPrincipal a,string center,Guid card)
 {
  if(a.Identity?.IsAuthenticated!=true)
   return Results.Problem(statusCode:401,title:"Authentication required");
  if(string.IsNullOrWhiteSpace(a.FindFirst("sub")?.Value))
   return Results.Problem(statusCode:403,title:"Signed OIDC subject required");
  if(string.IsNullOrWhiteSpace(center)||card==Guid.Empty)
   return Results.Problem(statusCode:400,title:"Invalid center/card identity");
  return null;
 }
 private static async Task<bool> Granted(ClaimsPrincipal a,string center,Guid card,
  IWorkCardSessionLinkAuthorization grant,ICenterWorkCardAuthorization cards,
  ILocalPlanDraftReadAuthorization plans,ICenterSourceCatalogReadAuthorization sessions,
  string operation,CancellationToken ct)
 {
  try
  {
   return await grant.IsAuthorizedAsync(a,center,card,operation,ct)&&
    await cards.IsAuthorizedAsync(a,center,"WORK_CARD_LIST_READ",card,ct)&&
    await plans.IsAuthorizedAsync(a,center,ct)&&
    await sessions.IsAuthorizedAsync(a,center,ct);
  }
  catch(OperationCanceledException) when(ct.IsCancellationRequested){throw;}
  catch{return false;}
 }
 public static async Task<IResult> Append(ClaimsPrincipal actor,IConfiguration config,
  IWorkCardSessionLinkAuthorization grant,ICenterWorkCardAuthorization cards,
  ILocalPlanDraftReadAuthorization plans,ICenterSourceCatalogReadAuthorization sessions,
  string centerId,Guid workCardId,AppendWorkSessionLinkRequest? request,CancellationToken ct)
 {
  var invalid=Identity(actor,centerId,workCardId);if(invalid is not null)return invalid;
  if(request is null||request.SessionId==Guid.Empty)
   return Results.Problem(statusCode:400,title:"A genuine session identity is required");
  if(!await Granted(actor,centerId,workCardId,grant,cards,plans,sessions,
   "WORK_CARD_SESSION_LINK_APPEND",ct))
   return Results.Problem(statusCode:403,title:"Independent link, work-card, plan and session permissions required");
  var url=config["CENTER_WORK_SESSION_LINK_WRITE_DATABASE_URL"];
  if(string.IsNullOrWhiteSpace(url))
   return Results.Problem(statusCode:503,title:"Session-link writer unavailable");
  try
  {
   await using var db=new NpgsqlConnection(url);await db.OpenAsync(ct);
   await using var tx=await db.BeginTransactionAsync(ct);
   if(!await RestrictedWorkSessionLinkRole.WriterAsync(db,tx,ct))
    return Results.Problem(statusCode:503,title:"Unsafe session-link writer");
   await using(var scope=new NpgsqlCommand(
    "SELECT set_config('kanoonyar.center_id',@center,true)",db,tx))
   {
    scope.Parameters.AddWithValue("center",centerId);
    await scope.ExecuteNonQueryAsync(ct);
   }
   await using var command=new NpgsqlCommand("""
    SELECT operations.append_work_card_session_link(@center,@card,@session,
     @actor,@audit,@event,@correlation)
    """,db,tx);
   command.Parameters.AddWithValue("center",centerId);
   command.Parameters.AddWithValue("card",workCardId);
   command.Parameters.AddWithValue("session",request.SessionId);
   command.Parameters.AddWithValue("actor",actor.FindFirst("sub")!.Value);
   command.Parameters.AddWithValue("audit",Guid.NewGuid());
   command.Parameters.AddWithValue("event",Guid.NewGuid());
   command.Parameters.AddWithValue("correlation",Guid.NewGuid().ToString("N"));
   if(await command.ExecuteScalarAsync(ct) is not Guid id||id!=request.SessionId)
    throw new InvalidOperationException("Unexpected source-link result");
   await tx.CommitAsync(ct);
   return Results.Ok(new{workCardId,sessionId=request.SessionId,
    visibility="PRIVATE_PLANNING",isPublished=false,isExecuted=false,
    isAttendanceVerified=false,isDeliveryVerified=false});
  }
  catch(PostgresException e) when(e.SqlState is "23503" or "23505" or "23514" or "40001")
   {return Results.Problem(statusCode:409,title:"Duplicate, missing or foreign original source");}
  catch(PostgresException e) when(e.SqlState=="42501"&&
   e.MessageText=="Center scope or active membership absent")
   {return Results.Problem(statusCode:403,title:"Active center membership absent");}
  catch(PostgresException e) when(e.SqlState=="42501")
   {return Results.Problem(statusCode:503,title:"Restricted session-link operation blocked");}
  catch(PostgresException e) when(e.SqlState=="22023")
   {return Results.Problem(statusCode:400,title:"Invalid link request");}
  catch(NpgsqlException){return Results.Problem(statusCode:503,title:"Session-link storage unavailable");}
  catch(TimeoutException){return Results.Problem(statusCode:503,title:"Session-link storage unavailable");}
 }
 public static async Task<IResult> List(ClaimsPrincipal actor,IConfiguration config,
  IWorkCardSessionLinkAuthorization grant,ICenterWorkCardAuthorization cards,
  ILocalPlanDraftReadAuthorization plans,ICenterSourceCatalogReadAuthorization sessions,
  string centerId,Guid workCardId,Guid? beforeSessionId,CancellationToken ct)
 {
  var invalid=Identity(actor,centerId,workCardId);if(invalid is not null)return invalid;
  if(beforeSessionId==Guid.Empty)
   return Results.Problem(statusCode:400,title:"Invalid session keyset cursor");
  if(!await Granted(actor,centerId,workCardId,grant,cards,plans,sessions,
    "WORK_CARD_SESSION_LINK_LIST_READ",ct))
   return Results.Problem(statusCode:403,title:"Independent link, work-card, plan and session permissions required");
  var url=config["CENTER_DRAFT_READ_DATABASE_URL"];
  if(string.IsNullOrWhiteSpace(url))
   return Results.Problem(statusCode:503,title:"Session-link reader unavailable");
  try
  {
   await using var db=new NpgsqlConnection(url);await db.OpenAsync(ct);
   await using var tx=await db.BeginTransactionAsync(IsolationLevel.RepeatableRead,ct);
   if(!await RestrictedWorkSessionLinkRole.ReaderAsync(db,tx,ct))
    return Results.Problem(statusCode:503,title:"Unsafe session-link reader");
   await using(var scope=new NpgsqlCommand(
    "SELECT set_config('kanoonyar.center_id',@center,true)",db,tx))
   {
    scope.Parameters.AddWithValue("center",centerId);
    await scope.ExecuteNonQueryAsync(ct);
   }
   await using(var source=new NpgsqlCommand("""
    SELECT EXISTS(SELECT 1 FROM operations.center_work_cards c
     JOIN centers.memberships m ON m.center_id=c.center_id
     JOIN centers.registry registry ON registry.center_id=c.center_id
     WHERE c.center_id=@center AND c.work_card_id=@card AND
      m.subject_id=@actor AND m.is_active AND registry.is_active)
    """,db,tx))
   {
    source.Parameters.AddWithValue("center",centerId);
    source.Parameters.AddWithValue("card",workCardId);
    source.Parameters.AddWithValue("actor",actor.FindFirst("sub")!.Value);
    if(await source.ExecuteScalarAsync(ct) is not true)
     return Results.Problem(statusCode:404,title:"No accessible original work card");
   }
   await using var query=new NpgsqlCommand("""
    SELECT l.session_id,s.class_id,s.session_title,c.title,s.scheduled_at,l.recorded_at
    FROM operations.work_card_session_links l
    JOIN learning.class_sessions s ON s.center_id=l.center_id AND s.session_id=l.session_id
    JOIN learning.center_classes c ON c.center_id=s.center_id AND c.class_id=s.class_id
    WHERE l.center_id=@center AND l.work_card_id=@card
     AND (@before::uuid IS NULL OR l.session_id<@before::uuid)
    ORDER BY l.session_id DESC LIMIT 21
    """,db,tx);
   query.Parameters.AddWithValue("center",centerId);
   query.Parameters.AddWithValue("card",workCardId);
   query.Parameters.Add("before",NpgsqlDbType.Uuid).Value=beforeSessionId.HasValue?
    (object)beforeSessionId.Value:DBNull.Value;
   var items=new List<object>();Guid? cursor=null;int count=0;
   await using(var read=await query.ExecuteReaderAsync(ct))
   {
    while(await read.ReadAsync(ct))
    {
     if(count<20)
     {
      cursor=read.GetGuid(0);
      items.Add(new{
       sessionId=cursor,classId=read.GetGuid(1),sessionTitle=read.GetString(2),
       classTitle=read.GetString(3),
       scheduledAt=read.IsDBNull(4)?(DateTime?)null:read.GetDateTime(4),
       recordedAt=read.GetDateTime(5),visibility="PRIVATE_PLANNING",
       isPublished=false,isExecuted=false,isAttendanceVerified=false,
       isDeliveryVerified=false});
     }
     count++;
    }
   }
   await tx.CommitAsync(ct);
   return Results.Ok(new{workCardId,links=items,nextBeforeSessionId=count>20?cursor:null,
    visibility="PRIVATE_PLANNING",isPublished=false,isExecuted=false,
    isAttendanceVerified=false,isDeliveryVerified=false});
  }
  catch(NpgsqlException){return Results.Problem(statusCode:503,title:"Session link source unavailable");}
  catch(TimeoutException){return Results.Problem(statusCode:503,title:"Session link source unavailable");}
 }
}
