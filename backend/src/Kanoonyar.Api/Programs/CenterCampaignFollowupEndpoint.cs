using System.Security.Claims;
using Kanoonyar.Api.Security;
using Npgsql;
using NpgsqlTypes;
namespace Kanoonyar.Api.Programs;
public sealed record AppendCampaignFollowupRequest(Guid EntryId,string EntryKind,Guid? RequestId,string Content);

/// <summary>Only clarifications on UNVERIFIED inspections; never acceptance, resolution or execution.</summary>
public static class CenterCampaignFollowupEndpoint
{
 private static IResult? Identity(ClaimsPrincipal actor,string center,Guid campaign,Guid association,Guid note)
 {
  if(actor.Identity?.IsAuthenticated!=true)return Results.Problem(statusCode:401,title:"Authentication required");
  if(string.IsNullOrWhiteSpace(actor.FindFirst("sub")?.Value))
   return Results.Problem(statusCode:403,title:"Verified OIDC subject required");
  if(string.IsNullOrWhiteSpace(center)||campaign==Guid.Empty||association==Guid.Empty||note==Guid.Empty)
   return Results.Problem(statusCode:400,title:"Invalid clarification scope");
  return null;
 }
 private static async Task<bool> Granted(ClaimsPrincipal actor,string center,Guid campaign,
  string operation,ICenterCampaignReportLinkAuthorization links,
  ICenterActivityReportReadAuthorization reports,CancellationToken ct)
 {
  try{
   return await links.IsAuthorizedAsync(actor,center,campaign,operation,ct)
    && await reports.IsAuthorizedAsync(actor,center,ct);
  }
  catch(OperationCanceledException) when(ct.IsCancellationRequested){throw;}
  catch{return false;}
 }
 public static async Task<IResult> Append(ClaimsPrincipal actor,IConfiguration config,
  ICenterCampaignReportLinkAuthorization links,ICenterActivityReportReadAuthorization reports,
  string centerId,Guid campaignId,Guid associationId,Guid noteId,AppendCampaignFollowupRequest? body,CancellationToken ct)
 {
  var invalid=Identity(actor,centerId,campaignId,associationId,noteId);if(invalid is not null)return invalid;
  if(body is null||body.EntryId==Guid.Empty||body.Content is null||
    body.Content.Trim().Length is <1 or >1000||
    !(body.EntryKind=="REQUEST"&&body.RequestId is null||
      body.EntryKind=="RESPONSE"&&body.RequestId.HasValue&&body.RequestId!=Guid.Empty))
   return Results.Problem(statusCode:400,title:"Invalid clarification request or response");
  if(!await Granted(actor,centerId,campaignId,"CAMPAIGN_INSPECTION_FOLLOWUP_APPEND",links,reports,ct))
   return Results.Problem(statusCode:403,title:"Both followup and source read grants required");
  var url=config["CENTER_CAMPAIGN_FOLLOWUP_WRITE_DATABASE_URL"];
  if(string.IsNullOrWhiteSpace(url))
   return Results.Problem(statusCode:503,title:"Clarification command unavailable");
  try{
   await using var db=new NpgsqlConnection(url);await db.OpenAsync(ct);
   await using var tx=await db.BeginTransactionAsync(ct);
   if(!await RestrictedCampaignFollowupRole.WriterAsync(db,tx,ct))
    return Results.Problem(statusCode:503,title:"Unsafe clarification writer role");
   await using(var scope=new NpgsqlCommand("SELECT set_config('kanoonyar.center_id',@center,true)",db,tx))
   {scope.Parameters.AddWithValue("center",centerId);await scope.ExecuteNonQueryAsync(ct);}
   await using var q=new NpgsqlCommand(
    """
    SELECT campaigns.append_inspection_followup(@center,@campaign,@association,@note,
      @entry,@request,@kind,@content,@actor,@audit,@event,@correlation)
    """,db,tx);
   q.Parameters.AddWithValue("center",centerId);
   q.Parameters.AddWithValue("campaign",campaignId);
   q.Parameters.AddWithValue("association",associationId);
   q.Parameters.AddWithValue("note",noteId);
   q.Parameters.AddWithValue("entry",body.EntryId);
   q.Parameters.Add("request",NpgsqlDbType.Uuid).Value=body.RequestId.HasValue?(object)body.RequestId.Value:DBNull.Value;
   q.Parameters.AddWithValue("kind",body.EntryKind);
   q.Parameters.AddWithValue("content",body.Content.Trim());
   q.Parameters.AddWithValue("actor",actor.FindFirst("sub")!.Value);
   q.Parameters.AddWithValue("audit",Guid.NewGuid());
   q.Parameters.AddWithValue("event",Guid.NewGuid());
   q.Parameters.AddWithValue("correlation",Guid.NewGuid().ToString("N"));
   if(await q.ExecuteScalarAsync(ct) is not Guid persisted||persisted!=body.EntryId)
    throw new InvalidOperationException("Clarification not persisted");
   await tx.CommitAsync(ct);
   return Results.Ok(new{campaignId,associationId,noteId,entryId=persisted,
     entryKind=body.EntryKind,requestId=body.RequestId,
     verificationStatus="UNVERIFIED",isApproval=false,isExecuted=false,isResolved=false});
  }
  catch(PostgresException ex) when(ex.SqlState is "23503" or "23505" or "23514" or "40001")
   {return Results.Problem(statusCode:409,title:"Clarification scope or immutable identity conflict");}
  catch(PostgresException ex) when(ex.SqlState=="42501"&&ex.MessageText=="Center scope or active membership absent")
   {return Results.Problem(statusCode:403,title:"Live center membership required");}
  catch(PostgresException ex) when(ex.SqlState=="42501")
   {return Results.Problem(statusCode:503,title:"Clarification SQL permission unsafe");}
  catch(PostgresException ex) when(ex.SqlState=="22023")
   {return Results.Problem(statusCode:400,title:"Invalid bounded clarification content");}
  catch(NpgsqlException){return Results.Problem(statusCode:503,title:"Clarification storage unavailable");}
  catch(TimeoutException){return Results.Problem(statusCode:503,title:"Clarification storage unavailable");}
 }
 public static async Task<IResult> List(ClaimsPrincipal actor,IConfiguration config,
  ICenterCampaignReportLinkAuthorization links,ICenterActivityReportReadAuthorization reports,
  string centerId,Guid campaignId,Guid associationId,Guid noteId,
  string? entryKind,Guid? requestId,Guid? beforeEntryId,CancellationToken ct)
 {
  var invalid=Identity(actor,centerId,campaignId,associationId,noteId);if(invalid is not null)return invalid;
  if(beforeEntryId==Guid.Empty||
    !(entryKind=="REQUEST"&&requestId is null||
      entryKind=="RESPONSE"&&requestId.HasValue&&requestId!=Guid.Empty))
   return Results.Problem(statusCode:400,title:"Invalid clarification pagination scope");
  if(!await Granted(actor,centerId,campaignId,"CAMPAIGN_INSPECTION_FOLLOWUP_READ",links,reports,ct))
   return Results.Problem(statusCode:403,title:"Both followup and source read grants required");
  var url=config["CENTER_DRAFT_READ_DATABASE_URL"];
  if(string.IsNullOrWhiteSpace(url))
   return Results.Problem(statusCode:503,title:"Clarifications unavailable");
  try{
   await using var db=new NpgsqlConnection(url);await db.OpenAsync(ct);
   await using var tx=await db.BeginTransactionAsync(ct);
   if(!await RestrictedCampaignFollowupRole.ReaderAsync(db,tx,ct))
    return Results.Problem(statusCode:503,title:"Unsafe clarification reader role");
   await using(var scope=new NpgsqlCommand("SELECT set_config('kanoonyar.center_id',@center,true)",db,tx))
   {scope.Parameters.AddWithValue("center",centerId);await scope.ExecuteNonQueryAsync(ct);}
   await using(var check=new NpgsqlCommand(
    """
    SELECT EXISTS(SELECT 1 FROM campaigns.campaign_report_inspection_notes n
      JOIN centers.memberships m ON m.center_id=n.center_id
      JOIN centers.registry c ON c.center_id=n.center_id
      WHERE n.center_id=@center AND n.campaign_id=@campaign AND n.association_id=@association
        AND n.note_id=@note AND m.subject_id=@actor AND m.is_active AND c.is_active)
    """,db,tx))
   {
    check.Parameters.AddWithValue("center",centerId);
    check.Parameters.AddWithValue("campaign",campaignId);
    check.Parameters.AddWithValue("association",associationId);
    check.Parameters.AddWithValue("note",noteId);
    check.Parameters.AddWithValue("actor",actor.FindFirst("sub")!.Value);
    if(await check.ExecuteScalarAsync(ct) is not true)
     return Results.Problem(statusCode:404,title:"Clarification parent not found in authorized center");
   }
   if(entryKind=="RESPONSE"){
    await using var owner=new NpgsqlCommand(
     """
     SELECT EXISTS(SELECT 1 FROM campaigns.inspection_followup_requests
       WHERE center_id=@center AND campaign_id=@campaign AND association_id=@association
        AND note_id=@note AND request_id=@request)
     """,db,tx);
    owner.Parameters.AddWithValue("center",centerId);
    owner.Parameters.AddWithValue("campaign",campaignId);
    owner.Parameters.AddWithValue("association",associationId);
    owner.Parameters.AddWithValue("note",noteId);
    owner.Parameters.AddWithValue("request",requestId!.Value);
    if(await owner.ExecuteScalarAsync(ct) is not true)
     return Results.Problem(statusCode:404,title:"Clarification request not in source scope");
   }
   var sql=entryKind=="REQUEST"?
    """
    SELECT request_id,question_text,requested_by_subject,requested_at
    FROM campaigns.inspection_followup_requests
    WHERE center_id=@center AND campaign_id=@campaign AND association_id=@association
     AND note_id=@note AND (@before::uuid IS NULL OR request_id<@before::uuid)
    ORDER BY request_id DESC LIMIT 21
    """:
    """
    SELECT response_id,response_text,responded_by_subject,responded_at
    FROM campaigns.inspection_followup_responses
    WHERE center_id=@center AND campaign_id=@campaign AND association_id=@association
     AND note_id=@note AND request_id=@request
     AND (@before::uuid IS NULL OR response_id<@before::uuid)
    ORDER BY response_id DESC LIMIT 21
    """;
   await using var q=new NpgsqlCommand(sql,db,tx);
   q.Parameters.AddWithValue("center",centerId);
   q.Parameters.AddWithValue("campaign",campaignId);
   q.Parameters.AddWithValue("association",associationId);
   q.Parameters.AddWithValue("note",noteId);
   if(entryKind=="RESPONSE")q.Parameters.AddWithValue("request",requestId!.Value);
   q.Parameters.Add("before",NpgsqlDbType.Uuid).Value=beforeEntryId.HasValue?(object)beforeEntryId.Value:DBNull.Value;
   var entries=new List<object>();Guid? last=null;int count=0;
   await using(var reader=await q.ExecuteReaderAsync(ct)){
    while(await reader.ReadAsync(ct)){
     if(count<20){
      last=reader.GetGuid(0);
      entries.Add(new{entryId=reader.GetGuid(0),content=reader.GetString(1),
       actorSubject=reader.GetString(2),recordedAt=reader.GetDateTime(3),
       entryKind,requestId=entryKind=="RESPONSE"?requestId:null,
       verificationStatus="UNVERIFIED",isApproval=false,isExecuted=false,isResolved=false});
     }
     count++;
    }
   }
   await tx.CommitAsync(ct);
   return Results.Ok(new{campaignId,associationId,noteId,entryKind,requestId,entries,
     nextBeforeEntryId=count>20?last:null,verificationStatus="UNVERIFIED",
     isApproval=false,isExecuted=false,isResolved=false});
  }
  catch(NpgsqlException){return Results.Problem(statusCode:503,title:"Clarification history unavailable");}
  catch(TimeoutException){return Results.Problem(statusCode:503,title:"Clarification history unavailable");}
 }
}
