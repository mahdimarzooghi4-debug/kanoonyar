using System.Data;
using System.Security.Claims;
using Kanoonyar.Api.Security;
using Npgsql;
using NpgsqlTypes;

namespace Kanoonyar.Api.Programs;

/// <summary>A source-first, unverified, read-only register within exactly one private campaign.</summary>
public static class CenterCampaignEvidenceRegisterEndpoint
{
 public static async Task<IResult> Get(
  ClaimsPrincipal actor,IConfiguration config,
  ICenterCampaignReportLinkAuthorization grants,
  ICenterActivityReportReadAuthorization sourceGrant,
  string centerId,Guid campaignId,Guid? beforeAssociationId,string? sourceKind,
  string? activityKey,CancellationToken ct)
 {
  if(actor.Identity?.IsAuthenticated!=true)
   return Results.Problem(statusCode:401,title:"Authentication required");
  if(string.IsNullOrWhiteSpace(actor.FindFirst("sub")?.Value))
   return Results.Problem(statusCode:403,title:"Verified OIDC subject required");
  if(string.IsNullOrWhiteSpace(centerId)||campaignId==Guid.Empty||
    beforeAssociationId==Guid.Empty||
    (sourceKind is not null && sourceKind!="CLASS_SESSION" && sourceKind!="CENTER_EVENT")||
    (activityKey is not null && !System.Text.RegularExpressions.Regex.IsMatch(
      activityKey,"^[a-z][a-z0-9_]{0,31}$",
      System.Text.RegularExpressions.RegexOptions.CultureInvariant)))
   return Results.Problem(statusCode:400,title:"Invalid register filter or cursor");
  try
  {
   foreach(var op in new[]{"CAMPAIGN_EVIDENCE_REGISTER_READ","CAMPAIGN_SOURCE_REPORT_READ",
     "CAMPAIGN_REPORT_INSPECTION_READ","CAMPAIGN_INSPECTION_FOLLOWUP_READ"})
    if(!await grants.IsAuthorizedAsync(actor,centerId,campaignId,op,ct))
     return Results.Problem(statusCode:403,title:"Independent campaign register rights required");
   if(!await sourceGrant.IsAuthorizedAsync(actor,centerId,ct))
    return Results.Problem(statusCode:403,title:"Original source report read rights required");
  }
  catch(OperationCanceledException) when(ct.IsCancellationRequested){throw;}
  catch{return Results.Problem(statusCode:403,title:"Source access not authorized");}

  var url=config["CENTER_DRAFT_READ_DATABASE_URL"];
  if(string.IsNullOrWhiteSpace(url))
   return Results.Problem(statusCode:503,title:"Register read unavailable");
  try
  {
   await using var db=new NpgsqlConnection(url);
   await db.OpenAsync(ct);
   await using var tx=await db.BeginTransactionAsync(IsolationLevel.RepeatableRead,ct);
   if(!await RestrictedCampaignReportLinkRole.ReaderAsync(db,tx,ct)||
      !await RestrictedCampaignInspectionRole.ReaderAsync(db,tx,ct)||
      !await RestrictedCampaignFollowupRole.ReaderAsync(db,tx,ct))
    return Results.Problem(statusCode:503,title:"Unsafe register SQL role");
   await using(var scope=new NpgsqlCommand("SELECT set_config('kanoonyar.center_id',@center,true)",db,tx))
   {
    scope.Parameters.AddWithValue("center",centerId);
    await scope.ExecuteNonQueryAsync(ct);
   }
   await using(var membership=new NpgsqlCommand(
    """
    SELECT EXISTS(SELECT 1 FROM centers.memberships m
     JOIN centers.registry c ON c.center_id=m.center_id
     JOIN campaigns.center_campaigns campaign ON campaign.center_id=m.center_id
     WHERE m.center_id=@center AND campaign.campaign_id=@campaign
      AND m.subject_id=@actor AND m.is_active AND c.is_active)
    """,db,tx))
   {
    membership.Parameters.AddWithValue("center",centerId);
    membership.Parameters.AddWithValue("campaign",campaignId);
    membership.Parameters.AddWithValue("actor",actor.FindFirst("sub")!.Value);
    if(await membership.ExecuteScalarAsync(ct) is not true)
     return Results.Problem(statusCode:404,title:"Private campaign not in authorized scope");
   }
   await using var q=new NpgsqlCommand(
    """
    SELECT a.association_id,a.report_id,a.campaign_revision,a.activity_key,
     a.draft_id,a.pinned_plan_revision,r.source_kind,r.source_record_ref,
     r.reported_summary,r.evidence_ref,r.link_id,
     r.recorded_at,a.linked_at,latest.entry_kind,latest.entry_id,
     latest.entry_at
    FROM campaigns.campaign_report_links a
    JOIN campaigns.center_campaigns c ON c.center_id=a.center_id
      AND c.campaign_id=a.campaign_id AND c.draft_id=a.draft_id
      AND c.pinned_plan_revision=a.pinned_plan_revision
    JOIN campaigns.center_campaign_revisions version ON version.center_id=a.center_id
      AND version.campaign_id=a.campaign_id AND version.revision=a.campaign_revision
    JOIN programs.center_activity_reports r ON r.center_id=a.center_id
      AND r.report_id=a.report_id AND r.draft_id=a.draft_id
    JOIN programs.activity_report_source_bindings b ON b.center_id=r.center_id
      AND b.report_id=r.report_id AND b.source_kind=r.source_kind
      AND b.source_record_ref=r.source_record_ref
    JOIN programs.center_plan_issuance_links issuance ON issuance.center_id=r.center_id
      AND issuance.link_id=r.link_id AND issuance.draft_id=r.draft_id
      AND issuance.pinned_revision=a.pinned_plan_revision
    LEFT JOIN LATERAL (
      SELECT entry_kind,entry_id,entry_at FROM (
        SELECT 'NOTE'::text AS entry_kind,n.note_id AS entry_id,n.noted_at AS entry_at
        FROM campaigns.campaign_report_inspection_notes n
        WHERE n.center_id=a.center_id AND n.campaign_id=a.campaign_id
          AND n.association_id=a.association_id
        UNION ALL
        SELECT 'REQUEST'::text,req.request_id,req.requested_at
        FROM campaigns.inspection_followup_requests req
        JOIN campaigns.campaign_report_inspection_notes n ON
          n.center_id=req.center_id AND n.note_id=req.note_id
          AND n.campaign_id=req.campaign_id AND n.association_id=req.association_id
        WHERE req.center_id=a.center_id AND req.campaign_id=a.campaign_id
          AND req.association_id=a.association_id
        UNION ALL
        SELECT 'RESPONSE'::text,res.response_id,res.responded_at
        FROM campaigns.inspection_followup_responses res
        JOIN campaigns.inspection_followup_requests req ON
          req.center_id=res.center_id AND req.request_id=res.request_id
          AND req.campaign_id=res.campaign_id AND req.association_id=res.association_id
          AND req.note_id=res.note_id
        WHERE res.center_id=a.center_id AND res.campaign_id=a.campaign_id
          AND res.association_id=a.association_id
      ) original_events ORDER BY entry_at DESC,entry_kind DESC,entry_id DESC LIMIT 1
    ) latest ON true
    WHERE a.center_id=@center AND a.campaign_id=@campaign
      AND (@before::uuid IS NULL OR a.association_id<@before::uuid)
      AND (@source::text IS NULL OR r.source_kind=@source::text)
      AND (@activity::text IS NULL OR a.activity_key=@activity::text)
      AND EXISTS(SELECT 1 FROM jsonb_array_elements(version.planned_activities) planned
       WHERE planned->>'activityKey'=a.activity_key)
    ORDER BY a.association_id DESC LIMIT 21
    """,db,tx);
   q.Parameters.AddWithValue("center",centerId);
   q.Parameters.AddWithValue("campaign",campaignId);
   q.Parameters.Add("before",NpgsqlDbType.Uuid).Value=beforeAssociationId.HasValue?
    (object)beforeAssociationId.Value:DBNull.Value;
   q.Parameters.Add("source",NpgsqlDbType.Text).Value=sourceKind is not null?
    (object)sourceKind:DBNull.Value;
   q.Parameters.Add("activity",NpgsqlDbType.Text).Value=activityKey is not null?
    (object)activityKey:DBNull.Value;
   var entries=new List<object>();
   Guid? last=null;int total=0;
   await using(var reader=await q.ExecuteReaderAsync(ct))
   {
    while(await reader.ReadAsync(ct))
    {
     if(total<20)
     {
      last=reader.GetGuid(0);
      entries.Add(new{
       associationId=reader.GetGuid(0),reportId=reader.GetGuid(1),
       campaignRevision=reader.GetInt64(2),activityKey=reader.GetString(3),
       draftId=reader.GetGuid(4),pinnedPlanRevision=reader.GetInt64(5),
       sourceKind=reader.GetString(6),sourceRecordRef=reader.GetString(7),
       reportedSummary=reader.GetString(8),evidenceRef=reader.GetString(9),
       linkId=reader.GetGuid(10),reportRecordedAt=reader.GetDateTime(11),
       linkedAt=reader.GetDateTime(12),
       latestEntryKind=reader.IsDBNull(13)?(string?)null:reader.GetString(13),
       latestEntryId=reader.IsDBNull(14)?(Guid?)null:reader.GetGuid(14),
       latestEntryAt=reader.IsDBNull(15)?(DateTime?)null:reader.GetDateTime(15),
       verificationStatus="UNVERIFIED",isApproval=false,isExecuted=false,isResolved=false
      });
     }
     total++;
    }
   }
   await tx.CommitAsync(ct);
   return Results.Ok(new{campaignId,sourceKind,activityKey,
    records=entries,nextBeforeAssociationId=total>20?last:null,
    isPageEnd=total<=20,verificationStatus="UNVERIFIED",
    isApproval=false,isExecuted=false,isResolved=false});
  }
  catch(NpgsqlException){return Results.Problem(statusCode:503,title:"Register source read unavailable");}
  catch(TimeoutException){return Results.Problem(statusCode:503,title:"Register source read unavailable");}
 }
}
