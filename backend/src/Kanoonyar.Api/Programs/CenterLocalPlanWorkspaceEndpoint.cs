using System.Data;
using System.Security.Claims;
using Kanoonyar.Api.Security;
using Npgsql;
using NpgsqlTypes;

namespace Kanoonyar.Api.Programs;

/// <summary>
/// Composes existing immutable original source records in ONE repeatable-read snapshot.
/// No status mutation, resource reservation, funding, execution, or approval.
/// </summary>
public static class CenterLocalPlanWorkspaceEndpoint
{
 private sealed record WorkCardRow(Guid CardId,long Revision,string Title,string PlanningNote,
  DateTime? PlannedDueAt,DateTime RecordedAt);
 private sealed record NeedRow(Guid NeedId,long Revision,string Label,string Detail,
  DateTime? DesiredBy,DateTime RecordedAt);

 public static async Task<IResult> Get(ClaimsPrincipal actor,IConfiguration config,
  ILocalPlanDraftReadAuthorization planGrant,ICenterWorkCardAuthorization cardGrant,
  ICenterWorkResourceNeedAuthorization needGrant,string centerId,Guid draftId,
  long planRevision,Guid? beforeCardId,CancellationToken ct)
 {
  if(actor.Identity?.IsAuthenticated!=true)
   return Results.Problem(statusCode:401,title:"Authentication required");
  var subject=actor.FindFirst("sub")?.Value;
  if(string.IsNullOrWhiteSpace(subject))
   return Results.Problem(statusCode:403,title:"Verified OIDC subject required");
  if(string.IsNullOrWhiteSpace(centerId)||draftId==Guid.Empty||planRevision<=0||
   beforeCardId==Guid.Empty)
   return Results.Problem(statusCode:400,title:"Invalid source plan or work-card cursor");
  try
  {
   if(!await planGrant.IsAuthorizedAsync(actor,centerId,ct)||
      !await cardGrant.IsAuthorizedAsync(actor,centerId,"WORK_CARD_LIST_READ",Guid.Empty,ct)||
      !await needGrant.IsAuthorizedAsync(actor,centerId,Guid.Empty,"WORK_RESOURCE_NEED_LIST_READ",ct))
    return Results.Problem(statusCode:403,title:"Three independent source-owner read permissions required");
  }
  catch(OperationCanceledException) when(ct.IsCancellationRequested){throw;}
  catch{return Results.Problem(statusCode:403,title:"Source-owner permissions unavailable");}
  var url=config["CENTER_DRAFT_READ_DATABASE_URL"];
  if(string.IsNullOrWhiteSpace(url))
   return Results.Problem(statusCode:503,title:"Private local-plan workspace reader unavailable");
  try
  {
   await using var db=new NpgsqlConnection(url);await db.OpenAsync(ct);
   await using var tx=await db.BeginTransactionAsync(IsolationLevel.RepeatableRead,ct);
   if(!await RestrictedWorkResourceNeedRole.ReaderAsync(db,tx,ct))
    return Results.Problem(statusCode:503,title:"Unsafe local-plan workspace SQL reader");
   await using(var caps=new NpgsqlCommand("""
    SELECT has_table_privilege(current_user,
     'programs.center_plan_draft_revisions'::regclass,'SELECT')
     AND NOT (has_table_privilege(current_user,
       'programs.center_plan_draft_revisions'::regclass,'INSERT')
       OR has_table_privilege(current_user,
        'programs.center_plan_draft_revisions'::regclass,'UPDATE')
       OR has_table_privilege(current_user,
        'programs.center_plan_draft_revisions'::regclass,'DELETE'))
    """,db,tx))
   {
    if(await caps.ExecuteScalarAsync(ct) is not true)
     return Results.Problem(statusCode:503,title:"Unsafe original plan reader");
   }
   await using(var scope=new NpgsqlCommand("SELECT set_config('kanoonyar.center_id',@center,true)",db,tx))
   {
    scope.Parameters.AddWithValue("center",centerId);
    await scope.ExecuteNonQueryAsync(ct);
   }
   await using(var active=new NpgsqlCommand("""
    SELECT EXISTS(SELECT 1 FROM centers.memberships m JOIN centers.registry c
      ON m.center_id=c.center_id WHERE m.center_id=@center AND m.subject_id=@subject
       AND m.is_active AND c.is_active)
    """,db,tx))
   {
    active.Parameters.AddWithValue("center",centerId);
    active.Parameters.AddWithValue("subject",subject);
    if(await active.ExecuteScalarAsync(ct) is not true)
     return Results.Problem(statusCode:403,title:"Active center membership required");
   }
   string localTitle;string programId;long nationalVersion;
   await using(var source=new NpgsqlCommand("""
    SELECT local_title,program_id,national_version
    FROM programs.center_plan_draft_revisions
    WHERE center_id=@center AND draft_id=@draft AND revision=@revision
    """,db,tx))
   {
    source.Parameters.AddWithValue("center",centerId);
    source.Parameters.AddWithValue("draft",draftId);
    source.Parameters.AddWithValue("revision",planRevision);
    await using var read=await source.ExecuteReaderAsync(ct);
    if(!await read.ReadAsync(ct))
     return Results.Problem(statusCode:404,title:"Exact local plan revision unavailable");
    localTitle=read.GetString(0);programId=read.GetString(1);
    nationalVersion=read.GetInt32(2);
   }
   var cards=new List<WorkCardRow>();Guid? lastId=null;var count=0;
   await using(var query=new NpgsqlCommand("""
    SELECT c.work_card_id,r.revision,r.title,r.planning_note,
      r.planned_due_at,r.recorded_at
    FROM operations.center_work_cards c
    JOIN LATERAL(
      SELECT revision,title,planning_note,planned_due_at,recorded_at
      FROM operations.center_work_card_revisions v
      WHERE v.center_id=c.center_id AND v.work_card_id=c.work_card_id
      ORDER BY revision DESC LIMIT 1
    ) r ON true
    WHERE c.center_id=@center AND c.draft_id=@draft
     AND c.pinned_plan_revision=@revision
     AND (@before::uuid IS NULL OR c.work_card_id<@before::uuid)
    ORDER BY c.work_card_id DESC LIMIT 21
    """,db,tx))
   {
    query.Parameters.AddWithValue("center",centerId);
    query.Parameters.AddWithValue("draft",draftId);
    query.Parameters.AddWithValue("revision",planRevision);
    query.Parameters.Add("before",NpgsqlDbType.Uuid).Value=beforeCardId.HasValue?
     (object)beforeCardId.Value:DBNull.Value;
    await using var rd=await query.ExecuteReaderAsync(ct);
    while(await rd.ReadAsync(ct))
    {
     if(count<20)
     {
      lastId=rd.GetGuid(0);
      cards.Add(new WorkCardRow(lastId.Value,rd.GetInt64(1),rd.GetString(2),
       rd.GetString(3),rd.IsDBNull(4)?null:rd.GetDateTime(4),rd.GetDateTime(5)));
     }
     count++;
    }
   }
   // Aggregate read rights cannot widen per-resource grants.
   // Deny the *whole* page if any returned card is not covered.
   foreach(var card in cards)
   {
    bool allowed;
    try
    {
     allowed=await cardGrant.IsAuthorizedAsync(actor,centerId,"WORK_CARD_LIST_READ",card.CardId,ct)
      && await needGrant.IsAuthorizedAsync(actor,centerId,card.CardId,"WORK_RESOURCE_NEED_LIST_READ",ct);
    }
    catch(OperationCanceledException) when(ct.IsCancellationRequested){throw;}
    catch{return Results.Problem(statusCode:403,title:"Card-specific rights unavailable");}
    if(!allowed)return Results.Problem(statusCode:403,title:"Card-specific read rights required");
   }
   var output=new List<object>();
   foreach(var card in cards)
   {
    var needs=new List<NeedRow>();Guid? lastNeed=null;var total=0;
    await using var query=new NpgsqlCommand("""
     SELECT n.need_id,r.revision,r.resource_label,r.planning_detail,
      r.desired_by,r.recorded_at
     FROM operations.center_work_resource_needs n
     JOIN LATERAL(
      SELECT revision,resource_label,planning_detail,desired_by,recorded_at
      FROM operations.center_work_resource_need_revisions v
      WHERE v.center_id=n.center_id AND v.need_id=n.need_id
      ORDER BY revision DESC LIMIT 1
     ) r ON true
     WHERE n.center_id=@center AND n.work_card_id=@card
     ORDER BY n.need_id DESC LIMIT 6
     """,db,tx);
    query.Parameters.AddWithValue("center",centerId);
    query.Parameters.AddWithValue("card",card.CardId);
    await using(var rd=await query.ExecuteReaderAsync(ct))
    {
     while(await rd.ReadAsync(ct))
     {
      if(total<5)
      {
       lastNeed=rd.GetGuid(0);
       needs.Add(new NeedRow(lastNeed.Value,rd.GetInt64(1),rd.GetString(2),
        rd.GetString(3),rd.IsDBNull(4)?null:rd.GetDateTime(4),rd.GetDateTime(5)));
      }
      total++;
     }
    }
    output.Add(new{
     workCardId=card.CardId,revision=card.Revision,title=card.Title,
     planningNote=card.PlanningNote,plannedDueAt=card.PlannedDueAt,
     recordedAt=card.RecordedAt,
     draftId,pinnedPlanRevision=planRevision,
     needs=needs.Select(n=>new{
      needId=n.NeedId,workCardId=card.CardId,revision=n.Revision,
      resourceLabel=n.Label,planningDetail=n.Detail,desiredBy=n.DesiredBy,
      recordedAt=n.RecordedAt,visibility="PRIVATE_PLANNING",
      fulfillmentStatus="UNKNOWN",isAllocated=false,isPurchased=false,isFulfilled=false
     }).ToArray(),needsPreviewLimit=5,
     nextBeforeNeedId=total>5?lastNeed:null,
     visibility="PRIVATE_DRAFT",executionStatus="UNKNOWN",isExecuted=false
    });
   }
   await tx.CommitAsync(ct);
   return Results.Ok(new{
    sourcePlan=new{draftId,pinnedPlanRevision=planRevision,localTitle,
     programId,nationalVersion,visibility="PRIVATE_DRAFT",isPublished=false},
    cards=output,nextBeforeCardId=count>20?lastId:null,
    visibility="PRIVATE_PLANNING",executionStatus="UNKNOWN",
    fulfillmentStatus="UNKNOWN",isExecuted=false,isAllocated=false,
    isPurchased=false,isFulfilled=false
   });
  }
  catch(NpgsqlException){return Results.Problem(statusCode:503,title:"Local-plan workspace database unavailable");}
  catch(TimeoutException){return Results.Problem(statusCode:503,title:"Local-plan workspace unavailable");}
 }
}
