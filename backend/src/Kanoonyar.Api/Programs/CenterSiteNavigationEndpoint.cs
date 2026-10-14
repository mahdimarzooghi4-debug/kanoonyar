using System.Data;
using System.Security.Claims;
using Kanoonyar.Api.Security;
using Npgsql;
using NpgsqlTypes;

namespace Kanoonyar.Api.Programs;

public sealed record SiteNavigationPageRef(Guid PageId,long PinnedPageRevision);
public sealed record AppendCenterSiteNavigationRequest(long ExpectedRevision,
 List<SiteNavigationPageRef>? Items);
public interface ICenterSiteNavigationAuthorization
{
 ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal actor,string centerId,
  string operation,CancellationToken ct);
}
public sealed class UnapprovedCenterSiteNavigationAuthorization:ICenterSiteNavigationAuthorization
{
 public ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal actor,string centerId,
  string operation,CancellationToken ct)=>ValueTask.FromResult(false);
}
public static class CenterSiteNavigationEndpoint
{
 private static IResult? Identity(ClaimsPrincipal actor,string centerId)
 {
  if(actor.Identity?.IsAuthenticated!=true)
   return Results.Problem(statusCode:401,title:"Authentication required");
  if(string.IsNullOrWhiteSpace(actor.FindFirst("sub")?.Value))
   return Results.Problem(statusCode:403,title:"Verified OIDC subject required");
  if(string.IsNullOrWhiteSpace(centerId))
   return Results.Problem(statusCode:400,title:"Invalid center identity");
  return null;
 }
 private static async Task<bool> Granted(ClaimsPrincipal actor,string centerId,
  ICenterSiteNavigationAuthorization navigation,ICenterSiteDraftAuthorization pages,
  string operation,CancellationToken ct)
 {
  try
  {
   return await navigation.IsAuthorizedAsync(actor,centerId,operation,ct)
    && await pages.IsAuthorizedAsync(actor,centerId,Guid.Empty,"SITE_DRAFT_LIST_READ",ct);
  }
  catch(OperationCanceledException) when(ct.IsCancellationRequested){throw;}
  catch{return false;}
 }
 public static async Task<IResult> Append(ClaimsPrincipal actor,IConfiguration config,
  ICenterSiteNavigationAuthorization navigation,ICenterSiteDraftAuthorization pages,
  string centerId,AppendCenterSiteNavigationRequest? request,CancellationToken ct)
 {
  var invalid=Identity(actor,centerId);if(invalid is not null)return invalid;
  if(request is null||request.ExpectedRevision<0||request.Items is null||
    request.Items.Any(x=>x is null||x.PageId==Guid.Empty||x.PinnedPageRevision<=0)||
    request.Items.Select(x=>x.PageId).Distinct().Count()!=request.Items.Count)
   return Results.Problem(statusCode:400,title:"Invalid ordered private site links");
  if(!await Granted(actor,centerId,navigation,pages,"SITE_NAVIGATION_APPEND",ct))
   return Results.Problem(statusCode:403,title:"Independent navigation and page grants required");
  var url=config["CENTER_SITE_NAVIGATION_WRITE_DATABASE_URL"];
  if(string.IsNullOrWhiteSpace(url))
   return Results.Problem(statusCode:503,title:"Private navigation command unavailable");
  try
  {
   await using var db=new NpgsqlConnection(url);await db.OpenAsync(ct);
   await using var tx=await db.BeginTransactionAsync(ct);
   if(!await RestrictedSiteNavigationRole.WriterAsync(db,tx,ct))
    return Results.Problem(statusCode:503,title:"Unsafe navigation SQL writer");
   await using(var scope=new NpgsqlCommand("SELECT set_config('kanoonyar.center_id',@center,true)",db,tx))
   {scope.Parameters.AddWithValue("center",centerId);await scope.ExecuteNonQueryAsync(ct);}
   await using var cmd=new NpgsqlCommand("""
    SELECT web.append_center_site_navigation(@center,@expected,@ids,@revisions,
      @actor,@audit,@event,@correlation)
    """,db,tx);
   cmd.Parameters.AddWithValue("center",centerId);
   cmd.Parameters.AddWithValue("expected",request.ExpectedRevision);
   cmd.Parameters.Add("ids",NpgsqlDbType.Array|NpgsqlDbType.Uuid).Value=
     request.Items.Select(x=>x.PageId).ToArray();
   cmd.Parameters.Add("revisions",NpgsqlDbType.Array|NpgsqlDbType.Bigint).Value=
     request.Items.Select(x=>x.PinnedPageRevision).ToArray();
   cmd.Parameters.AddWithValue("actor",actor.FindFirst("sub")!.Value);
   cmd.Parameters.AddWithValue("audit",Guid.NewGuid());
   cmd.Parameters.AddWithValue("event",Guid.NewGuid());
   cmd.Parameters.AddWithValue("correlation",Guid.NewGuid().ToString("N"));
   if(await cmd.ExecuteScalarAsync(ct) is not long revision||
      revision!=request.ExpectedRevision+1)
    throw new InvalidOperationException("Private site navigation revision mismatch");
   await tx.CommitAsync(ct);
   return Results.Ok(new{revision,linkedPages=request.Items.Count,
     visibility="PRIVATE_DRAFT",isPublished=false});
  }
  catch(PostgresException e) when(e.SqlState is "40001" or "23503" or "23505" or "23514")
   {return Results.Problem(statusCode:409,title:"Stale navigation or invalid source revision");}
  catch(PostgresException e) when(e.SqlState=="42501"&&
   e.MessageText=="Center scope or active membership absent")
   {return Results.Problem(statusCode:403,title:"Live center membership required");}
  catch(PostgresException e) when(e.SqlState=="22023")
   {return Results.Problem(statusCode:400,title:"Invalid ordered navigation");}
  catch(PostgresException e) when(e.SqlState=="42501")
   {return Results.Problem(statusCode:503,title:"Navigation SQL privilege unavailable");}
  catch(NpgsqlException){return Results.Problem(statusCode:503,title:"Navigation storage unavailable");}
  catch(TimeoutException){return Results.Problem(statusCode:503,title:"Navigation storage unavailable");}
 }
 public static Task<IResult> Latest(ClaimsPrincipal actor,IConfiguration config,
  ICenterSiteNavigationAuthorization navigation,ICenterSiteDraftAuthorization pages,
  string centerId,CancellationToken ct)=>
  Read(actor,config,navigation,pages,centerId,null,false,ct);
 public static Task<IResult> History(ClaimsPrincipal actor,IConfiguration config,
  ICenterSiteNavigationAuthorization navigation,ICenterSiteDraftAuthorization pages,
  string centerId,long? beforeRevision,CancellationToken ct)=>
  Read(actor,config,navigation,pages,centerId,beforeRevision,true,ct);
 private sealed record NavLink(int Position,Guid PageId,long PinnedPageRevision,
  string Slug,string Title);
 private static async Task<List<NavLink>> Links(NpgsqlConnection db,NpgsqlTransaction tx,
  string center,long revision,CancellationToken ct)
 {
  await using var query=new NpgsqlCommand("""
   SELECT l.position,l.page_id,l.pinned_page_revision,p.slug,r.title
   FROM web.center_site_navigation_items l
   JOIN web.center_pages p ON p.center_id=l.center_id AND p.page_id=l.page_id
   JOIN web.page_draft_revisions r ON r.center_id=l.center_id
    AND r.page_id=l.page_id AND r.revision=l.pinned_page_revision
   WHERE l.center_id=@center AND l.navigation_revision=@revision
   ORDER BY l.position ASC
   """,db,tx);
  query.Parameters.AddWithValue("center",center);
  query.Parameters.AddWithValue("revision",revision);
  var rows=new List<NavLink>();
  await using var reader=await query.ExecuteReaderAsync(ct);
  while(await reader.ReadAsync(ct))
   rows.Add(new(reader.GetInt32(0),reader.GetGuid(1),reader.GetInt64(2),
    reader.GetString(3),reader.GetString(4)));
  return rows;
 }
 private static async Task<IResult> Read(ClaimsPrincipal actor,IConfiguration config,
  ICenterSiteNavigationAuthorization navigation,ICenterSiteDraftAuthorization pages,
  string centerId,long? before,bool history,CancellationToken ct)
 {
  var invalid=Identity(actor,centerId);if(invalid is not null)return invalid;
  if(history&&before.HasValue&&before.Value<=0)
   return Results.Problem(statusCode:400,title:"Invalid navigation revision cursor");
  if(!await Granted(actor,centerId,navigation,pages,
     history?"SITE_NAVIGATION_HISTORY_READ":"SITE_NAVIGATION_READ",ct))
   return Results.Problem(statusCode:403,title:"Independent navigation and page read grants required");
  var url=config["CENTER_DRAFT_READ_DATABASE_URL"];
  if(string.IsNullOrWhiteSpace(url))
   return Results.Problem(statusCode:503,title:"Navigation read unavailable");
  try
  {
   await using var db=new NpgsqlConnection(url);await db.OpenAsync(ct);
   await using var tx=await db.BeginTransactionAsync(IsolationLevel.RepeatableRead,ct);
   if(!await RestrictedSiteNavigationRole.ReaderAsync(db,tx,ct))
    return Results.Problem(statusCode:503,title:"Unsafe navigation reader role");
   await using(var scope=new NpgsqlCommand("SELECT set_config('kanoonyar.center_id',@center,true)",db,tx))
   {scope.Parameters.AddWithValue("center",centerId);await scope.ExecuteNonQueryAsync(ct);}
   await using(var member=new NpgsqlCommand("""
    SELECT EXISTS(SELECT 1 FROM centers.memberships m
     JOIN centers.registry c ON c.center_id=m.center_id
     WHERE m.center_id=@center AND m.subject_id=@actor AND m.is_active AND c.is_active)
    """,db,tx))
   {
    member.Parameters.AddWithValue("center",centerId);
    member.Parameters.AddWithValue("actor",actor.FindFirst("sub")!.Value);
    if(await member.ExecuteScalarAsync(ct) is not true)
     return Results.Problem(statusCode:403,title:"Live center membership required");
   }
   if(!history)
   {
    await using var query=new NpgsqlCommand("""
     SELECT max(revision) FROM web.center_site_navigation_revisions WHERE center_id=@center
     """,db,tx);
    query.Parameters.AddWithValue("center",centerId);
    var result=await query.ExecuteScalarAsync(ct);
    long? revision=result is long r?r:null;
    var links=revision.HasValue?await Links(db,tx,centerId,revision.Value,ct):new List<NavLink>();
    await tx.CommitAsync(ct);
    return Results.Ok(new{revision,items=links,visibility="PRIVATE_DRAFT",isPublished=false});
   }
   await using var cmd=new NpgsqlCommand("""
    SELECT revision,recorded_at FROM web.center_site_navigation_revisions
    WHERE center_id=@center AND (@before::bigint IS NULL OR revision<@before::bigint)
    ORDER BY revision DESC LIMIT 21
    """,db,tx);
   cmd.Parameters.AddWithValue("center",centerId);
   cmd.Parameters.Add("before",NpgsqlDbType.Bigint).Value=before.HasValue?(object)before.Value:DBNull.Value;
   var revisionRows=new List<(long Revision,DateTime At)>();
   await using(var reader=await cmd.ExecuteReaderAsync(ct))
   {
    while(await reader.ReadAsync(ct))
    {
     if(revisionRows.Count<21)revisionRows.Add((reader.GetInt64(0),reader.GetDateTime(1)));
    }
   }
   var rows=new List<object>();
   foreach(var row in revisionRows.Take(20))
   {
    var links=await Links(db,tx,centerId,row.Revision,ct);
    rows.Add(new{revision=row.Revision,recordedAt=row.At,items=links,
      visibility="PRIVATE_DRAFT",isPublished=false});
   }
   await tx.CommitAsync(ct);
   return Results.Ok(new{revisions=rows,
    nextBeforeRevision=revisionRows.Count>20?(long?)revisionRows[19].Revision:null,
    visibility="PRIVATE_DRAFT",isPublished=false});
  }
  catch(NpgsqlException){return Results.Problem(statusCode:503,title:"Navigation read unavailable");}
  catch(TimeoutException){return Results.Problem(statusCode:503,title:"Navigation read unavailable");}
 }
}
