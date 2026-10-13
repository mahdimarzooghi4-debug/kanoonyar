using System.Data;
using System.Security.Claims;
using Kanoonyar.Api.Security;
using Npgsql;
using NpgsqlTypes;
namespace Kanoonyar.Api.Programs;
public sealed record AppendWorkResourceNeedRequest(Guid NeedId,long ExpectedRevision,
 string ResourceLabel,string PlanningDetail,DateTimeOffset? DesiredBy);
public interface ICenterWorkResourceNeedAuthorization
{
 ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal actor,string centerId,
  Guid workCardId,string operation,CancellationToken ct);
}
public sealed class UnapprovedWorkResourceNeedAuthorization:ICenterWorkResourceNeedAuthorization
{
 public ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal a,string c,Guid w,string o,
  CancellationToken ct)=>ValueTask.FromResult(false);
}
public static class CenterWorkResourceNeedEndpoint
{
 private static IResult? Validate(ClaimsPrincipal actor,string center,Guid card,Guid? need=null)
 {
  if(actor.Identity?.IsAuthenticated!=true)
   return Results.Problem(statusCode:401,title:"Authentication required");
  if(string.IsNullOrWhiteSpace(actor.FindFirst("sub")?.Value))
   return Results.Problem(statusCode:403,title:"Signed OIDC subject required");
  if(string.IsNullOrWhiteSpace(center)||card==Guid.Empty||need==Guid.Empty)
   return Results.Problem(statusCode:400,title:"Invalid work resource source identity");
  return null;
 }
 private static async Task<bool> Allow(ClaimsPrincipal actor,string center,Guid card,
  string op,ICenterWorkResourceNeedAuthorization grant,ICenterWorkCardAuthorization cards,
  ILocalPlanDraftReadAuthorization plans,CancellationToken ct)
 {
  try
  {
   return await grant.IsAuthorizedAsync(actor,center,card,op,ct)
    && await cards.IsAuthorizedAsync(actor,center,"WORK_CARD_LIST_READ",card,ct)
    && await plans.IsAuthorizedAsync(actor,center,ct);
  }
  catch(OperationCanceledException) when(ct.IsCancellationRequested){throw;}
  catch{return false;}
 }
 public static async Task<IResult> Append(ClaimsPrincipal actor,IConfiguration config,
  ICenterWorkResourceNeedAuthorization grant,ICenterWorkCardAuthorization cards,
  ILocalPlanDraftReadAuthorization plans,string centerId,Guid workCardId,
  AppendWorkResourceNeedRequest? request,CancellationToken ct)
 {
  var invalid=Validate(actor,centerId,workCardId,request?.NeedId);if(invalid is not null)return invalid;
  if(request is null||request.NeedId==Guid.Empty||request.ExpectedRevision<0||
   string.IsNullOrWhiteSpace(request.ResourceLabel)||request.ResourceLabel.Length>160||
   request.PlanningDetail is null||request.PlanningDetail.Length>2000||
   (request.DesiredBy.HasValue&&request.DesiredBy.Value.Offset!=TimeSpan.Zero))
   return Results.Problem(statusCode:400,title:"Invalid resource planning record");
  if(!await Allow(actor,centerId,workCardId,"WORK_RESOURCE_NEED_APPEND",grant,cards,plans,ct))
   return Results.Problem(statusCode:403,title:"Independent resource, work-card and plan grants required");
  var url=config["CENTER_WORK_RESOURCE_NEED_WRITE_DATABASE_URL"];
  if(string.IsNullOrWhiteSpace(url))return Results.Problem(statusCode:503,title:"Resource planning writer unavailable");
  try
  {
   await using var db=new NpgsqlConnection(url);await db.OpenAsync(ct);
   await using var tx=await db.BeginTransactionAsync(ct);
   if(!await RestrictedWorkResourceNeedRole.WriterAsync(db,tx,ct))
    return Results.Problem(statusCode:503,title:"Unsafe resource planning writer");
   await using(var scope=new NpgsqlCommand("SELECT set_config('kanoonyar.center_id',@center,true)",db,tx))
   {scope.Parameters.AddWithValue("center",centerId);await scope.ExecuteNonQueryAsync(ct);}
   await using var cmd=new NpgsqlCommand("""
    SELECT operations.append_work_resource_need(@center,@need,@card,@expected,
     @label,@detail,@desired,@actor,@audit,@event,@correlation)
    """,db,tx);
   cmd.Parameters.AddWithValue("center",centerId);
   cmd.Parameters.AddWithValue("need",request.NeedId);
   cmd.Parameters.AddWithValue("card",workCardId);
   cmd.Parameters.AddWithValue("expected",request.ExpectedRevision);
   cmd.Parameters.AddWithValue("label",request.ResourceLabel.Trim());
   cmd.Parameters.AddWithValue("detail",request.PlanningDetail);
   cmd.Parameters.Add("desired",NpgsqlDbType.TimestampTz).Value=request.DesiredBy.HasValue?
     (object)request.DesiredBy.Value.UtcDateTime:DBNull.Value;
   cmd.Parameters.AddWithValue("actor",actor.FindFirst("sub")!.Value);
   cmd.Parameters.AddWithValue("audit",Guid.NewGuid());
   cmd.Parameters.AddWithValue("event",Guid.NewGuid());
   cmd.Parameters.AddWithValue("correlation",Guid.NewGuid().ToString("N"));
   if(await cmd.ExecuteScalarAsync(ct) is not long revision||
      revision!=request.ExpectedRevision+1)
    throw new InvalidOperationException("Invalid resource planning acknowledgement");
   await tx.CommitAsync(ct);
   return Results.Ok(new{needId=request.NeedId,workCardId,revision,
    visibility="PRIVATE_PLANNING",fulfillmentStatus="UNKNOWN",
    isAllocated=false,isPurchased=false,isFulfilled=false});
  }
  catch(PostgresException e) when(e.SqlState is "40001" or "23503" or "23505" or "23514")
   {return Results.Problem(statusCode:409,title:"Resource need stale or foreign/missing work card");}
  catch(PostgresException e) when(e.SqlState=="42501"&&
   e.MessageText=="Center scope or active membership absent")
   {return Results.Problem(statusCode:403,title:"Live center membership required");}
  catch(PostgresException e) when(e.SqlState=="42501")
   {return Results.Problem(statusCode:503,title:"Restricted resource planning access unavailable");}
  catch(PostgresException e) when(e.SqlState=="22023")
   {return Results.Problem(statusCode:400,title:"Invalid resource planning request");}
  catch(NpgsqlException){return Results.Problem(statusCode:503,title:"Resource planning storage unavailable");}
  catch(TimeoutException){return Results.Problem(statusCode:503,title:"Resource planning storage unavailable");}
 }
 public static Task<IResult> List(ClaimsPrincipal actor,IConfiguration config,
  ICenterWorkResourceNeedAuthorization grant,ICenterWorkCardAuthorization cards,
  ILocalPlanDraftReadAuthorization plans,string centerId,Guid workCardId,
  Guid? beforeNeedId,CancellationToken ct)=>
  Read(actor,config,grant,cards,plans,centerId,workCardId,null,beforeNeedId,null,ct);
 public static Task<IResult> History(ClaimsPrincipal actor,IConfiguration config,
  ICenterWorkResourceNeedAuthorization grant,ICenterWorkCardAuthorization cards,
  ILocalPlanDraftReadAuthorization plans,string centerId,Guid workCardId,
  Guid needId,long? beforeRevision,CancellationToken ct)=>
  Read(actor,config,grant,cards,plans,centerId,workCardId,needId,null,beforeRevision,ct);
 private static async Task<IResult> Read(ClaimsPrincipal actor,IConfiguration config,
  ICenterWorkResourceNeedAuthorization grant,ICenterWorkCardAuthorization cards,
  ILocalPlanDraftReadAuthorization plans,string centerId,Guid workCardId,
  Guid? needId,Guid? beforeId,long? beforeRevision,CancellationToken ct)
 {
  var invalid=Validate(actor,centerId,workCardId,needId);if(invalid is not null)return invalid;
  if(beforeId==Guid.Empty||(beforeRevision.HasValue&&beforeRevision.Value<=0))
   return Results.Problem(statusCode:400,title:"Invalid resource planning cursor");
  var op=needId.HasValue?"WORK_RESOURCE_NEED_HISTORY_READ":"WORK_RESOURCE_NEED_LIST_READ";
  if(!await Allow(actor,centerId,workCardId,op,grant,cards,plans,ct))
   return Results.Problem(statusCode:403,title:"Independent resource, work-card and plan grants required");
  var url=config["CENTER_DRAFT_READ_DATABASE_URL"];
  if(string.IsNullOrWhiteSpace(url))return Results.Problem(statusCode:503,title:"Resource needs reader unavailable");
  try
  {
   await using var db=new NpgsqlConnection(url);await db.OpenAsync(ct);
   await using var tx=await db.BeginTransactionAsync(IsolationLevel.RepeatableRead,ct);
   if(!await RestrictedWorkResourceNeedRole.ReaderAsync(db,tx,ct))
    return Results.Problem(statusCode:503,title:"Unsafe resource planning reader");
   await using(var scope=new NpgsqlCommand("SELECT set_config('kanoonyar.center_id',@center,true)",db,tx))
   {scope.Parameters.AddWithValue("center",centerId);await scope.ExecuteNonQueryAsync(ct);}
   await using(var member=new NpgsqlCommand("""
    SELECT EXISTS(SELECT 1 FROM centers.memberships m JOIN centers.registry c
      ON m.center_id=c.center_id WHERE m.center_id=@center AND m.subject_id=@actor
      AND m.is_active AND c.is_active)
    """,db,tx))
   {
    member.Parameters.AddWithValue("center",centerId);
    member.Parameters.AddWithValue("actor",actor.FindFirst("sub")!.Value);
    if(await member.ExecuteScalarAsync(ct) is not true)
     return Results.Problem(statusCode:403,title:"Live center membership required");
   }
   await using(var owner=new NpgsqlCommand("""
    SELECT EXISTS(SELECT 1 FROM operations.center_work_cards
      WHERE center_id=@center AND work_card_id=@card)
    """,db,tx))
   {
    owner.Parameters.AddWithValue("center",centerId);
    owner.Parameters.AddWithValue("card",workCardId);
    if(await owner.ExecuteScalarAsync(ct) is not true)
     return Results.Problem(statusCode:404,title:"Work card not found for authorized center");
   }
   if(needId.HasValue)
   {
    await using(var identity=new NpgsqlCommand("""
     SELECT EXISTS(SELECT 1 FROM operations.center_work_resource_needs
       WHERE center_id=@center AND work_card_id=@card AND need_id=@need)
     """,db,tx))
    {
     identity.Parameters.AddWithValue("center",centerId);
     identity.Parameters.AddWithValue("card",workCardId);
     identity.Parameters.AddWithValue("need",needId.Value);
     if(await identity.ExecuteScalarAsync(ct) is not true)
      return Results.Problem(statusCode:404,title:"Resource need not found for work card");
    }
    await using var history=new NpgsqlCommand("""
     SELECT revision,resource_label,planning_detail,desired_by,recorded_at
     FROM operations.center_work_resource_need_revisions
     WHERE center_id=@center AND need_id=@need
      AND (@before::bigint IS NULL OR revision<@before::bigint)
     ORDER BY revision DESC LIMIT 21
     """,db,tx);
    history.Parameters.AddWithValue("center",centerId);
    history.Parameters.AddWithValue("need",needId.Value);
    history.Parameters.Add("before",NpgsqlDbType.Bigint).Value=
     beforeRevision.HasValue?(object)beforeRevision.Value:DBNull.Value;
    var items=new List<object>();long? cursor=null;int n=0;
    await using(var rd=await history.ExecuteReaderAsync(ct))
    {
     while(await rd.ReadAsync(ct))
     {
      if(n<20)
      {
       cursor=rd.GetInt64(0);
       items.Add(new{revision=cursor,resourceLabel=rd.GetString(1),
        planningDetail=rd.GetString(2),desiredBy=rd.IsDBNull(3)?(DateTime?)null:rd.GetDateTime(3),
        recordedAt=rd.GetDateTime(4),visibility="PRIVATE_PLANNING",
        fulfillmentStatus="UNKNOWN",isAllocated=false,isPurchased=false,isFulfilled=false});
      }
      n++;
     }
    }
    await tx.CommitAsync(ct);
    return Results.Ok(new{workCardId,needId=needId.Value,revisions=items,
      nextBeforeRevision=n>20?cursor:null,visibility="PRIVATE_PLANNING",
      fulfillmentStatus="UNKNOWN",isAllocated=false,isPurchased=false,isFulfilled=false});
   }
   await using var list=new NpgsqlCommand("""
    SELECT i.need_id,v.revision,v.resource_label,v.planning_detail,v.desired_by,v.recorded_at
    FROM operations.center_work_resource_needs i
    JOIN LATERAL(SELECT revision,resource_label,planning_detail,desired_by,recorded_at
      FROM operations.center_work_resource_need_revisions v WHERE v.center_id=i.center_id
       AND v.need_id=i.need_id ORDER BY revision DESC LIMIT 1) v ON true
    WHERE i.center_id=@center AND i.work_card_id=@card
     AND (@before::uuid IS NULL OR i.need_id<@before::uuid)
    ORDER BY i.need_id DESC LIMIT 21
    """,db,tx);
   list.Parameters.AddWithValue("center",centerId);
   list.Parameters.AddWithValue("card",workCardId);
   list.Parameters.Add("before",NpgsqlDbType.Uuid).Value=beforeId.HasValue?
    (object)beforeId.Value:DBNull.Value;
   var records=new List<object>();Guid? next=null;int count=0;
   await using(var rd=await list.ExecuteReaderAsync(ct))
   {
    while(await rd.ReadAsync(ct))
    {
     if(count<20)
     {
      next=rd.GetGuid(0);
      records.Add(new{needId=next,workCardId,revision=rd.GetInt64(1),
       resourceLabel=rd.GetString(2),planningDetail=rd.GetString(3),
       desiredBy=rd.IsDBNull(4)?(DateTime?)null:rd.GetDateTime(4),
       recordedAt=rd.GetDateTime(5),visibility="PRIVATE_PLANNING",
       fulfillmentStatus="UNKNOWN",isAllocated=false,isPurchased=false,isFulfilled=false});
     }
     count++;
    }
   }
   await tx.CommitAsync(ct);
   return Results.Ok(new{workCardId,needs=records,nextBeforeNeedId=count>20?next:null,
    visibility="PRIVATE_PLANNING",fulfillmentStatus="UNKNOWN",
    isAllocated=false,isPurchased=false,isFulfilled=false});
  }
  catch(NpgsqlException){return Results.Problem(statusCode:503,title:"Resource planning reader unavailable");}
  catch(TimeoutException){return Results.Problem(statusCode:503,title:"Resource planning reader unavailable");}
 }
}
