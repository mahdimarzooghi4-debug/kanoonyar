using System.Data;
using System.Security.Claims;
using Kanoonyar.Api.Security;
using Npgsql;
using NpgsqlTypes;

namespace Kanoonyar.Api.Programs;

/// <summary>
/// Read-only projection of source-owned campaign report, human note and clarification history.
/// Timeline is never verification, resolution, execution or a performance KPI.
/// </summary>
public static class CenterCampaignReviewWorkspaceEndpoint
{
 private static readonly HashSet<string> Kinds=["NOTE","REQUEST","RESPONSE"];

 public static async Task<IResult> Get(
  ClaimsPrincipal actor,IConfiguration config,
  ICenterCampaignReportLinkAuthorization campaignGrants,
  ICenterActivityReportReadAuthorization reportGrants,
  string centerId,Guid campaignId,Guid associationId,
  DateTimeOffset? beforeAt,string? beforeKind,Guid? beforeId,CancellationToken ct)
 {
  if(actor.Identity?.IsAuthenticated!=true)
   return Results.Problem(statusCode:401,title:"Authentication required");
  if(string.IsNullOrWhiteSpace(actor.FindFirst("sub")?.Value))
   return Results.Problem(statusCode:403,title:"Verified subject required");
  if(string.IsNullOrWhiteSpace(centerId)||campaignId==Guid.Empty||associationId==Guid.Empty)
   return Results.Problem(statusCode:400,title:"Invalid source association");
  var hasCursor=beforeAt.HasValue||beforeKind is not null||beforeId.HasValue;
  if(hasCursor&&(!beforeAt.HasValue||beforeKind is null||!Kinds.Contains(beforeKind)||
    !beforeId.HasValue||beforeId==Guid.Empty||beforeAt.Value==DateTimeOffset.MinValue||
    beforeAt.Value==DateTimeOffset.MaxValue))
   return Results.Problem(statusCode:400,title:"Invalid timeline cursor");

  try
  {
   foreach(var op in new[]{"CAMPAIGN_SOURCE_REPORT_READ","CAMPAIGN_REPORT_INSPECTION_READ",
     "CAMPAIGN_INSPECTION_FOLLOWUP_READ"})
    if(!await campaignGrants.IsAuthorizedAsync(actor,centerId,campaignId,op,ct))
     return Results.Problem(statusCode:403,title:"All independent review read grants required");
   if(!await reportGrants.IsAuthorizedAsync(actor,centerId,ct))
    return Results.Problem(statusCode:403,title:"Original source report read grant required");
  }
  catch(OperationCanceledException) when(ct.IsCancellationRequested){throw;}
  catch{return Results.Problem(statusCode:403,title:"Review source authority unavailable");}

  var url=config["CENTER_DRAFT_READ_DATABASE_URL"];
  if(string.IsNullOrWhiteSpace(url))
   return Results.Problem(statusCode:503,title:"Review workspace reader unavailable");
  try
  {
   await using var db=new NpgsqlConnection(url);await db.OpenAsync(ct);
   await using var tx=await db.BeginTransactionAsync(IsolationLevel.RepeatableRead,ct);
   if(!await RestrictedCampaignReportLinkRole.ReaderAsync(db,tx,ct)||
      !await RestrictedCampaignInspectionRole.ReaderAsync(db,tx,ct)||
      !await RestrictedCampaignFollowupRole.ReaderAsync(db,tx,ct))
    return Results.Problem(statusCode:503,title:"Unsafe workspace SQL read role");
   await using(var scope=new NpgsqlCommand("SELECT set_config('kanoonyar.center_id',@center,true)",db,tx))
   {scope.Parameters.AddWithValue("center",centerId);await scope.ExecuteNonQueryAsync(ct);}

   await using(var member=new NpgsqlCommand(
    """
    SELECT EXISTS(SELECT 1 FROM centers.memberships m JOIN centers.registry c
      ON m.center_id=c.center_id WHERE m.center_id=@center AND m.subject_id=@actor
      AND m.is_active AND c.is_active)
    """,db,tx))
   {
    member.Parameters.AddWithValue("center",centerId);
    member.Parameters.AddWithValue("actor",actor.FindFirst("sub")!.Value);
    if(await member.ExecuteScalarAsync(ct) is not true)
     return Results.Problem(statusCode:404,title:"Source not found in authorized center");
   }
   object? source;
   await using(var q=new NpgsqlCommand(
    """
    SELECT a.report_id,a.activity_key,a.campaign_revision,a.draft_id,
      a.pinned_plan_revision,r.source_kind,r.source_record_ref,r.reported_summary,
      r.evidence_ref,r.claimed_occurred_at,r.recorded_at,a.linked_at,r.link_id
    FROM campaigns.campaign_report_links a
    JOIN campaigns.center_campaigns c ON c.center_id=a.center_id
      AND c.campaign_id=a.campaign_id AND c.draft_id=a.draft_id
      AND c.pinned_plan_revision=a.pinned_plan_revision
    JOIN campaigns.center_campaign_revisions v ON v.center_id=a.center_id
      AND v.campaign_id=a.campaign_id AND v.revision=a.campaign_revision
    JOIN programs.center_activity_reports r ON r.center_id=a.center_id
      AND r.report_id=a.report_id AND r.draft_id=a.draft_id
    JOIN programs.activity_report_source_bindings b ON b.center_id=r.center_id
      AND b.report_id=r.report_id AND b.source_kind=r.source_kind
      AND b.source_record_ref=r.source_record_ref
    JOIN programs.center_plan_issuance_links l ON l.center_id=r.center_id
      AND l.link_id=r.link_id AND l.draft_id=r.draft_id
      AND l.pinned_revision=a.pinned_plan_revision
    WHERE a.center_id=@center AND a.campaign_id=@campaign AND a.association_id=@association
     AND EXISTS(SELECT 1 FROM jsonb_array_elements(v.planned_activities) activity
       WHERE activity->>'activityKey'=a.activity_key)
    """,db,tx))
   {
    q.Parameters.AddWithValue("center",centerId);
    q.Parameters.AddWithValue("campaign",campaignId);
    q.Parameters.AddWithValue("association",associationId);
    await using var reader=await q.ExecuteReaderAsync(ct);
    if(!await reader.ReadAsync(ct))
     return Results.Problem(statusCode:404,title:"Source not found in authorized center");
    source=new{
     reportId=reader.GetGuid(0),activityKey=reader.GetString(1),
     campaignRevision=reader.GetInt64(2),draftId=reader.GetGuid(3),
     pinnedPlanRevision=reader.GetInt64(4),sourceKind=reader.GetString(5),
     sourceRecordRef=reader.GetString(6),reportedSummary=reader.GetString(7),
     evidenceRef=reader.GetString(8),
     claimedOccurredAt=reader.IsDBNull(9)?(DateTime?)null:reader.GetDateTime(9),
     reportRecordedAt=reader.GetDateTime(10),linkedAt=reader.GetDateTime(11),
     linkId=reader.GetGuid(12),
     verificationStatus="UNVERIFIED",isApproval=false,isExecuted=false,isResolved=false
    };
   }
   await using var events=new NpgsqlCommand(
    """
    WITH history AS(
      SELECT 'NOTE'::text AS kind,n.note_id AS entry_id,n.note_id AS note_id,
       NULL::uuid AS request_id,n.note_text AS content,n.noted_by_subject AS actor,n.noted_at AS at
      FROM campaigns.campaign_report_inspection_notes n
      WHERE n.center_id=@center AND n.campaign_id=@campaign AND n.association_id=@association
      UNION ALL
      SELECT 'REQUEST'::text,q.request_id,q.note_id,q.request_id,
       q.question_text,q.requested_by_subject,q.requested_at
      FROM campaigns.inspection_followup_requests q
      JOIN campaigns.campaign_report_inspection_notes n ON n.center_id=q.center_id
        AND n.note_id=q.note_id AND n.campaign_id=q.campaign_id
        AND n.association_id=q.association_id
      WHERE q.center_id=@center AND q.campaign_id=@campaign AND q.association_id=@association
      UNION ALL
      SELECT 'RESPONSE'::text,a.response_id,a.note_id,a.request_id,
       a.response_text,a.responded_by_subject,a.responded_at
      FROM campaigns.inspection_followup_responses a
      JOIN campaigns.inspection_followup_requests q ON q.center_id=a.center_id
        AND q.request_id=a.request_id AND q.campaign_id=a.campaign_id
        AND q.association_id=a.association_id AND q.note_id=a.note_id
      WHERE a.center_id=@center AND a.campaign_id=@campaign AND a.association_id=@association
    )
    SELECT kind,entry_id,note_id,request_id,content,actor,at FROM history
    WHERE @before_at::timestamptz IS NULL
     OR (at,kind,entry_id)<(@before_at::timestamptz,@before_kind::text,@before_id::uuid)
    ORDER BY at DESC,kind DESC,entry_id DESC LIMIT 21
    """,db,tx);
   events.Parameters.AddWithValue("center",centerId);
   events.Parameters.AddWithValue("campaign",campaignId);
   events.Parameters.AddWithValue("association",associationId);
   events.Parameters.Add("before_at",NpgsqlDbType.TimestampTz).Value=
     beforeAt.HasValue?(object)beforeAt.Value.UtcDateTime:DBNull.Value;
   events.Parameters.Add("before_kind",NpgsqlDbType.Text).Value=
     beforeKind is not null?(object)beforeKind:DBNull.Value;
   events.Parameters.Add("before_id",NpgsqlDbType.Uuid).Value=
     beforeId.HasValue?(object)beforeId.Value:DBNull.Value;
   var rows=new List<object>();int count=0;DateTime? nextAt=null;string? nextKind=null;Guid? nextId=null;
   await using(var reader=await events.ExecuteReaderAsync(ct))
   {
    while(await reader.ReadAsync(ct))
    {
     if(count<20)
     {
      var kind=reader.GetString(0);
      var at=reader.GetDateTime(6);
      nextAt=at;nextKind=kind;nextId=reader.GetGuid(1);
      rows.Add(new{entryKind=kind,entryId=reader.GetGuid(1),
       noteId=reader.GetGuid(2),
       requestId=reader.IsDBNull(3)?(Guid?)null:reader.GetGuid(3),
       content=reader.GetString(4),actorSubject=reader.GetString(5),recordedAt=at,
       verificationStatus="UNVERIFIED",isApproval=false,isExecuted=false,isResolved=false});
     }
     count++;
    }
   }
   await tx.CommitAsync(ct);
   return Results.Ok(new{campaignId,associationId,source,entries=rows,
    nextBeforeAt=count>20?nextAt:null,nextBeforeKind=count>20?nextKind:null,
    nextBeforeId=count>20?nextId:null,verificationStatus="UNVERIFIED",
    isApproval=false,isExecuted=false,isResolved=false,
    isComplete=count<=20});
  }
  catch(NpgsqlException){return Results.Problem(statusCode:503,title:"Review source workspace unavailable");}
  catch(TimeoutException){return Results.Problem(statusCode:503,title:"Review source workspace unavailable");}
 }
}
