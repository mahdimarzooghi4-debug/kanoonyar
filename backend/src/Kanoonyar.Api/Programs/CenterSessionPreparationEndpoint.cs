using System.Data;
using System.Security.Claims;
using Kanoonyar.Api.Security;
using Npgsql;
using NpgsqlTypes;

namespace Kanoonyar.Api.Programs;

public sealed record AppendSessionPreparationRequest(
 Guid SessionId,Guid ClassId,long ExpectedRevision,string Title,string TeachingOutline,string MaterialsNote);
public interface ICenterSessionPreparationAuthorization
{
 ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal actor,string centerId,string operation,
  Guid sessionId,CancellationToken ct);
}
public sealed class UnapprovedCenterSessionPreparationAuthorization:ICenterSessionPreparationAuthorization
{
 public ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal actor,string centerId,string operation,
  Guid sessionId,CancellationToken ct)=>ValueTask.FromResult(false);
}
public static class CenterSessionPreparationEndpoint
{
 private static IResult? Identity(ClaimsPrincipal actor,string center,Guid? sourceId=null)
 {
  if(actor.Identity?.IsAuthenticated!=true)
   return Results.Problem(statusCode:401,title:"Authentication required");
  if(string.IsNullOrWhiteSpace(actor.FindFirst("sub")?.Value))
   return Results.Problem(statusCode:403,title:"Verified OIDC subject required");
  if(string.IsNullOrWhiteSpace(center)||(sourceId.HasValue&&sourceId.Value==Guid.Empty))
   return Results.Problem(statusCode:400,title:"Invalid source identity");
  return null;
 }
 private static async Task<bool> Granted(ClaimsPrincipal actor,string center,
  ICenterSessionPreparationAuthorization grant,ICenterSourceCatalogReadAuthorization source,
  string op,Guid session,CancellationToken ct)
 {
  try{return await grant.IsAuthorizedAsync(actor,center,op,session,ct)
    && await source.IsAuthorizedAsync(actor,center,ct);}
  catch(OperationCanceledException) when(ct.IsCancellationRequested){throw;}
  catch{return false;}
 }
 public static async Task<IResult> Append(ClaimsPrincipal actor,IConfiguration config,
  ICenterSessionPreparationAuthorization grant,ICenterSourceCatalogReadAuthorization source,
  string centerId,AppendSessionPreparationRequest? request,CancellationToken ct)
 {
  var error=Identity(actor,centerId,request?.SessionId);if(error is not null)return error;
  if(request is null||request.SessionId==Guid.Empty||request.ClassId==Guid.Empty||
    request.ExpectedRevision<0||string.IsNullOrWhiteSpace(request.Title)||
    request.Title.Length>160||request.TeachingOutline is null||
    request.TeachingOutline.Length>4000||request.MaterialsNote is null||
    request.MaterialsNote.Length>2000)
   return Results.Problem(statusCode:400,title:"Invalid private session preparation");
  if(!await Granted(actor,centerId,grant,source,"SESSION_PREPARATION_APPEND",request.SessionId,ct))
   return Results.Problem(statusCode:403,title:"Separate session preparation and source grants required");
  var url=config["CENTER_SESSION_PREPARATION_WRITE_DATABASE_URL"];
  if(string.IsNullOrWhiteSpace(url))
   return Results.Problem(statusCode:503,title:"Private preparation writer unavailable");
  try
  {
   await using var db=new NpgsqlConnection(url);await db.OpenAsync(ct);
   await using var tx=await db.BeginTransactionAsync(ct);
   if(!await RestrictedSessionPreparationRole.WriterAsync(db,tx,ct))
    return Results.Problem(statusCode:503,title:"Unsafe session preparation writer");
   await using(var scope=new NpgsqlCommand("SELECT set_config('kanoonyar.center_id',@center,true)",db,tx))
   {scope.Parameters.AddWithValue("center",centerId);await scope.ExecuteNonQueryAsync(ct);}
   await using var cmd=new NpgsqlCommand("""
    SELECT learning.append_session_preparation(@center,@session,@class,@expected,
      @title,@outline,@materials,@actor,@audit,@event,@correlation)
    """,db,tx);
   cmd.Parameters.AddWithValue("center",centerId);
   cmd.Parameters.AddWithValue("session",request.SessionId);
   cmd.Parameters.AddWithValue("class",request.ClassId);
   cmd.Parameters.AddWithValue("expected",request.ExpectedRevision);
   cmd.Parameters.AddWithValue("title",request.Title.Trim());
   cmd.Parameters.AddWithValue("outline",request.TeachingOutline);
   cmd.Parameters.AddWithValue("materials",request.MaterialsNote);
   cmd.Parameters.AddWithValue("actor",actor.FindFirst("sub")!.Value);
   cmd.Parameters.AddWithValue("audit",Guid.NewGuid());
   cmd.Parameters.AddWithValue("event",Guid.NewGuid());
   cmd.Parameters.AddWithValue("correlation",Guid.NewGuid().ToString("N"));
   if(await cmd.ExecuteScalarAsync(ct) is not long revision||
      revision!=request.ExpectedRevision+1)
    throw new InvalidOperationException("Unexpected session preparation acknowledgement");
   await tx.CommitAsync(ct);
   return Results.Ok(new{sessionId=request.SessionId,classId=request.ClassId,revision,
    visibility="PRIVATE_PREPARATION",isPublished=false,deliveryVerified=false,
    isAttendanceVerified=false,isAssessed=false});
  }
  catch(PostgresException e) when(e.SqlState is "23503" or "23505" or "23514" or "40001")
   {return Results.Problem(statusCode:409,title:"Stale preparation or foreign/nonexistent source session");}
  catch(PostgresException e) when(e.SqlState=="42501"&&
   e.MessageText=="Center scope or active membership absent")
   {return Results.Problem(statusCode:403,title:"Live center membership required");}
  catch(PostgresException e) when(e.SqlState=="42501")
   {return Results.Problem(statusCode:503,title:"Preparation SQL access unavailable");}
  catch(PostgresException e) when(e.SqlState=="22023")
   {return Results.Problem(statusCode:400,title:"Invalid preparation command");}
  catch(NpgsqlException){return Results.Problem(statusCode:503,title:"Preparation storage unavailable");}
  catch(TimeoutException){return Results.Problem(statusCode:503,title:"Preparation storage unavailable");}
 }
 public static Task<IResult> List(ClaimsPrincipal actor,IConfiguration config,
  ICenterSessionPreparationAuthorization grant,ICenterSourceCatalogReadAuthorization source,
  string centerId,Guid? beforeSessionId,CancellationToken ct)=>
  Read(actor,config,grant,source,centerId,null,beforeSessionId,null,ct);
 public static Task<IResult> History(ClaimsPrincipal actor,IConfiguration config,
  ICenterSessionPreparationAuthorization grant,ICenterSourceCatalogReadAuthorization source,
  string centerId,Guid sessionId,long? beforeRevision,CancellationToken ct)=>
  Read(actor,config,grant,source,centerId,sessionId,null,beforeRevision,ct);
 private static async Task<IResult> Read(ClaimsPrincipal actor,IConfiguration config,
  ICenterSessionPreparationAuthorization grant,ICenterSourceCatalogReadAuthorization source,
  string centerId,Guid? sessionId,Guid? beforeSession,long? beforeRevision,CancellationToken ct)
 {
  var invalid=Identity(actor,centerId,sessionId);if(invalid is not null)return invalid;
  if(beforeSession==Guid.Empty||(beforeRevision.HasValue&&beforeRevision.Value<=0))
   return Results.Problem(statusCode:400,title:"Invalid preparation read cursor");
  var op=sessionId.HasValue?"SESSION_PREPARATION_HISTORY_READ":"SESSION_PREPARATION_LIST_READ";
  if(!await Granted(actor,centerId,grant,source,op,sessionId??Guid.Empty,ct))
   return Results.Problem(statusCode:403,title:"Separate preparation and source read grants required");
  var url=config["CENTER_DRAFT_READ_DATABASE_URL"];
  if(string.IsNullOrWhiteSpace(url))
   return Results.Problem(statusCode:503,title:"Preparation read unavailable");
  try
  {
   await using var db=new NpgsqlConnection(url);await db.OpenAsync(ct);
   await using var tx=await db.BeginTransactionAsync(IsolationLevel.RepeatableRead,ct);
   if(!await RestrictedSessionPreparationRole.ReaderAsync(db,tx,ct))
    return Results.Problem(statusCode:503,title:"Unsafe session preparation reader");
   await using(var scope=new NpgsqlCommand("SELECT set_config('kanoonyar.center_id',@center,true)",db,tx))
   {scope.Parameters.AddWithValue("center",centerId);await scope.ExecuteNonQueryAsync(ct);}
   await using(var membership=new NpgsqlCommand("""
    SELECT EXISTS(SELECT 1 FROM centers.memberships m JOIN centers.registry c
     ON c.center_id=m.center_id WHERE m.center_id=@center AND m.subject_id=@subject
      AND m.is_active AND c.is_active)
    """,db,tx))
   {
    membership.Parameters.AddWithValue("center",centerId);
    membership.Parameters.AddWithValue("subject",actor.FindFirst("sub")!.Value);
    if(await membership.ExecuteScalarAsync(ct) is not true)
     return Results.Problem(statusCode:403,title:"Live center membership required");
   }
   if(sessionId.HasValue)
   {
    Guid classId;string sessionTitle;
    await using(var identity=new NpgsqlCommand("""
     SELECT i.class_id,s.session_title FROM learning.session_preparations i
     JOIN learning.class_sessions s ON s.center_id=i.center_id AND s.session_id=i.session_id
     WHERE i.center_id=@center AND i.session_id=@session
     """,db,tx))
    {
     identity.Parameters.AddWithValue("center",centerId);
     identity.Parameters.AddWithValue("session",sessionId.Value);
     await using var rd=await identity.ExecuteReaderAsync(ct);
     if(!await rd.ReadAsync(ct))
      return Results.Problem(statusCode:404,title:"Private session preparation not found");
     classId=rd.GetGuid(0);sessionTitle=rd.GetString(1);
    }
    await using var cmd=new NpgsqlCommand("""
     SELECT revision,preparation_title,teaching_outline,materials_note,recorded_at
     FROM learning.session_preparation_revisions
     WHERE center_id=@center AND session_id=@session
      AND (@before::bigint IS NULL OR revision<@before::bigint)
     ORDER BY revision DESC LIMIT 21
     """,db,tx);
    cmd.Parameters.AddWithValue("center",centerId);
    cmd.Parameters.AddWithValue("session",sessionId.Value);
    cmd.Parameters.Add("before",NpgsqlDbType.Bigint).Value=beforeRevision.HasValue?
      (object)beforeRevision.Value:DBNull.Value;
    var rows=new List<object>();long? cursor=null;int count=0;
    await using(var rd=await cmd.ExecuteReaderAsync(ct))
    {
     while(await rd.ReadAsync(ct))
     {
      if(count<20)
      {
       cursor=rd.GetInt64(0);
       rows.Add(new{revision=cursor,title=rd.GetString(1),
        teachingOutline=rd.GetString(2),materialsNote=rd.GetString(3),
        recordedAt=rd.GetDateTime(4),visibility="PRIVATE_PREPARATION",
        isPublished=false,deliveryVerified=false,isAttendanceVerified=false,isAssessed=false});
      }
      count++;
     }
    }
    await tx.CommitAsync(ct);
    return Results.Ok(new{sessionId=sessionId.Value,classId,sessionTitle,revisions=rows,
      nextBeforeRevision=count>20?cursor:null,visibility="PRIVATE_PREPARATION",
      isPublished=false,deliveryVerified=false,isAttendanceVerified=false,isAssessed=false});
   }
   await using var list=new NpgsqlCommand("""
    SELECT i.session_id,i.class_id,s.session_title,c.title,
      v.revision,v.preparation_title,v.teaching_outline,v.materials_note,v.recorded_at
    FROM learning.session_preparations i
    JOIN learning.class_sessions s ON s.center_id=i.center_id AND s.session_id=i.session_id
    JOIN learning.center_classes c ON c.center_id=i.center_id AND c.class_id=i.class_id
    JOIN LATERAL (SELECT revision,preparation_title,teaching_outline,materials_note,recorded_at
      FROM learning.session_preparation_revisions r
      WHERE r.center_id=i.center_id AND r.session_id=i.session_id
      ORDER BY revision DESC LIMIT 1) v ON true
    WHERE i.center_id=@center AND (@before::uuid IS NULL OR i.session_id<@before::uuid)
    ORDER BY i.session_id DESC LIMIT 21
    """,db,tx);
   list.Parameters.AddWithValue("center",centerId);
   list.Parameters.Add("before",NpgsqlDbType.Uuid).Value=beforeSession.HasValue?
     (object)beforeSession.Value:DBNull.Value;
   var records=new List<object>();Guid? last=null;int seen=0;
   await using(var rd=await list.ExecuteReaderAsync(ct))
   {
    while(await rd.ReadAsync(ct))
    {
     if(seen<20)
     {
      last=rd.GetGuid(0);
      records.Add(new{sessionId=last,classId=rd.GetGuid(1),sessionTitle=rd.GetString(2),
       classTitle=rd.GetString(3),revision=rd.GetInt64(4),title=rd.GetString(5),
       teachingOutline=rd.GetString(6),materialsNote=rd.GetString(7),
       recordedAt=rd.GetDateTime(8),visibility="PRIVATE_PREPARATION",
       isPublished=false,deliveryVerified=false,isAttendanceVerified=false,isAssessed=false});
     }
     seen++;
    }
   }
   await tx.CommitAsync(ct);
   return Results.Ok(new{preparations=records,nextBeforeSessionId=seen>20?last:null,
    visibility="PRIVATE_PREPARATION",isPublished=false,deliveryVerified=false,
    isAttendanceVerified=false,isAssessed=false});
  }
  catch(NpgsqlException){return Results.Problem(statusCode:503,title:"Preparation source unavailable");}
  catch(TimeoutException){return Results.Problem(statusCode:503,title:"Preparation source unavailable");}
 }
}
