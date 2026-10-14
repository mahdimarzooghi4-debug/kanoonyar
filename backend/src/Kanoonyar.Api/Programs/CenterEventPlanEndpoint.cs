using System.Data;
using System.Security.Claims;
using Kanoonyar.Api.Security;
using Npgsql;
using NpgsqlTypes;

namespace Kanoonyar.Api.Programs;

public sealed record AppendCenterEventPlanRequest(Guid EventId,long ExpectedRevision,
 Guid DraftId,long PinnedPlanRevision,string Title,string Purpose,
 string LocationDescription,DateTimeOffset ScheduledAt);
public interface ICenterEventPlanAuthorization
{
 ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal actor,string centerId,Guid eventId,
  string operation,CancellationToken ct);
}
public sealed class UnapprovedCenterEventPlanAuthorization:ICenterEventPlanAuthorization
{
 public ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal actor,string centerId,Guid eventId,
  string operation,CancellationToken ct)=>ValueTask.FromResult(false);
}
public static class CenterEventPlanEndpoint
{
 private static IResult? Identity(ClaimsPrincipal actor,string centerId,Guid? eventId=null)
 {
  if(actor.Identity?.IsAuthenticated!=true)return Results.Problem(statusCode:401,title:"Authentication required");
  if(string.IsNullOrWhiteSpace(actor.FindFirst("sub")?.Value))
   return Results.Problem(statusCode:403,title:"Verified OIDC subject required");
  if(string.IsNullOrWhiteSpace(centerId)||eventId==Guid.Empty)
   return Results.Problem(statusCode:400,title:"Invalid event scope");
  return null;
 }
 private static async Task<bool> Granted(ICenterEventPlanAuthorization auth,ClaimsPrincipal a,
  string center,Guid id,string op,CancellationToken ct)
 {
  try{return await auth.IsAuthorizedAsync(a,center,id,op,ct);}
  catch(OperationCanceledException) when(ct.IsCancellationRequested){throw;}
  catch{return false;}
 }
 private static bool Valid(AppendCenterEventPlanRequest? x)=>
  x is not null && x.EventId!=Guid.Empty && x.DraftId!=Guid.Empty &&
  x.ExpectedRevision>=0 && x.PinnedPlanRevision>0 &&
  x.Title is not null && x.Title.Trim().Length is >=1 and <=200 &&
  x.Purpose is not null && x.Purpose.Trim().Length is >=1 and <=500 &&
  x.LocationDescription is not null && x.LocationDescription.Trim().Length is >=1 and <=200 &&
  x.ScheduledAt!=default && x.ScheduledAt.Offset==TimeSpan.Zero;
 public static async Task<IResult> Append(ClaimsPrincipal actor,IConfiguration config,
  ICenterEventPlanAuthorization auth,string centerId,AppendCenterEventPlanRequest? body,CancellationToken ct)
 {
  var invalid=Identity(actor,centerId,body?.EventId??Guid.Empty);
  if(invalid is not null)return invalid;
  if(!Valid(body))return Results.Problem(statusCode:400,title:"Invalid private event schedule");
  var b=body!;
  if(!await Granted(auth,actor,centerId,b.EventId,"EVENT_PLAN_APPEND",ct))
   return Results.Problem(statusCode:403,title:"Independent event planning grant required");
  var url=config["CENTER_EVENT_PLAN_WRITE_DATABASE_URL"];
  if(string.IsNullOrWhiteSpace(url))
   return Results.Problem(statusCode:503,title:"Event planning command unavailable");
  try
  {
   await using var db=new NpgsqlConnection(url);await db.OpenAsync(ct);
   await using var tx=await db.BeginTransactionAsync(ct);
   if(!await RestrictedCenterEventPlanRole.WriterAsync(db,tx,ct))
    return Results.Problem(statusCode:503,title:"Unsafe event planning SQL role");
   await using(var scope=new NpgsqlCommand("SELECT set_config('kanoonyar.center_id',@center,true)",db,tx))
   {scope.Parameters.AddWithValue("center",centerId);await scope.ExecuteNonQueryAsync(ct);}
   await using var cmd=new NpgsqlCommand("""
    SELECT events.append_center_event_plan(@center,@event,@expected,@draft,@pinned,
     @title,@purpose,@location,@scheduled,@actor,@audit,@outbox,@correlation)
    """,db,tx);
   cmd.Parameters.AddWithValue("center",centerId);
   cmd.Parameters.AddWithValue("event",b.EventId);
   cmd.Parameters.AddWithValue("expected",b.ExpectedRevision);
   cmd.Parameters.AddWithValue("draft",b.DraftId);
   cmd.Parameters.AddWithValue("pinned",b.PinnedPlanRevision);
   cmd.Parameters.AddWithValue("title",b.Title.Trim());
   cmd.Parameters.AddWithValue("purpose",b.Purpose.Trim());
   cmd.Parameters.AddWithValue("location",b.LocationDescription.Trim());
   cmd.Parameters.AddWithValue("scheduled",b.ScheduledAt.UtcDateTime);
   cmd.Parameters.AddWithValue("actor",actor.FindFirst("sub")!.Value);
   cmd.Parameters.AddWithValue("audit",Guid.NewGuid());
   cmd.Parameters.AddWithValue("outbox",Guid.NewGuid());
   cmd.Parameters.AddWithValue("correlation",Guid.NewGuid().ToString("N"));
   if(await cmd.ExecuteScalarAsync(ct) is not long revision||revision!=b.ExpectedRevision+1)
    throw new InvalidOperationException("Event plan revision was not persisted");
   await tx.CommitAsync(ct);
   return Results.Ok(new{eventId=b.EventId,draftId=b.DraftId,pinnedPlanRevision=b.PinnedPlanRevision,
    revision,visibility="PRIVATE_DRAFT",isPublished=false,isExecuted=false,isRegistrationOpen=false});
  }
  catch(PostgresException e) when(e.SqlState is "40001" or "23503" or "23505" or "23514")
   {return Results.Problem(statusCode:409,title:"Stale event or incompatible pinned plan");}
  catch(PostgresException e) when(e.SqlState=="42501"&&e.MessageText=="Center scope or active membership absent")
   {return Results.Problem(statusCode:403,title:"Live center membership required");}
  catch(PostgresException e) when(e.SqlState=="42501")
   {return Results.Problem(statusCode:503,title:"Event SQL permission unavailable");}
  catch(PostgresException e) when(e.SqlState=="22023")
   {return Results.Problem(statusCode:400,title:"Invalid planned event");}
  catch(NpgsqlException){return Results.Problem(statusCode:503,title:"Event planning storage unavailable");}
  catch(TimeoutException){return Results.Problem(statusCode:503,title:"Event planning storage unavailable");}
 }
 public static Task<IResult> List(ClaimsPrincipal actor,IConfiguration config,
  ICenterEventPlanAuthorization auth,string centerId,Guid? beforeEventId,CancellationToken ct)=>
  Read(actor,config,auth,centerId,null,beforeEventId,null,ct);
 public static Task<IResult> History(ClaimsPrincipal actor,IConfiguration config,
  ICenterEventPlanAuthorization auth,string centerId,Guid eventId,long? beforeRevision,CancellationToken ct)=>
  Read(actor,config,auth,centerId,eventId,null,beforeRevision,ct);
 private static async Task<IResult> Read(ClaimsPrincipal actor,IConfiguration config,
  ICenterEventPlanAuthorization auth,string centerId,Guid? eventId,Guid? beforeEventId,
  long? beforeRevision,CancellationToken ct)
 {
  var invalid=Identity(actor,centerId,eventId);
  if(invalid is not null)return invalid;
  if(beforeEventId==Guid.Empty||(beforeRevision.HasValue&&beforeRevision.Value<1))
   return Results.Problem(statusCode:400,title:"Invalid event cursor");
  var op=eventId.HasValue?"EVENT_PLAN_HISTORY_READ":"EVENT_PLAN_LIST_READ";
  if(!await Granted(auth,actor,centerId,eventId??Guid.Empty,op,ct))
   return Results.Problem(statusCode:403,title:"Independent event read grant required");
  var url=config["CENTER_DRAFT_READ_DATABASE_URL"];
  if(string.IsNullOrWhiteSpace(url))
   return Results.Problem(statusCode:503,title:"Private event read unavailable");
  try
  {
   await using var db=new NpgsqlConnection(url);await db.OpenAsync(ct);
   await using var tx=await db.BeginTransactionAsync(IsolationLevel.RepeatableRead,ct);
   if(!await RestrictedCenterEventPlanRole.ReaderAsync(db,tx,ct))
    return Results.Problem(statusCode:503,title:"Unsafe event reader role");
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
   if(eventId.HasValue)
   {
    Guid draft;long pinned;
    await using(var find=new NpgsqlCommand("""
     SELECT draft_id,pinned_plan_revision FROM events.center_event_plan_bindings
     WHERE center_id=@center AND event_id=@event
     """,db,tx))
    {
     find.Parameters.AddWithValue("center",centerId);
     find.Parameters.AddWithValue("event",eventId.Value);
     await using var rd=await find.ExecuteReaderAsync(ct);
     if(!await rd.ReadAsync(ct))
      return Results.Problem(statusCode:404,title:"Private event not in authorized center");
     draft=rd.GetGuid(0);pinned=rd.GetInt64(1);
    }
    await using var cmd=new NpgsqlCommand("""
     SELECT revision,title,purpose,location_description,scheduled_at,recorded_at
     FROM events.center_event_plan_revisions WHERE center_id=@center AND event_id=@event
       AND (@before::bigint IS NULL OR revision<@before::bigint)
     ORDER BY revision DESC LIMIT 21
     """,db,tx);
    cmd.Parameters.AddWithValue("center",centerId);
    cmd.Parameters.AddWithValue("event",eventId.Value);
    cmd.Parameters.Add("before",NpgsqlDbType.Bigint).Value=
     beforeRevision.HasValue?(object)beforeRevision.Value:DBNull.Value;
    var entries=new List<object>();long? next=null;int n=0;
    await using(var rd=await cmd.ExecuteReaderAsync(ct))
    {
     while(await rd.ReadAsync(ct))
     {
      if(n<20)
      {
       next=rd.GetInt64(0);
       entries.Add(new{revision=rd.GetInt64(0),title=rd.GetString(1),
        purpose=rd.GetString(2),locationDescription=rd.GetString(3),
        scheduledAt=rd.GetDateTime(4),recordedAt=rd.GetDateTime(5),
        visibility="PRIVATE_DRAFT",isPublished=false,isExecuted=false,isRegistrationOpen=false});
      }
      n++;
     }
    }
    await tx.CommitAsync(ct);
    return Results.Ok(new{eventId=eventId.Value,draftId=draft,pinnedPlanRevision=pinned,
     revisions=entries,nextBeforeRevision=n>20?next:null});
   }
   await using var list=new NpgsqlCommand("""
    SELECT b.event_id,b.draft_id,b.pinned_plan_revision,
     r.revision,r.title,r.purpose,r.location_description,r.scheduled_at,r.recorded_at
    FROM events.center_event_plan_bindings b
    JOIN LATERAL(SELECT revision,title,purpose,location_description,scheduled_at,recorded_at
     FROM events.center_event_plan_revisions WHERE center_id=b.center_id
       AND event_id=b.event_id ORDER BY revision DESC LIMIT 1) r ON true
    WHERE b.center_id=@center AND (@before::uuid IS NULL OR b.event_id<@before::uuid)
    ORDER BY b.event_id DESC LIMIT 21
    """,db,tx);
   list.Parameters.AddWithValue("center",centerId);
   list.Parameters.Add("before",NpgsqlDbType.Uuid).Value=
    beforeEventId.HasValue?(object)beforeEventId.Value:DBNull.Value;
   var events=new List<object>();Guid? cursor=null;int total=0;
   await using(var rd=await list.ExecuteReaderAsync(ct))
   {
    while(await rd.ReadAsync(ct))
    {
     if(total<20)
     {
      cursor=rd.GetGuid(0);
      events.Add(new{eventId=rd.GetGuid(0),draftId=rd.GetGuid(1),
       pinnedPlanRevision=rd.GetInt64(2),revision=rd.GetInt64(3),
       title=rd.GetString(4),purpose=rd.GetString(5),locationDescription=rd.GetString(6),
       scheduledAt=rd.GetDateTime(7),recordedAt=rd.GetDateTime(8),
       visibility="PRIVATE_DRAFT",isPublished=false,isExecuted=false,isRegistrationOpen=false});
     }
     total++;
    }
   }
   await tx.CommitAsync(ct);
   return Results.Ok(new{events,nextBeforeEventId=total>20?cursor:null});
  }
  catch(NpgsqlException){return Results.Problem(statusCode:503,title:"Private events unavailable");}
  catch(TimeoutException){return Results.Problem(statusCode:503,title:"Private events unavailable");}
 }
}
