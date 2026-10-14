using System.Data;
using System.Security.Claims;
using Kanoonyar.Api.Security;
using Npgsql;
using NpgsqlTypes;

namespace Kanoonyar.Api.Programs;

public sealed record AssembledSiteDesign(long Revision,string LayoutKey,string PaletteKey,
 string TypeKey,string HeaderText,string FooterText,bool ShowIntro,bool ShowFooter,
 DateTime RecordedAt,string Visibility="PRIVATE_DRAFT",bool IsPublished=false);

/// <summary>
/// S37: coherent, read-only and nonpublic composition of exact immutable page
/// revisions pinned by the private navigation; no publication or grants.
/// </summary>
public static class CenterAssembledSitePreviewEndpoint
{
 private static async Task<bool> Allowed(ClaimsPrincipal actor,string center,
  ICenterSiteNavigationAuthorization nav,ICenterSiteDraftAuthorization pages,
  ICenterSiteDesignAuthorization design,CancellationToken ct)
 {
  try
  {
   return await nav.IsAuthorizedAsync(actor,center,"SITE_NAVIGATION_READ",ct)
    && await pages.IsAuthorizedAsync(actor,center,Guid.Empty,"SITE_DRAFT_LIST_READ",ct)
    && await design.IsAuthorizedAsync(actor,center,"SITE_DESIGN_READ",ct);
  }
  catch(OperationCanceledException) when(ct.IsCancellationRequested){throw;}
  catch{return false;}
 }
 public static async Task<IResult> Read(ClaimsPrincipal actor,IConfiguration config,
  ICenterSiteNavigationAuthorization navigation,ICenterSiteDraftAuthorization pages,
  ICenterSiteDesignAuthorization design,string centerId,long? navigationRevision,
  long? designRevision,int? afterPosition,CancellationToken ct)
 {
  if(actor.Identity?.IsAuthenticated!=true)
   return Results.Problem(statusCode:401,title:"Authentication required");
  if(string.IsNullOrWhiteSpace(actor.FindFirst("sub")?.Value))
   return Results.Problem(statusCode:403,title:"Signed OIDC subject required");
  if(string.IsNullOrWhiteSpace(centerId)||
   (navigationRevision.HasValue!=designRevision.HasValue)||
   (navigationRevision.HasValue&&navigationRevision.Value<=0)||
   (designRevision.HasValue&&designRevision.Value<=0)||
   (afterPosition.HasValue&&(!navigationRevision.HasValue||afterPosition.Value<=0)))
   return Results.Problem(statusCode:400,title:"Invalid immutable assembled preview cursor");
  if(!await Allowed(actor,centerId,navigation,pages,design,ct))
   return Results.Problem(statusCode:403,title:"Navigation, page and design rights required independently");
  var url=config["CENTER_DRAFT_READ_DATABASE_URL"];
  if(string.IsNullOrWhiteSpace(url))
   return Results.Problem(statusCode:503,title:"Private site preview reader unavailable");
  try
  {
   await using var db=new NpgsqlConnection(url);await db.OpenAsync(ct);
   await using var tx=await db.BeginTransactionAsync(IsolationLevel.RepeatableRead,ct);
   if(!await RestrictedSiteNavigationRole.ReaderAsync(db,tx,ct)||
      !await RestrictedSiteDesignRole.ReaderAsync(db,tx,ct))
    return Results.Problem(statusCode:503,title:"Unsafe assembled site preview SQL reader");
   await using(var scope=new NpgsqlCommand("SELECT set_config('kanoonyar.center_id',@center,true)",db,tx))
   {scope.Parameters.AddWithValue("center",centerId);await scope.ExecuteNonQueryAsync(ct);}
   await using(var member=new NpgsqlCommand("""
    SELECT EXISTS(SELECT 1 FROM centers.memberships m JOIN centers.registry c
     ON m.center_id=c.center_id WHERE m.center_id=@center AND m.subject_id=@actor
      AND m.is_active AND c.is_active)
    """,db,tx))
   {
    member.Parameters.AddWithValue("center",centerId);
    member.Parameters.AddWithValue("actor",actor.FindFirst("sub")!.Value);
    if(await member.ExecuteScalarAsync(ct) is not true)
     return Results.Problem(statusCode:403,title:"Live center membership required");
   }
   long? nr=navigationRevision,dr=designRevision;
   await using(var navQuery=new NpgsqlCommand("""
    SELECT revision FROM web.center_site_navigation_revisions WHERE center_id=@center
      AND (@exact::bigint IS NULL OR revision=@exact::bigint)
    ORDER BY revision DESC LIMIT 1
    """,db,tx))
   {
    navQuery.Parameters.AddWithValue("center",centerId);
    navQuery.Parameters.Add("exact",NpgsqlDbType.Bigint).Value=
     nr.HasValue?(object)nr.Value:DBNull.Value;
    nr=await navQuery.ExecuteScalarAsync(ct) as long?;
   }
   AssembledSiteDesign? designValue=null;
   await using(var designQuery=new NpgsqlCommand("""
    SELECT revision,layout_key,palette_key,type_key,header_text,footer_text,
      show_intro,show_footer,recorded_at
    FROM web.center_site_design_revisions WHERE center_id=@center
      AND (@exact::bigint IS NULL OR revision=@exact::bigint)
    ORDER BY revision DESC LIMIT 1
    """,db,tx))
   {
    designQuery.Parameters.AddWithValue("center",centerId);
    designQuery.Parameters.Add("exact",NpgsqlDbType.Bigint).Value=
     dr.HasValue?(object)dr.Value:DBNull.Value;
    await using var reader=await designQuery.ExecuteReaderAsync(ct);
    if(await reader.ReadAsync(ct))
     designValue=new(reader.GetInt64(0),reader.GetString(1),reader.GetString(2),
      reader.GetString(3),reader.GetString(4),reader.GetString(5),
      reader.GetBoolean(6),reader.GetBoolean(7),reader.GetDateTime(8));
   }
   if(navigationRevision.HasValue&&nr!=navigationRevision||
      designRevision.HasValue&&designValue?.Revision!=designRevision)
    return Results.Problem(statusCode:404,title:"Requested immutable private site version unavailable");
   dr=designValue?.Revision;
   if(!nr.HasValue||designValue is null)
   {
    await tx.CommitAsync(ct);
    return Results.Ok(new{ready=false,
     missingNavigation=!nr.HasValue,missingDesign=designValue is null,
     navigationRevision=nr,designRevision=dr,design=designValue,
     items=Array.Empty<object>(),nextAfterPosition=(int?)null,
     visibility="PRIVATE_DRAFT",isPublished=false,deliveryVerified=false});
   }
   await using var query=new NpgsqlCommand("""
    SELECT n.position,n.page_id,n.pinned_page_revision,p.slug,d.title,d.body_text
    FROM web.center_site_navigation_items n
    JOIN web.page_draft_revisions d ON d.center_id=n.center_id
      AND d.page_id=n.page_id AND d.revision=n.pinned_page_revision
    JOIN web.center_pages p ON p.center_id=n.center_id AND p.page_id=n.page_id
    WHERE n.center_id=@center AND n.navigation_revision=@nav
      AND n.position>@after
    ORDER BY n.position ASC LIMIT 21
    """,db,tx);
   query.Parameters.AddWithValue("center",centerId);
   query.Parameters.AddWithValue("nav",nr.Value);
   query.Parameters.AddWithValue("after",afterPosition??0);
   var records=new List<object>();int count=0;int? cursor=null;
   await using(var reader=await query.ExecuteReaderAsync(ct))
   {
    while(await reader.ReadAsync(ct))
    {
     if(count<20)
     {
      cursor=reader.GetInt32(0);
      records.Add(new{
       position=cursor,pageId=reader.GetGuid(1),
       pinnedPageRevision=reader.GetInt64(2),
       slug=reader.GetString(3),title=reader.GetString(4),bodyText=reader.GetString(5),
       visibility="PRIVATE_DRAFT",isPublished=false});
     }
     count++;
    }
   }
   await tx.CommitAsync(ct);
   return Results.Ok(new{
    ready=true,missingNavigation=false,missingDesign=false,
    navigationRevision=nr,designRevision=dr,design=designValue,
    items=records,nextAfterPosition=count>20?cursor:null,
    visibility="PRIVATE_DRAFT",isPublished=false,deliveryVerified=false});
  }
  catch(NpgsqlException){return Results.Problem(statusCode:503,title:"Private site preview source unavailable");}
  catch(TimeoutException){return Results.Problem(statusCode:503,title:"Private site preview source unavailable");}
 }
}
