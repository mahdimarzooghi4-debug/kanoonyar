using System.Data;
using System.Security.Claims;
using Kanoonyar.Api.Security;
using Npgsql;
using NpgsqlTypes;

namespace Kanoonyar.Api.Programs;

public sealed record AppendCenterWorkCardRequest(Guid WorkCardId,long ExpectedRevision,
 Guid DraftId,long PinnedPlanRevision,string Title,string PlanningNote,DateTimeOffset? PlannedDueAt);
public interface ICenterWorkCardAuthorization
{
 ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal actor,string centerId,string operation,
  Guid cardId,CancellationToken ct);
}
public sealed class UnapprovedCenterWorkCardAuthorization:ICenterWorkCardAuthorization
{
 public ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal actor,string centerId,string operation,
  Guid cardId,CancellationToken ct)=>ValueTask.FromResult(false);
}
public static class CenterWorkCardEndpoint
{
 private static IResult? Identity(ClaimsPrincipal actor,string centerId,Guid? card=null)
 {
  if(actor.Identity?.IsAuthenticated!=true)
   return Results.Problem(statusCode:401,title:"Authentication required");
  if(string.IsNullOrWhiteSpace(actor.FindFirst("sub")?.Value))
   return Results.Problem(statusCode:403,title:"Verified OIDC subject required");
  if(string.IsNullOrWhiteSpace(centerId)||(card.HasValue&&card.Value==Guid.Empty))
   return Results.Problem(statusCode:400,title:"Invalid work card identity");
  return null;
 }
 private static async Task<bool> Granted(ClaimsPrincipal actor,string center,
  ICenterWorkCardAuthorization grant,ILocalPlanDraftReadAuthorization source,
  string operation,Guid id,CancellationToken ct)
 {
  try{return await grant.IsAuthorizedAsync(actor,center,operation,id,ct)&&
    await source.IsAuthorizedAsync(actor,center,ct);}
  catch(OperationCanceledException) when(ct.IsCancellationRequested){throw;}
  catch{return false;}
 }
 public static async Task<IResult> Append(ClaimsPrincipal actor,IConfiguration config,
  ICenterWorkCardAuthorization grant,ILocalPlanDraftReadAuthorization source,
  string centerId,AppendCenterWorkCardRequest? request,CancellationToken ct)
 {
  var error=Identity(actor,centerId,request?.WorkCardId);if(error is not null)return error;
  if(request is null||request.WorkCardId==Guid.Empty||request.DraftId==Guid.Empty||
   request.PinnedPlanRevision<=0||request.ExpectedRevision<0||
   string.IsNullOrWhiteSpace(request.Title)||request.Title.Length>160||
   request.PlanningNote is null||request.PlanningNote.Length>1200||
   (request.PlannedDueAt.HasValue&&request.PlannedDueAt.Value.Offset!=TimeSpan.Zero))
   return Results.Problem(statusCode:400,title:"Invalid private work-card planning");
  if(!await Granted(actor,centerId,grant,source,"WORK_CARD_APPEND",request.WorkCardId,ct))
   return Results.Problem(statusCode:403,title:"Separate task and source-plan grants required");
  var url=config["CENTER_WORK_CARD_WRITE_DATABASE_URL"];
  if(string.IsNullOrWhiteSpace(url))
   return Results.Problem(statusCode:503,title:"Private work-card writer unavailable");
  try
  {
   await using var db=new NpgsqlConnection(url);await db.OpenAsync(ct);
   await using var tx=await db.BeginTransactionAsync(ct);
   if(!await RestrictedCenterWorkCardRole.WriterAsync(db,tx,ct))
    return Results.Problem(statusCode:503,title:"Unsafe work-card writer role");
   await using(var scope=new NpgsqlCommand("SELECT set_config('kanoonyar.center_id',@center,true)",db,tx))
   {scope.Parameters.AddWithValue("center",centerId);await scope.ExecuteNonQueryAsync(ct);}
   await using var cmd=new NpgsqlCommand("""
    SELECT operations.append_center_work_card(@center,@card,@expected,@draft,@pinned,
      @title,@note,@due,@actor,@audit,@event,@correlation)
    """,db,tx);
   cmd.Parameters.AddWithValue("center",centerId);
   cmd.Parameters.AddWithValue("card",request.WorkCardId);
   cmd.Parameters.AddWithValue("expected",request.ExpectedRevision);
   cmd.Parameters.AddWithValue("draft",request.DraftId);
   cmd.Parameters.AddWithValue("pinned",request.PinnedPlanRevision);
   cmd.Parameters.AddWithValue("title",request.Title.Trim());
   cmd.Parameters.AddWithValue("note",request.PlanningNote);
   cmd.Parameters.Add("due",NpgsqlDbType.TimestampTz).Value=request.PlannedDueAt.HasValue?
    (object)request.PlannedDueAt.Value.UtcDateTime:DBNull.Value;
   cmd.Parameters.AddWithValue("actor",actor.FindFirst("sub")!.Value);
   cmd.Parameters.AddWithValue("audit",Guid.NewGuid());
   cmd.Parameters.AddWithValue("event",Guid.NewGuid());
   cmd.Parameters.AddWithValue("correlation",Guid.NewGuid().ToString("N"));
   if(await cmd.ExecuteScalarAsync(ct) is not long revision||
     revision!=request.ExpectedRevision+1)
    throw new InvalidOperationException("Invalid work-card revision acknowledgement");
   await tx.CommitAsync(ct);
   return Results.Ok(new{workCardId=request.WorkCardId,revision, draftId=request.DraftId,
    pinnedPlanRevision=request.PinnedPlanRevision,visibility="PRIVATE_DRAFT",
    executionStatus="UNKNOWN",isExecuted=false});
  }
  catch(PostgresException e) when(e.SqlState is "23503" or "40001" or "23505" or "23514")
   {return Results.Problem(statusCode:409,title:"Stale work card or invalid source plan");}
  catch(PostgresException e) when(e.SqlState=="42501"&&
    e.MessageText=="Center scope or active membership absent")
   {return Results.Problem(statusCode:403,title:"Live center membership required");}
  catch(PostgresException e) when(e.SqlState=="42501")
   {return Results.Problem(statusCode:503,title:"Work-card SQL operation blocked");}
  catch(PostgresException e) when(e.SqlState=="22023")
   {return Results.Problem(statusCode:400,title:"Invalid work-card data");}
  catch(NpgsqlException){return Results.Problem(statusCode:503,title:"Work-card storage unavailable");}
  catch(TimeoutException){return Results.Problem(statusCode:503,title:"Work-card storage unavailable");}
 }
 public static Task<IResult> List(ClaimsPrincipal actor,IConfiguration config,
  ICenterWorkCardAuthorization grant,ILocalPlanDraftReadAuthorization source,
  string centerId,Guid? beforeCardId,CancellationToken ct)=>
   Read(actor,config,grant,source,centerId,null,beforeCardId,null,ct);
 public static Task<IResult> History(ClaimsPrincipal actor,IConfiguration config,
  ICenterWorkCardAuthorization grant,ILocalPlanDraftReadAuthorization source,
  string centerId,Guid workCardId,long? beforeRevision,CancellationToken ct)=>
   Read(actor,config,grant,source,centerId,workCardId,null,beforeRevision,ct);
 private static async Task<IResult> Read(ClaimsPrincipal actor,IConfiguration config,
  ICenterWorkCardAuthorization grant,ILocalPlanDraftReadAuthorization source,
  string centerId,Guid? cardId,Guid? beforeId,long? beforeRevision,CancellationToken ct)
 {
  var invalid=Identity(actor,centerId,cardId);if(invalid is not null)return invalid;
  if(beforeId==Guid.Empty||(beforeRevision.HasValue&&beforeRevision.Value<=0))
   return Results.Problem(statusCode:400,title:"Invalid work-card read cursor");
  var op=cardId.HasValue?"WORK_CARD_HISTORY_READ":"WORK_CARD_LIST_READ";
  if(!await Granted(actor,centerId,grant,source,op,cardId??Guid.Empty,ct))
   return Results.Problem(statusCode:403,title:"Separate task and source-plan read grants required");
  var url=config["CENTER_DRAFT_READ_DATABASE_URL"];
  if(string.IsNullOrWhiteSpace(url))
   return Results.Problem(statusCode:503,title:"Work-card reader unavailable");
  try
  {
   await using var db=new NpgsqlConnection(url);await db.OpenAsync(ct);
   await using var tx=await db.BeginTransactionAsync(IsolationLevel.RepeatableRead,ct);
   if(!await RestrictedCenterWorkCardRole.ReaderAsync(db,tx,ct))
    return Results.Problem(statusCode:503,title:"Unsafe private work-card reader");
   await using(var scope=new NpgsqlCommand("SELECT set_config('kanoonyar.center_id',@center,true)",db,tx))
   {scope.Parameters.AddWithValue("center",centerId);await scope.ExecuteNonQueryAsync(ct);}
   await using(var member=new NpgsqlCommand("""
    SELECT EXISTS(SELECT 1 FROM centers.memberships m JOIN centers.registry c
     ON c.center_id=m.center_id WHERE m.center_id=@center AND m.subject_id=@actor
       AND m.is_active AND c.is_active)
    """,db,tx))
   {
    member.Parameters.AddWithValue("center",centerId);
    member.Parameters.AddWithValue("actor",actor.FindFirst("sub")!.Value);
    if(await member.ExecuteScalarAsync(ct) is not true)
     return Results.Problem(statusCode:403,title:"Live center membership required");
   }
   if(cardId.HasValue)
   {
    Guid draft;long pinned;
    await using(var exists=new NpgsqlCommand("""
     SELECT draft_id,pinned_plan_revision FROM operations.center_work_cards
     WHERE center_id=@center AND work_card_id=@card
     """,db,tx))
    {
     exists.Parameters.AddWithValue("center",centerId);
     exists.Parameters.AddWithValue("card",cardId.Value);
     await using var rd=await exists.ExecuteReaderAsync(ct);
     if(!await rd.ReadAsync(ct))
      return Results.Problem(statusCode:404,title:"Work card absent from authorized center");
     draft=rd.GetGuid(0);pinned=rd.GetInt64(1);
    }
    await using var history=new NpgsqlCommand("""
     SELECT revision,title,planning_note,planned_due_at,recorded_at
     FROM operations.center_work_card_revisions
     WHERE center_id=@center AND work_card_id=@card
      AND (@before::bigint IS NULL OR revision<@before::bigint)
     ORDER BY revision DESC LIMIT 21
     """,db,tx);
    history.Parameters.AddWithValue("center",centerId);
    history.Parameters.AddWithValue("card",cardId.Value);
    history.Parameters.Add("before",NpgsqlDbType.Bigint).Value=beforeRevision.HasValue?
     (object)beforeRevision.Value:DBNull.Value;
    var rows=new List<object>();long? cursor=null;int count=0;
    await using(var rd=await history.ExecuteReaderAsync(ct))
    {
     while(await rd.ReadAsync(ct))
     {
      if(count<20)
      {
       cursor=rd.GetInt64(0);
       rows.Add(new{revision=cursor,title=rd.GetString(1),planningNote=rd.GetString(2),
        plannedDueAt=rd.IsDBNull(3)?(DateTime?)null:rd.GetDateTime(3),
        recordedAt=rd.GetDateTime(4),visibility="PRIVATE_DRAFT",
        executionStatus="UNKNOWN",isExecuted=false});
      }
      count++;
     }
    }
    await tx.CommitAsync(ct);
    return Results.Ok(new{workCardId=cardId.Value,draftId=draft,pinnedPlanRevision=pinned,
      revisions=rows,nextBeforeRevision=count>20?cursor:null,visibility="PRIVATE_DRAFT",
      executionStatus="UNKNOWN",isExecuted=false});
   }
   await using var list=new NpgsqlCommand("""
    SELECT i.work_card_id,i.draft_id,i.pinned_plan_revision,v.revision,
      v.title,v.planning_note,v.planned_due_at,v.recorded_at
    FROM operations.center_work_cards i JOIN LATERAL(
     SELECT revision,title,planning_note,planned_due_at,recorded_at
     FROM operations.center_work_card_revisions r
     WHERE r.center_id=i.center_id AND r.work_card_id=i.work_card_id
     ORDER BY revision DESC LIMIT 1
    ) v ON true
    WHERE i.center_id=@center AND (@before::uuid IS NULL OR i.work_card_id<@before::uuid)
    ORDER BY i.work_card_id DESC LIMIT 21
    """,db,tx);
   list.Parameters.AddWithValue("center",centerId);
   list.Parameters.Add("before",NpgsqlDbType.Uuid).Value=beforeId.HasValue?
    (object)beforeId.Value:DBNull.Value;
   var cards=new List<object>();Guid? cursorId=null;int total=0;
   await using(var rd=await list.ExecuteReaderAsync(ct))
   {
    while(await rd.ReadAsync(ct))
    {
     if(total<20)
     {
      cursorId=rd.GetGuid(0);
      cards.Add(new{workCardId=cursorId,draftId=rd.GetGuid(1),
       pinnedPlanRevision=rd.GetInt64(2),revision=rd.GetInt64(3),
       title=rd.GetString(4),planningNote=rd.GetString(5),
       plannedDueAt=rd.IsDBNull(6)?(DateTime?)null:rd.GetDateTime(6),
       recordedAt=rd.GetDateTime(7),visibility="PRIVATE_DRAFT",
       executionStatus="UNKNOWN",isExecuted=false});
     }
     total++;
    }
   }
   await tx.CommitAsync(ct);
   return Results.Ok(new{cards,nextBeforeCardId=total>20?cursorId:null,
    visibility="PRIVATE_DRAFT",executionStatus="UNKNOWN",isExecuted=false});
  }
  catch(NpgsqlException){return Results.Problem(statusCode:503,title:"Work-card read unavailable");}
  catch(TimeoutException){return Results.Problem(statusCode:503,title:"Work-card read unavailable");}
 }
}
