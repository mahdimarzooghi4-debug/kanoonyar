using System.Security.Claims;
using Kanoonyar.Api.Security;
using Npgsql;
using NpgsqlTypes;

namespace Kanoonyar.Api.Programs;
public sealed record AppendCampaignInspectionNoteRequest(Guid NoteId,string NoteText);

/// <summary>Human inspection comments only; never a review decision or source verification.</summary>
public static class CenterCampaignInspectionNotesEndpoint
{
 private static IResult? Identity(ClaimsPrincipal actor,string center,Guid campaign,Guid association)
 {
  if(actor.Identity?.IsAuthenticated!=true)return Results.Problem(statusCode:401,title:"Authentication required");
  if(string.IsNullOrWhiteSpace(actor.FindFirst("sub")?.Value))
   return Results.Problem(statusCode:403,title:"Verified OIDC subject required");
  if(string.IsNullOrWhiteSpace(center)||campaign==Guid.Empty||association==Guid.Empty)
   return Results.Problem(statusCode:400,title:"Invalid inspection scope");
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
  string centerId,Guid campaignId,Guid associationId,
  AppendCampaignInspectionNoteRequest? request,CancellationToken ct)
 {
  var invalid=Identity(actor,centerId,campaignId,associationId);if(invalid is not null)return invalid;
  if(request is null||request.NoteId==Guid.Empty||request.NoteText is null||
     request.NoteText.Trim().Length is <1 or >1000)
    return Results.Problem(statusCode:400,title:"Invalid bounded inspection note");
  if(!await Granted(actor,centerId,campaignId,"CAMPAIGN_REPORT_INSPECTION_APPEND",links,reports,ct))
    return Results.Problem(statusCode:403,title:"Independent inspection and report read grants required");
  var url=config["CENTER_CAMPAIGN_INSPECTION_WRITE_DATABASE_URL"];
  if(string.IsNullOrWhiteSpace(url))
    return Results.Problem(statusCode:503,title:"Source inspection note unavailable");
  try{
   await using var db=new NpgsqlConnection(url);await db.OpenAsync(ct);
   await using var tx=await db.BeginTransactionAsync(ct);
   if(!await RestrictedCampaignInspectionRole.WriterAsync(db,tx,ct))
    return Results.Problem(statusCode:503,title:"Unsafe inspection SQL writer");
   await using(var scope=new NpgsqlCommand("SELECT set_config('kanoonyar.center_id',@center,true)",db,tx))
   {scope.Parameters.AddWithValue("center",centerId);await scope.ExecuteNonQueryAsync(ct);}
   await using var cmd=new NpgsqlCommand(
    """
    SELECT campaigns.record_campaign_report_inspection(
      @center,@campaign,@association,@note,@text,@actor,@audit,@event,@correlation)
    """,db,tx);
   cmd.Parameters.AddWithValue("center",centerId);
   cmd.Parameters.AddWithValue("campaign",campaignId);
   cmd.Parameters.AddWithValue("association",associationId);
   cmd.Parameters.AddWithValue("note",request.NoteId);
   cmd.Parameters.AddWithValue("text",request.NoteText.Trim());
   cmd.Parameters.AddWithValue("actor",actor.FindFirst("sub")!.Value);
   cmd.Parameters.AddWithValue("audit",Guid.NewGuid());
   cmd.Parameters.AddWithValue("event",Guid.NewGuid());
   cmd.Parameters.AddWithValue("correlation",Guid.NewGuid().ToString("N"));
   if(await cmd.ExecuteScalarAsync(ct) is not Guid result||result!=request.NoteId)
    throw new InvalidOperationException("Inspection not persisted");
   await tx.CommitAsync(ct);
   return Results.Ok(new{campaignId,associationId,noteId=result,
     verificationStatus="UNVERIFIED",isApproval=false,isExecuted=false});
  }
  catch(PostgresException ex) when(ex.SqlState is "23503" or "23505" or "23514")
  {return Results.Problem(statusCode:409,title:"Unknown or duplicated source inspection");}
  catch(PostgresException ex) when(ex.SqlState=="42501"&&ex.MessageText=="Center scope or active membership absent")
  {return Results.Problem(statusCode:403,title:"Live center membership required");}
  catch(PostgresException ex) when(ex.SqlState=="42501")
  {return Results.Problem(statusCode:503,title:"Inspection SQL permission unavailable");}
  catch(PostgresException ex) when(ex.SqlState=="22023")
  {return Results.Problem(statusCode:400,title:"Invalid source inspection text");}
  catch(NpgsqlException){return Results.Problem(statusCode:503,title:"Inspection note storage unavailable");}
  catch(TimeoutException){return Results.Problem(statusCode:503,title:"Inspection note storage unavailable");}
 }
 public static async Task<IResult> List(ClaimsPrincipal actor,IConfiguration config,
  ICenterCampaignReportLinkAuthorization links,ICenterActivityReportReadAuthorization reports,
  string centerId,Guid campaignId,Guid associationId,Guid? beforeNoteId,CancellationToken ct)
 {
  var invalid=Identity(actor,centerId,campaignId,associationId);if(invalid is not null)return invalid;
  if(beforeNoteId==Guid.Empty)return Results.Problem(statusCode:400,title:"Invalid inspection cursor");
  if(!await Granted(actor,centerId,campaignId,"CAMPAIGN_REPORT_INSPECTION_READ",links,reports,ct))
   return Results.Problem(statusCode:403,title:"Independent inspection and report read grants required");
  var url=config["CENTER_DRAFT_READ_DATABASE_URL"];
  if(string.IsNullOrWhiteSpace(url))
   return Results.Problem(statusCode:503,title:"Inspection notes unavailable");
  try{
   await using var db=new NpgsqlConnection(url);await db.OpenAsync(ct);
   await using var tx=await db.BeginTransactionAsync(ct);
   if(!await RestrictedCampaignInspectionRole.ReaderAsync(db,tx,ct))
    return Results.Problem(statusCode:503,title:"Unsafe inspection reader role");
   await using(var scope=new NpgsqlCommand("SELECT set_config('kanoonyar.center_id',@center,true)",db,tx))
   {scope.Parameters.AddWithValue("center",centerId);await scope.ExecuteNonQueryAsync(ct);}
   await using(var check=new NpgsqlCommand(
    """
    SELECT EXISTS(
      SELECT 1 FROM campaigns.campaign_report_links l
      JOIN centers.memberships m ON m.center_id=l.center_id
      JOIN centers.registry c ON c.center_id=l.center_id
      WHERE l.center_id=@center AND l.campaign_id=@campaign
      AND l.association_id=@association AND m.subject_id=@actor
      AND m.is_active AND c.is_active)
    """,db,tx)){
    check.Parameters.AddWithValue("center",centerId);
    check.Parameters.AddWithValue("campaign",campaignId);
    check.Parameters.AddWithValue("association",associationId);
    check.Parameters.AddWithValue("actor",actor.FindFirst("sub")!.Value);
    if(await check.ExecuteScalarAsync(ct) is not true)
     return Results.Problem(statusCode:404,title:"Source association not found in authorized center");
   }
   await using var q=new NpgsqlCommand(
    """
    SELECT note_id,note_text,noted_by_subject,noted_at
    FROM campaigns.campaign_report_inspection_notes
    WHERE center_id=@center AND campaign_id=@campaign AND association_id=@association
      AND (@before::uuid IS NULL OR note_id<@before::uuid)
    ORDER BY note_id DESC LIMIT 21
    """,db,tx);
   q.Parameters.AddWithValue("center",centerId);
   q.Parameters.AddWithValue("campaign",campaignId);
   q.Parameters.AddWithValue("association",associationId);
   q.Parameters.Add("before",NpgsqlDbType.Uuid).Value=beforeNoteId.HasValue?(object)beforeNoteId.Value:DBNull.Value;
   var notes=new List<object>();Guid? next=null;int count=0;
   await using(var reader=await q.ExecuteReaderAsync(ct)){
    while(await reader.ReadAsync(ct)){
     if(count<20){
      next=reader.GetGuid(0);
      notes.Add(new{noteId=reader.GetGuid(0),noteText=reader.GetString(1),
        notedBySubject=reader.GetString(2),notedAt=reader.GetDateTime(3),
        verificationStatus="UNVERIFIED",isApproval=false,isExecuted=false});
     }
     count++;
    }
   }
   await tx.CommitAsync(ct);
   return Results.Ok(new{campaignId,associationId,notes,nextBeforeNoteId=count>20?next:null,
     verificationStatus="UNVERIFIED",isApproval=false,isExecuted=false});
  }
  catch(NpgsqlException){return Results.Problem(statusCode:503,title:"Inspection notes unavailable");}
  catch(TimeoutException){return Results.Problem(statusCode:503,title:"Inspection notes unavailable");}
 }
}
