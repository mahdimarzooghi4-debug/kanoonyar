using System.Data;
using System.Security.Claims;
using Kanoonyar.Api.Security;
using Npgsql;
using NpgsqlTypes;

namespace Kanoonyar.Api.Programs;

public sealed record SubmitCenterCollaborationProposalRequest(
 Guid ProposalId,string TargetCenterId,string Title,string Summary);
public interface ICenterCollaborationAuthorization
{
 ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal actor,string centerId,
  string operation,CancellationToken ct);
}
public sealed class UnapprovedCenterCollaborationAuthorization:ICenterCollaborationAuthorization
{
 public ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal actor,string centerId,
  string operation,CancellationToken ct)=>ValueTask.FromResult(false);
}
public static class CenterCollaborationEndpoint
{
 private static IResult? Identity(ClaimsPrincipal actor,string center)
 {
  if(actor.Identity?.IsAuthenticated!=true)
   return Results.Problem(statusCode:401,title:"Authentication required");
  if(string.IsNullOrWhiteSpace(actor.FindFirst("sub")?.Value))
   return Results.Problem(statusCode:403,title:"Verified OIDC subject required");
  if(string.IsNullOrWhiteSpace(center))
   return Results.Problem(statusCode:400,title:"Invalid center identity");
  return null;
 }
 private static async Task<bool> Allowed(ICenterCollaborationAuthorization authorization,
  ClaimsPrincipal actor,string center,string op,CancellationToken ct)
 {
  try{return await authorization.IsAuthorizedAsync(actor,center,op,ct);}
  catch(OperationCanceledException) when(ct.IsCancellationRequested){throw;}
  catch{return false;}
 }
 public static async Task<IResult> Submit(ClaimsPrincipal actor,IConfiguration config,
  ICenterCollaborationAuthorization authorization,string centerId,
  SubmitCenterCollaborationProposalRequest? request,CancellationToken ct)
 {
  var invalid=Identity(actor,centerId);if(invalid is not null)return invalid;
  if(request is null||request.ProposalId==Guid.Empty||
   string.IsNullOrWhiteSpace(request.TargetCenterId)||
   request.TargetCenterId==centerId||
   string.IsNullOrWhiteSpace(request.Title)||request.Title.Length>160||
   string.IsNullOrWhiteSpace(request.Summary)||request.Summary.Length>1200)
   return Results.Problem(statusCode:400,title:"Invalid explicitly directed collaboration proposal");
  if(!await Allowed(authorization,actor,centerId,"COLLABORATION_PROPOSAL_CREATE",ct))
   return Results.Problem(statusCode:403,title:"Independent outgoing collaboration grant required");
  var url=config["CENTER_COLLABORATION_WRITE_DATABASE_URL"];
  if(string.IsNullOrWhiteSpace(url))
   return Results.Problem(statusCode:503,title:"Collaboration write storage unavailable");
  try
  {
   await using var db=new NpgsqlConnection(url);await db.OpenAsync(ct);
   await using var tx=await db.BeginTransactionAsync(ct);
   if(!await RestrictedCollaborationRole.WriterAsync(db,tx,ct))
    return Results.Problem(statusCode:503,title:"Unsafe collaboration writer role");
   await using(var scope=new NpgsqlCommand("SELECT set_config('kanoonyar.center_id',@center,true)",db,tx))
   {scope.Parameters.AddWithValue("center",centerId);await scope.ExecuteNonQueryAsync(ct);}
   await using var cmd=new NpgsqlCommand("""
    SELECT collaboration.record_center_proposal(@id,@source,@target,@title,@summary,
      @actor,@audit,@event,@correlation)
    """,db,tx);
   cmd.Parameters.AddWithValue("id",request.ProposalId);
   cmd.Parameters.AddWithValue("source",centerId);
   cmd.Parameters.AddWithValue("target",request.TargetCenterId);
   cmd.Parameters.AddWithValue("title",request.Title.Trim());
   cmd.Parameters.AddWithValue("summary",request.Summary.Trim());
   cmd.Parameters.AddWithValue("actor",actor.FindFirst("sub")!.Value);
   cmd.Parameters.AddWithValue("audit",Guid.NewGuid());
   cmd.Parameters.AddWithValue("event",Guid.NewGuid());
   cmd.Parameters.AddWithValue("correlation",Guid.NewGuid().ToString("N"));
   if(await cmd.ExecuteScalarAsync(ct) is not Guid recorded||
    recorded!=request.ProposalId)
    throw new InvalidOperationException("Invalid proposal acknowledgement");
   await tx.CommitAsync(ct);
   return Results.Ok(new{proposalId=recorded,sourceCenterId=centerId,
     targetCenterId=request.TargetCenterId,visibility="EXPLICIT_TARGET_ONLY",
     reviewStatus="PROPOSED_UNREVIEWED",isAccepted=false,isExecuted=false});
  }
  catch(PostgresException e) when(e.SqlState is "23505" or "23503" or "23514" or "40001")
   {return Results.Problem(statusCode:409,title:"Duplicate or unavailable collaboration proposal");}
  catch(PostgresException e) when(e.SqlState=="42501"&&
   e.MessageText=="Source membership or center scope absent")
   {return Results.Problem(statusCode:403,title:"Live source center membership required");}
  catch(PostgresException e) when(e.SqlState=="42501")
   {return Results.Problem(statusCode:503,title:"Collaboration write SQL privilege missing");}
  catch(PostgresException e) when(e.SqlState=="22023")
   {return Results.Problem(statusCode:400,title:"Invalid proposal payload");}
  catch(NpgsqlException){return Results.Problem(statusCode:503,title:"Collaboration storage unavailable");}
  catch(TimeoutException){return Results.Problem(statusCode:503,title:"Collaboration storage unavailable");}
 }
 public static Task<IResult> Outgoing(ClaimsPrincipal actor,IConfiguration config,
  ICenterCollaborationAuthorization authorization,string centerId,Guid? beforeProposalId,
  CancellationToken ct)=>Read(actor,config,authorization,centerId,beforeProposalId,false,ct);
 public static Task<IResult> Incoming(ClaimsPrincipal actor,IConfiguration config,
  ICenterCollaborationAuthorization authorization,string centerId,Guid? beforeProposalId,
  CancellationToken ct)=>Read(actor,config,authorization,centerId,beforeProposalId,true,ct);
 private static async Task<IResult> Read(ClaimsPrincipal actor,IConfiguration config,
  ICenterCollaborationAuthorization authorization,string centerId,Guid? before,
  bool incoming,CancellationToken ct)
 {
  var invalid=Identity(actor,centerId);if(invalid is not null)return invalid;
  if(before==Guid.Empty)return Results.Problem(statusCode:400,title:"Invalid collaboration cursor");
  var operation=incoming?"COLLABORATION_INCOMING_READ":"COLLABORATION_OUTGOING_READ";
  if(!await Allowed(authorization,actor,centerId,operation,ct))
   return Results.Problem(statusCode:403,title:"Independent collaboration read grant required");
  var url=config["CENTER_DRAFT_READ_DATABASE_URL"];
  if(string.IsNullOrWhiteSpace(url))
   return Results.Problem(statusCode:503,title:"Collaboration read unavailable");
  try
  {
   await using var db=new NpgsqlConnection(url);await db.OpenAsync(ct);
   await using var tx=await db.BeginTransactionAsync(IsolationLevel.RepeatableRead,ct);
   if(!await RestrictedCollaborationRole.ReaderAsync(db,tx,ct))
    return Results.Problem(statusCode:503,title:"Unsafe collaboration reader role");
   await using(var scope=new NpgsqlCommand("SELECT set_config('kanoonyar.center_id',@center,true)",db,tx))
   {scope.Parameters.AddWithValue("center",centerId);await scope.ExecuteNonQueryAsync(ct);}
   await using(var member=new NpgsqlCommand("""
    SELECT EXISTS(SELECT 1 FROM centers.memberships m JOIN centers.registry c
     ON c.center_id=m.center_id
     WHERE m.center_id=@center AND m.subject_id=@actor
       AND m.is_active AND c.is_active)
    """,db,tx))
   {
    member.Parameters.AddWithValue("center",centerId);
    member.Parameters.AddWithValue("actor",actor.FindFirst("sub")!.Value);
    if(await member.ExecuteScalarAsync(ct) is not true)
     return Results.Problem(statusCode:403,title:"Live center membership required");
   }
   await using var q=new NpgsqlCommand(incoming?"""
    SELECT proposal_id,source_center_id,target_center_id,title,summary,recorded_at
    FROM collaboration.center_proposals
    WHERE target_center_id=@center
     AND (@before::uuid IS NULL OR proposal_id<@before::uuid)
    ORDER BY proposal_id DESC LIMIT 21
    """:"""
    SELECT proposal_id,source_center_id,target_center_id,title,summary,recorded_at
    FROM collaboration.center_proposals
    WHERE source_center_id=@center
     AND (@before::uuid IS NULL OR proposal_id<@before::uuid)
    ORDER BY proposal_id DESC LIMIT 21
    """,db,tx);
   q.Parameters.AddWithValue("center",centerId);
   q.Parameters.Add("before",NpgsqlDbType.Uuid).Value=before.HasValue?
    (object)before.Value:DBNull.Value;
   var proposals=new List<object>();Guid? cursor=null;int count=0;
   await using(var reader=await q.ExecuteReaderAsync(ct))
   {
    while(await reader.ReadAsync(ct))
    {
     if(count<20)
     {
      cursor=reader.GetGuid(0);
      proposals.Add(new{proposalId=cursor,sourceCenterId=reader.GetString(1),
       targetCenterId=reader.GetString(2),title=reader.GetString(3),
       summary=reader.GetString(4),recordedAt=reader.GetDateTime(5),
       visibility="EXPLICIT_TARGET_ONLY",reviewStatus="PROPOSED_UNREVIEWED",
       isAccepted=false,isExecuted=false});
     }
     count++;
    }
   }
   await tx.CommitAsync(ct);
   return Results.Ok(new{proposals,nextBeforeProposalId=count>20?cursor:null,
    direction=incoming?"INCOMING":"OUTGOING",visibility="EXPLICIT_TARGET_ONLY",
    reviewStatus="PROPOSED_UNREVIEWED",isAccepted=false,isExecuted=false});
  }
  catch(NpgsqlException){return Results.Problem(statusCode:503,title:"Collaboration read unavailable");}
  catch(TimeoutException){return Results.Problem(statusCode:503,title:"Collaboration read unavailable");}
 }
}
