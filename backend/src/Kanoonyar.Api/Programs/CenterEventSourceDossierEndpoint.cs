using System.Data;
using System.Security.Claims;
using Kanoonyar.Api.Security;
using Npgsql;
using NpgsqlTypes;

namespace Kanoonyar.Api.Programs;

/// <summary>One versioned private event and source-owned UNVERIFIED reports, never an outcome.</summary>
public static class CenterEventSourceDossierEndpoint
{
 public static async Task<IResult> Get(ClaimsPrincipal actor,IConfiguration config,
  ICenterEventPlanAuthorization eventGrant,ICenterActivityReportReadAuthorization reportGrant,
  string centerId,Guid eventId,long? beforeSequence,CancellationToken ct)
 {
  if(actor.Identity?.IsAuthenticated!=true)
   return Results.Problem(statusCode:401,title:"Authentication required");
  var subject=actor.FindFirst("sub")?.Value;
  if(string.IsNullOrWhiteSpace(subject))
   return Results.Problem(statusCode:403,title:"Verified OIDC subject required");
  if(string.IsNullOrWhiteSpace(centerId)||eventId==Guid.Empty||
   (beforeSequence.HasValue&&beforeSequence.Value<=0))
   return Results.Problem(statusCode:400,title:"Invalid event dossier request");
  try
  {
   if(!await eventGrant.IsAuthorizedAsync(actor,centerId,eventId,"EVENT_SOURCE_DOSSIER_READ",ct)||
      !await reportGrant.IsAuthorizedAsync(actor,centerId,ct))
    return Results.Problem(statusCode:403,title:"Independent event and original report read grants required");
  }
  catch(OperationCanceledException) when(ct.IsCancellationRequested){throw;}
  catch{return Results.Problem(statusCode:403,title:"Event evidence read not authorized");}

  var url=config["CENTER_DRAFT_READ_DATABASE_URL"];
  if(string.IsNullOrWhiteSpace(url))
   return Results.Problem(statusCode:503,title:"Event evidence storage unavailable");
  try
  {
   await using var db=new NpgsqlConnection(url);await db.OpenAsync(ct);
   await using var tx=await db.BeginTransactionAsync(IsolationLevel.RepeatableRead,ct);
   if(!await RestrictedCenterEventPlanRole.ReaderAsync(db,tx,ct))
    return Results.Problem(statusCode:503,title:"Unsafe event dossier reader");
   await using(var capabilities=new NpgsqlCommand("""
    SELECT has_table_privilege(current_user,'programs.center_activity_reports'::regclass,'SELECT')
     AND has_table_privilege(current_user,'programs.activity_report_source_bindings'::regclass,'SELECT')
     AND has_table_privilege(current_user,'programs.center_plan_issuance_links'::regclass,'SELECT')
     AND NOT (
      has_table_privilege(current_user,'programs.center_activity_reports'::regclass,'INSERT')
      OR has_table_privilege(current_user,'programs.activity_report_source_bindings'::regclass,'INSERT')
      -- Audit/outbox mutation and SET ROLE are already checked by VerifyReadOnlyAsync;
      -- never resolve governance regclass through an unprivileged reader schema.
      OR has_function_privilege(current_user,
       'programs.record_center_sourced_activity_command(text,uuid,uuid,uuid,text,uuid,text,text,timestamptz,text,uuid,uuid,text)','EXECUTE'))
    """,db,tx))
   {
    if(await capabilities.ExecuteScalarAsync(ct) is not true)
     return Results.Problem(statusCode:503,title:"Unsafe original report SQL privileges");
   }
   await using(var scope=new NpgsqlCommand("SELECT set_config('kanoonyar.center_id',@center,true)",db,tx))
   {scope.Parameters.AddWithValue("center",centerId);await scope.ExecuteNonQueryAsync(ct);}
   Guid draft;long pinned;long revision;string title,purpose,location;DateTime scheduled;
   // The latest immutable event plan and its binding are read from one snapshot.
   await using(var source=new NpgsqlCommand("""
    SELECT b.draft_id,b.pinned_plan_revision,v.revision,v.title,
           v.purpose,v.location_description,v.scheduled_at
    FROM events.center_events e
    JOIN events.center_event_plan_bindings b
      ON b.center_id=e.center_id AND b.event_id=e.event_id
    JOIN LATERAL(SELECT revision,title,purpose,location_description,scheduled_at
      FROM events.center_event_plan_revisions
      WHERE center_id=b.center_id AND event_id=b.event_id
      ORDER BY revision DESC LIMIT 1) v ON true
    WHERE e.center_id=@center AND e.event_id=@event AND EXISTS(
     SELECT 1 FROM centers.memberships m JOIN centers.registry c ON c.center_id=m.center_id
     WHERE m.center_id=e.center_id AND m.subject_id=@actor AND m.is_active AND c.is_active)
    """,db,tx))
   {
    source.Parameters.AddWithValue("center",centerId);
    source.Parameters.AddWithValue("event",eventId);
    source.Parameters.AddWithValue("actor",subject);
    await using var rd=await source.ExecuteReaderAsync(ct);
    if(!await rd.ReadAsync(ct))
     return Results.Problem(statusCode:404,title:"Private event not in authorized center");
    draft=rd.GetGuid(0);pinned=rd.GetInt64(1);revision=rd.GetInt64(2);
    title=rd.GetString(3);purpose=rd.GetString(4);location=rd.GetString(5);
    scheduled=rd.GetDateTime(6);
   }
   // No duplicate report materialization, no opaque legacy source upgraded to typed.
   // Exact source/plan revision match is required for every original report.
   await using var q=new NpgsqlCommand("""
    SELECT r.report_sequence,r.report_id,r.link_id,l.issuance_id,
     l.program_id,l.national_version,l.pinned_revision,
     r.reported_summary,r.evidence_ref,r.claimed_occurred_at,r.recorded_at
    FROM programs.activity_report_source_bindings typed
    JOIN programs.center_activity_reports r
     ON r.center_id=typed.center_id AND r.report_id=typed.report_id
      AND r.source_kind=typed.source_kind AND r.source_record_ref=typed.source_record_ref
    JOIN programs.center_plan_issuance_links l
     ON l.center_id=r.center_id AND l.link_id=r.link_id AND l.draft_id=r.draft_id
    WHERE typed.center_id=@center AND typed.source_kind='CENTER_EVENT'
     AND typed.event_id=@event AND r.draft_id=@draft AND l.pinned_revision=@pinned
     AND (@before::bigint IS NULL OR r.report_sequence<@before::bigint)
    ORDER BY r.report_sequence DESC LIMIT 21
    """,db,tx);
   q.Parameters.AddWithValue("center",centerId);
   q.Parameters.AddWithValue("event",eventId);
   q.Parameters.AddWithValue("draft",draft);
   q.Parameters.AddWithValue("pinned",pinned);
   q.Parameters.Add("before",NpgsqlDbType.Bigint).Value=beforeSequence.HasValue?
    (object)beforeSequence.Value:DBNull.Value;
   var reports=new List<object>();long? cursor=null;int count=0;
   await using(var rd=await q.ExecuteReaderAsync(ct))
   {
    while(await rd.ReadAsync(ct))
    {
     if(count<20)
     {
      cursor=rd.GetInt64(0);
      reports.Add(new{
       reportSequence=rd.GetInt64(0),reportId=rd.GetGuid(1),linkId=rd.GetGuid(2),
       issuanceId=rd.GetGuid(3),programId=rd.GetString(4),
       nationalVersion=rd.GetInt32(5),pinnedRevision=rd.GetInt64(6),
       reportedSummary=rd.GetString(7),evidenceRef=rd.GetString(8),
       claimedOccurredAt=rd.IsDBNull(9)?(DateTime?)null:rd.GetDateTime(9),
       recordedAt=rd.GetDateTime(10),sourceKind="CENTER_EVENT",sourceRecordRef=eventId,
       verificationStatus="UNVERIFIED",isApproved=false,isExecuted=false
      });
     }
     count++;
    }
   }
   await tx.CommitAsync(ct);
   return Results.Ok(new{eventId,draftId=draft,pinnedPlanRevision=pinned,revision,
    title,purpose,locationDescription=location,scheduledAt=scheduled,
    visibility="PRIVATE_DRAFT",isPublished=false,isExecuted=false,isRegistrationOpen=false,
    activityReports=reports,nextBeforeSequence=count>20?cursor:null,
    verificationStatus="UNVERIFIED",isApproved=false,isOutcome=false});
  }
  catch(NpgsqlException){return Results.Problem(statusCode:503,title:"Event dossier source unavailable");}
  catch(TimeoutException){return Results.Problem(statusCode:503,title:"Event dossier source unavailable");}
 }
}
