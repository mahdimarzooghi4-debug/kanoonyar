using System.Data;
using System.Security.Claims;
using Kanoonyar.Api.Security;
using Npgsql;
using NpgsqlTypes;

namespace Kanoonyar.Api.Programs;

/// <summary>Read-only private scheduling view over existing source owners. Not occurrence evidence.</summary>
public static class CenterPrivateCalendarEndpoint
{
 public static async Task<IResult> Get(
  ClaimsPrincipal actor,IConfiguration config,
  ICenterEventPlanAuthorization eventGrant,ICenterSourceCatalogReadAuthorization classGrant,
  string centerId,DateTimeOffset fromUtc,DateTimeOffset untilUtc,
  DateTimeOffset? beforeAt,string? beforeKind,Guid? beforeSourceId,CancellationToken ct)
 {
  if(actor.Identity?.IsAuthenticated!=true)
   return Results.Problem(statusCode:401,title:"Authentication required");
  var subject=actor.FindFirst("sub")?.Value;
  if(string.IsNullOrWhiteSpace(subject))
   return Results.Problem(statusCode:403,title:"Verified OIDC subject required");
  var cursorSet=beforeAt.HasValue||beforeKind is not null||beforeSourceId.HasValue;
  if(string.IsNullOrWhiteSpace(centerId)||fromUtc==default||untilUtc==default||
   fromUtc.Offset!=TimeSpan.Zero||untilUtc.Offset!=TimeSpan.Zero||fromUtc>=untilUtc||
   (cursorSet&&(!beforeAt.HasValue||beforeAt.Value.Offset!=TimeSpan.Zero||
    beforeAt.Value<fromUtc||beforeAt.Value>=untilUtc||
    beforeKind is not ("CLASS_SESSION" or "CENTER_EVENT")||
    !beforeSourceId.HasValue||beforeSourceId.Value==Guid.Empty)))
   return Results.Problem(statusCode:400,title:"Invalid private calendar window or keyset cursor");
  try
  {
   if(!await eventGrant.IsAuthorizedAsync(actor,centerId,Guid.Empty,"CENTER_PRIVATE_CALENDAR_READ",ct)||
      !await classGrant.IsAuthorizedAsync(actor,centerId,ct))
    return Results.Problem(statusCode:403,title:"Independent event and class schedule read grants required");
  }
  catch(OperationCanceledException) when(ct.IsCancellationRequested){throw;}
  catch{return Results.Problem(statusCode:403,title:"Calendar rights unavailable");}
  var url=config["CENTER_DRAFT_READ_DATABASE_URL"];
  if(string.IsNullOrWhiteSpace(url))
   return Results.Problem(statusCode:503,title:"Private calendar storage unavailable");
  try
  {
   await using var db=new NpgsqlConnection(url);await db.OpenAsync(ct);
   await using var tx=await db.BeginTransactionAsync(IsolationLevel.RepeatableRead,ct);
   if(!await RestrictedCenterEventPlanRole.ReaderAsync(db,tx,ct))
    return Results.Problem(statusCode:503,title:"Unsafe private event calendar reader");
   await using(var capabilities=new NpgsqlCommand("""
    SELECT has_table_privilege(current_user,'learning.class_sessions'::regclass,'SELECT')
      AND NOT (has_table_privilege(current_user,'learning.class_sessions'::regclass,'INSERT')
        OR has_table_privilege(current_user,'learning.class_sessions'::regclass,'UPDATE')
        OR has_table_privilege(current_user,'learning.class_sessions'::regclass,'DELETE'))
    """,db,tx))
   {
    if(await capabilities.ExecuteScalarAsync(ct) is not true)
     return Results.Problem(statusCode:503,title:"Unsafe class schedule SQL capability");
   }
   await using(var scope=new NpgsqlCommand("SELECT set_config('kanoonyar.center_id',@center,true)",db,tx))
   {scope.Parameters.AddWithValue("center",centerId);await scope.ExecuteNonQueryAsync(ct);}
   await using(var member=new NpgsqlCommand("""
    SELECT EXISTS(SELECT 1 FROM centers.memberships m JOIN centers.registry c
     ON c.center_id=m.center_id WHERE m.center_id=@center AND m.subject_id=@actor
      AND m.is_active AND c.is_active)
    """,db,tx))
   {
    member.Parameters.AddWithValue("center",centerId);
    member.Parameters.AddWithValue("actor",subject);
    if(await member.ExecuteScalarAsync(ct) is not true)
     return Results.Problem(statusCode:403,title:"Live center membership required");
   }
   // One ordered snapshot; latest event revision only, never the immutable S12 genesis time.
   // Undated sessions are unknown and excluded, never counted as absent/zero.
   await using var query=new NpgsqlCommand("""
    WITH sources AS (
     SELECT s.scheduled_at AS at_utc,'CLASS_SESSION'::text AS source_kind,
      s.session_id AS source_id,s.session_title AS title,
      NULL::uuid AS draft_id,NULL::bigint AS pinned_revision,
      NULL::bigint AS plan_revision
     FROM learning.class_sessions s
     WHERE s.center_id=@center AND s.scheduled_at IS NOT NULL
     UNION ALL
     SELECT v.scheduled_at,'CENTER_EVENT'::text,b.event_id,v.title,
      b.draft_id,b.pinned_plan_revision,v.revision
     FROM events.center_events e
     JOIN events.center_event_plan_bindings b ON b.center_id=e.center_id
      AND b.event_id=e.event_id
     JOIN LATERAL (SELECT scheduled_at,title,revision
       FROM events.center_event_plan_revisions versions
       WHERE versions.center_id=b.center_id AND versions.event_id=b.event_id
       ORDER BY versions.revision DESC LIMIT 1) v ON true
     WHERE e.center_id=@center
    )
    SELECT at_utc,source_kind,source_id,title,draft_id,pinned_revision,plan_revision
    FROM sources
    WHERE at_utc>=@from AND at_utc<@until
      AND (@before::timestamptz IS NULL OR
        (at_utc,source_kind,source_id)>
        (@before::timestamptz,@before_kind::text,@before_id::uuid))
    ORDER BY at_utc ASC,source_kind ASC,source_id ASC LIMIT 21
    """,db,tx);
   query.Parameters.AddWithValue("center",centerId);
   query.Parameters.Add("from",NpgsqlDbType.TimestampTz).Value=fromUtc.UtcDateTime;
   query.Parameters.Add("until",NpgsqlDbType.TimestampTz).Value=untilUtc.UtcDateTime;
   query.Parameters.Add("before",NpgsqlDbType.TimestampTz).Value=beforeAt.HasValue?
    (object)beforeAt.Value.UtcDateTime:DBNull.Value;
   query.Parameters.Add("before_kind",NpgsqlDbType.Text).Value=beforeKind is null?
    DBNull.Value:beforeKind;
   query.Parameters.Add("before_id",NpgsqlDbType.Uuid).Value=beforeSourceId.HasValue?
    (object)beforeSourceId.Value:DBNull.Value;
   var items=new List<object>();DateTime? lastAt=null;Guid? lastId=null;string? lastKind=null;int count=0;
   await using(var rd=await query.ExecuteReaderAsync(ct))
   {
    while(await rd.ReadAsync(ct))
    {
     if(count<20)
     {
      lastAt=rd.GetDateTime(0);lastKind=rd.GetString(1);lastId=rd.GetGuid(2);
      items.Add(new{
       scheduledAt=lastAt,sourceKind=lastKind,sourceId=lastId,
       title=rd.GetString(3),
       draftId=rd.IsDBNull(4)?(Guid?)null:rd.GetGuid(4),
       pinnedPlanRevision=rd.IsDBNull(5)?(long?)null:rd.GetInt64(5),
       planRevision=rd.IsDBNull(6)?(long?)null:rd.GetInt64(6),
       visibility="PRIVATE_PLAN",isPublished=false,isExecuted=false,isAttendanceVerified=false
      });
     }
     count++;
    }
   }
   await tx.CommitAsync(ct);
   object? next=count>20?new{scheduledAt=lastAt,sourceKind=lastKind,sourceId=lastId}:null;
   return Results.Ok(new{
    fromUtc=fromUtc.UtcDateTime,untilUtc=untilUtc.UtcDateTime,items,nextCursor=next,
    visibility="PRIVATE_PLAN",isExecuted=false,isPublished=false,
    isAttendanceVerified=false
   });
  }
  catch(NpgsqlException){return Results.Problem(statusCode:503,title:"Private calendar source unavailable");}
  catch(TimeoutException){return Results.Problem(statusCode:503,title:"Private calendar source unavailable");}
 }
}
