using System.Security.Claims;
using Kanoonyar.Api.Security;
using Npgsql;
using NpgsqlTypes;

namespace Kanoonyar.Api.Programs;

public sealed record LinkCampaignSourceReportRequest(
 Guid AssociationId,long CampaignRevision,string ActivityKey,Guid ReportId);

public interface ICenterCampaignReportLinkAuthorization
{
 ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal user,string centerId,Guid campaignId,
  string operation,CancellationToken ct);
}
public sealed class UnapprovedCenterCampaignReportLinkAuthorization:ICenterCampaignReportLinkAuthorization
{
 public ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal user,string centerId,Guid campaignId,
  string operation,CancellationToken ct)=>ValueTask.FromResult(false);
}
public static class CenterCampaignReportLinkEndpoint
{
 private static readonly System.Text.RegularExpressions.Regex Key=
   new("^[a-z][a-z0-9_]{0,31}$",System.Text.RegularExpressions.RegexOptions.Compiled|
   System.Text.RegularExpressions.RegexOptions.CultureInvariant);
 private static IResult? Identity(ClaimsPrincipal actor,string center,Guid campaign)
 {
  if(actor.Identity?.IsAuthenticated!=true)return Results.Problem(statusCode:401,title:"Authentication required");
  if(string.IsNullOrWhiteSpace(actor.FindFirst("sub")?.Value))
   return Results.Problem(statusCode:403,title:"Verified subject required");
  if(string.IsNullOrWhiteSpace(center)||campaign==Guid.Empty)
   return Results.Problem(statusCode:400,title:"Invalid campaign identity");
  return null;
 }
 private static async Task<bool> Granted(ClaimsPrincipal user,string center,Guid campaign,
  string operation,ICenterCampaignReportLinkAuthorization links,
  ICenterActivityReportReadAuthorization reports,CancellationToken ct)
 {
  try
  {
   return await links.IsAuthorizedAsync(user,center,campaign,operation,ct)
      && await reports.IsAuthorizedAsync(user,center,ct);
  }
  catch(OperationCanceledException) when(ct.IsCancellationRequested){throw;}
  catch{return false;}
 }
 public static async Task<IResult> Link(
  ClaimsPrincipal actor,IConfiguration config,ICenterCampaignReportLinkAuthorization links,
  ICenterActivityReportReadAuthorization reports,string centerId,Guid campaignId,
  LinkCampaignSourceReportRequest? request,CancellationToken ct)
 {
  var invalid=Identity(actor,centerId,campaignId);if(invalid is not null)return invalid;
  if(request is null||request.AssociationId==Guid.Empty||request.ReportId==Guid.Empty||
    request.CampaignRevision<1||request.ActivityKey is null||!Key.IsMatch(request.ActivityKey))
   return Results.Problem(statusCode:400,title:"Invalid source association");
  if(!await Granted(actor,centerId,campaignId,"CAMPAIGN_SOURCE_REPORT_LINK",links,reports,ct))
   return Results.Problem(statusCode:403,title:"Independent report and campaign linking grants required");
  var url=config["CENTER_CAMPAIGN_REPORT_LINK_DATABASE_URL"];
  if(string.IsNullOrWhiteSpace(url))return Results.Problem(statusCode:503,title:"Report association unavailable");
  try
  {
   await using var db=new NpgsqlConnection(url);await db.OpenAsync(ct);
   await using var tx=await db.BeginTransactionAsync(ct);
   if(!await RestrictedCampaignReportLinkRole.WriterAsync(db,tx,ct))
    return Results.Problem(statusCode:503,title:"Unsafe association SQL role");
   await using(var scope=new NpgsqlCommand("SELECT set_config('kanoonyar.center_id',@center,true)",db,tx))
   {scope.Parameters.AddWithValue("center",centerId);await scope.ExecuteNonQueryAsync(ct);}
   await using var cmd=new NpgsqlCommand(
    """
    SELECT campaigns.link_unverified_source_report(@center,@assoc,@campaign,@rev,
      @activity,@report,@actor,@audit,@event,@correlation)
    """,db,tx);
   cmd.Parameters.AddWithValue("center",centerId);
   cmd.Parameters.AddWithValue("assoc",request.AssociationId);
   cmd.Parameters.AddWithValue("campaign",campaignId);
   cmd.Parameters.AddWithValue("rev",request.CampaignRevision);
   cmd.Parameters.AddWithValue("activity",request.ActivityKey);
   cmd.Parameters.AddWithValue("report",request.ReportId);
   cmd.Parameters.AddWithValue("actor",actor.FindFirst("sub")!.Value);
   cmd.Parameters.AddWithValue("audit",Guid.NewGuid());
   cmd.Parameters.AddWithValue("event",Guid.NewGuid());
   cmd.Parameters.AddWithValue("correlation",Guid.NewGuid().ToString("N"));
   if(await cmd.ExecuteScalarAsync(ct) is not Guid ack||ack!=request.AssociationId)
    throw new InvalidOperationException("Association not persisted");
   await tx.CommitAsync(ct);
   return Results.Ok(new{associationId=ack,campaignId,request.CampaignRevision,
    request.ActivityKey,request.ReportId,verificationStatus="UNVERIFIED",
    isExecuted=false,sourceReused=true});
  }
  catch(PostgresException ex) when(ex.SqlState is "23503" or "23505" or "23514" or "40001")
  {return Results.Problem(statusCode:409,title:"Source report or exact campaign revision conflict");}
  catch(PostgresException ex) when(ex.SqlState=="42501"&&ex.MessageText=="Center scope or active membership absent")
  {return Results.Problem(statusCode:403,title:"Live center membership required");}
  catch(PostgresException ex) when(ex.SqlState=="42501")
  {return Results.Problem(statusCode:503,title:"Association role insufficient");}
  catch(PostgresException ex) when(ex.SqlState=="22023")
  {return Results.Problem(statusCode:400,title:"Invalid source association");}
  catch(NpgsqlException){return Results.Problem(statusCode:503,title:"Source report association unavailable");}
  catch(TimeoutException){return Results.Problem(statusCode:503,title:"Source report association unavailable");}
 }
 public static async Task<IResult> List(
  ClaimsPrincipal actor,IConfiguration config,ICenterCampaignReportLinkAuthorization links,
  ICenterActivityReportReadAuthorization reports,string centerId,Guid campaignId,
  Guid? beforeAssociationId,CancellationToken ct)
 {
  var invalid=Identity(actor,centerId,campaignId);if(invalid is not null)return invalid;
  if(beforeAssociationId==Guid.Empty)return Results.Problem(statusCode:400,title:"Invalid association cursor");
  if(!await Granted(actor,centerId,campaignId,"CAMPAIGN_SOURCE_REPORT_READ",links,reports,ct))
   return Results.Problem(statusCode:403,title:"Independent report content read grants required");
  var url=config["CENTER_DRAFT_READ_DATABASE_URL"];
  if(string.IsNullOrWhiteSpace(url))return Results.Problem(statusCode:503,title:"Source association read unavailable");
  try
  {
   await using var db=new NpgsqlConnection(url);await db.OpenAsync(ct);
   await using var tx=await db.BeginTransactionAsync(ct);
   if(!await RestrictedCampaignReportLinkRole.ReaderAsync(db,tx,ct))
    return Results.Problem(statusCode:503,title:"Unsafe source association read role");
   await using(var scope=new NpgsqlCommand("SELECT set_config('kanoonyar.center_id',@center,true)",db,tx))
   {scope.Parameters.AddWithValue("center",centerId);await scope.ExecuteNonQueryAsync(ct);}
   await using(var check=new NpgsqlCommand(
     """
     SELECT EXISTS(SELECT 1 FROM campaigns.center_campaigns c
       JOIN centers.memberships m ON m.center_id=c.center_id
       JOIN centers.registry reg ON reg.center_id=c.center_id
       WHERE c.center_id=@center AND c.campaign_id=@campaign
        AND m.subject_id=@actor AND m.is_active AND reg.is_active)
     """,db,tx))
   {
    check.Parameters.AddWithValue("center",centerId);
    check.Parameters.AddWithValue("campaign",campaignId);
    check.Parameters.AddWithValue("actor",actor.FindFirst("sub")!.Value);
    if(await check.ExecuteScalarAsync(ct) is not true)
      return Results.Problem(statusCode:404,title:"Campaign not in authorized center");
   }
   await using var q=new NpgsqlCommand(
    """
    SELECT a.association_id,a.campaign_revision,a.activity_key,a.report_id,
      r.source_kind,r.source_record_ref,r.reported_summary,r.evidence_ref,
      r.claimed_occurred_at,r.recorded_at,a.linked_at,r.link_id
    FROM campaigns.campaign_report_links a
    JOIN programs.center_activity_reports r
      ON r.center_id=a.center_id AND r.report_id=a.report_id AND r.draft_id=a.draft_id
    JOIN programs.activity_report_source_bindings b
      ON b.center_id=r.center_id AND b.report_id=r.report_id
       AND b.source_kind=r.source_kind AND b.source_record_ref=r.source_record_ref
    JOIN programs.center_plan_issuance_links l
      ON l.center_id=r.center_id AND l.link_id=r.link_id AND l.draft_id=r.draft_id
       AND l.pinned_revision=a.pinned_plan_revision
    WHERE a.center_id=@center AND a.campaign_id=@campaign
      AND (@before::uuid IS NULL OR a.association_id<@before::uuid)
    ORDER BY a.association_id DESC LIMIT 21
    """,db,tx);
   q.Parameters.AddWithValue("center",centerId);
   q.Parameters.AddWithValue("campaign",campaignId);
   q.Parameters.Add("before",NpgsqlDbType.Uuid).Value=
    beforeAssociationId.HasValue?(object)beforeAssociationId.Value:DBNull.Value;
   var rows=new List<object>();Guid? cursor=null;int count=0;
   await using(var reader=await q.ExecuteReaderAsync(ct))
   {
    while(await reader.ReadAsync(ct))
    {
     if(count<20)
     {
      cursor=reader.GetGuid(0);
      rows.Add(new{
       associationId=reader.GetGuid(0),campaignRevision=reader.GetInt64(1),
       activityKey=reader.GetString(2),reportId=reader.GetGuid(3),
       sourceKind=reader.GetString(4),sourceRecordRef=reader.GetString(5),
       reportedSummary=reader.GetString(6),evidenceRef=reader.GetString(7),
       claimedOccurredAt=reader.IsDBNull(8)?(DateTime?)null:reader.GetDateTime(8),
       reportRecordedAt=reader.GetDateTime(9),linkedAt=reader.GetDateTime(10),
       linkId=reader.GetGuid(11),verificationStatus="UNVERIFIED",isExecuted=false
      });
     }
     count++;
    }
   }
   await tx.CommitAsync(ct);
   return Results.Ok(new{campaignId,associations=rows,
      nextBeforeAssociationId=count>20?cursor:null,
      verificationStatus="UNVERIFIED",isExecuted=false});
  }
  catch(NpgsqlException){return Results.Problem(statusCode:503,title:"Source association read unavailable");}
  catch(TimeoutException){return Results.Problem(statusCode:503,title:"Source association read unavailable");}
 }
}
