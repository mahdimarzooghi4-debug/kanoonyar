using System.Security.Claims;
using System.Text.RegularExpressions;
using Kanoonyar.Api.Security;
using Npgsql;
using NpgsqlTypes;

namespace Kanoonyar.Api.Programs;

public sealed record AppendCenterSiteDraftRequest(Guid PageId,long ExpectedRevision,
 string Slug,string Title,string BodyText);
public interface ICenterSiteDraftAuthorization
{
 ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal user,string centerId,Guid pageId,
    string operation,CancellationToken ct);
}
public sealed class UnapprovedCenterSiteDraftAuthorization:ICenterSiteDraftAuthorization
{
 public ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal user,string centerId,Guid pageId,
    string operation,CancellationToken ct)=>ValueTask.FromResult(false);
}
public static class CenterSiteDraftEndpoint
{
 private static readonly Regex SlugPattern=new("^[a-z0-9]+(-[a-z0-9]+)*$",
    RegexOptions.CultureInvariant|RegexOptions.Compiled);
 private static IResult? Validate(ClaimsPrincipal user,string centerId,Guid pageId,bool requirePage)
 {
  if(user.Identity?.IsAuthenticated!=true)return Results.Problem(statusCode:401,title:"Authentication required");
  if(string.IsNullOrWhiteSpace(user.FindFirst("sub")?.Value))
    return Results.Problem(statusCode:403,title:"Verified subject required");
  if(string.IsNullOrWhiteSpace(centerId)||(requirePage&&pageId==Guid.Empty))
    return Results.Problem(statusCode:400,title:"Invalid center page identity");
  return null;
 }
 private static async Task<bool> Allowed(ICenterSiteDraftAuthorization auth,ClaimsPrincipal user,
  string center,Guid page,string operation,CancellationToken ct)
 {
  try {return await auth.IsAuthorizedAsync(user,center,page,operation,ct);}
  catch(OperationCanceledException) when(ct.IsCancellationRequested){throw;}
  catch{return false;}
 }
 public static async Task<IResult> Append(ClaimsPrincipal user,IConfiguration config,
  ICenterSiteDraftAuthorization auth,string centerId,AppendCenterSiteDraftRequest? body,
  CancellationToken ct)
 {
  var invalid=Validate(user,centerId,body?.PageId??Guid.Empty,true);
  if(invalid is not null)return invalid;
  if(body is null||body.ExpectedRevision<0||body.Slug is null||body.Slug.Length>80||
    !SlugPattern.IsMatch(body.Slug)||body.Title is null||body.Title.Trim().Length==0||
    body.Title.Length>200||body.BodyText is null||body.BodyText.Length>20000)
    return Results.Problem(statusCode:400,title:"Invalid private page draft");
  if(!await Allowed(auth,user,centerId,body.PageId,"SITE_DRAFT_APPEND",ct))
    return Results.Problem(statusCode:403,title:"Independent site draft write grant required");
  var url=config["CENTER_SITE_DRAFT_WRITE_DATABASE_URL"];
  if(string.IsNullOrWhiteSpace(url))return Results.Problem(statusCode:503,title:"Private page draft write unavailable");
  try
  {
   await using var db=new NpgsqlConnection(url);
   await db.OpenAsync(ct);
   await using var tx=await db.BeginTransactionAsync(ct);
   if(!await RestrictedCenterSiteDraftRole.WriterAsync(db,tx,ct))
    return Results.Problem(statusCode:503,title:"Unsafe site draft command role");
   await using(var scope=new NpgsqlCommand("SELECT set_config('kanoonyar.center_id',@center,true)",db,tx))
   {
    scope.Parameters.AddWithValue("center",centerId);
    await scope.ExecuteNonQueryAsync(ct);
   }
   await using var cmd=new NpgsqlCommand(
    """
    SELECT web.append_center_page_draft(
      @center,@page,@expected,@slug,@title,@body,@actor,@audit,@event,@correlation)
    """,db,tx);
   cmd.Parameters.AddWithValue("center",centerId);
   cmd.Parameters.AddWithValue("page",body.PageId);
   cmd.Parameters.AddWithValue("expected",body.ExpectedRevision);
   cmd.Parameters.AddWithValue("slug",body.Slug);
   cmd.Parameters.AddWithValue("title",body.Title.Trim());
   cmd.Parameters.AddWithValue("body",body.BodyText);
   cmd.Parameters.AddWithValue("actor",user.FindFirst("sub")!.Value);
   cmd.Parameters.AddWithValue("audit",Guid.NewGuid());
   cmd.Parameters.AddWithValue("event",Guid.NewGuid());
   cmd.Parameters.AddWithValue("correlation",Guid.NewGuid().ToString("N"));
   if(await cmd.ExecuteScalarAsync(ct) is not long rev || rev!=body.ExpectedRevision+1)
     throw new InvalidOperationException("Invalid appended site page revision");
   await tx.CommitAsync(ct);
   return Results.Ok(new{pageId=body.PageId,slug=body.Slug,revision=rev,
      visibility="PRIVATE_DRAFT",isPublished=false});
  }
  catch(PostgresException e) when(e.SqlState is "40001" or "23505" or "23503" or "23514")
  {return Results.Problem(statusCode:409,title:"Page draft collision or stale revision");}
  catch(PostgresException e) when(e.SqlState=="42501"&&e.MessageText=="Center scope or active membership absent")
  {return Results.Problem(statusCode:403,title:"Live center membership required");}
  catch(PostgresException e) when(e.SqlState=="42501")
  {return Results.Problem(statusCode:503,title:"Site draft SQL role privilege missing");}
  catch(PostgresException e) when(e.SqlState=="22023")
  {return Results.Problem(statusCode:400,title:"Invalid site page draft");}
  catch(NpgsqlException){return Results.Problem(statusCode:503,title:"Site draft storage unavailable");}
  catch(TimeoutException){return Results.Problem(statusCode:503,title:"Site draft storage unavailable");}
 }
 public static async Task<IResult> List(ClaimsPrincipal user,IConfiguration config,
  ICenterSiteDraftAuthorization auth,string centerId,Guid? beforePageId,CancellationToken ct)
 {
  return await Read(user,config,auth,centerId,null,beforePageId,ct);
 }
 public static async Task<IResult> History(ClaimsPrincipal user,IConfiguration config,
  ICenterSiteDraftAuthorization auth,string centerId,Guid pageId,long? beforeRevision,CancellationToken ct)
 {
  var invalid=Validate(user,centerId,pageId,true);
  if(invalid is not null)return invalid;
  if(beforeRevision<=0)return Results.Problem(statusCode:400,title:"Invalid private page revision cursor");
  return await Read(user,config,auth,centerId,pageId,null,ct,beforeRevision);
 }
 private static async Task<IResult> Read(ClaimsPrincipal user,IConfiguration config,
   ICenterSiteDraftAuthorization auth,string centerId,Guid? pageId,Guid? beforeId,
   CancellationToken ct,long? beforeRevision=null)
 {
  var invalid=Validate(user,centerId,pageId??Guid.Empty,pageId.HasValue);
  if(invalid is not null)return invalid;
  if(beforeId==Guid.Empty)return Results.Problem(statusCode:400,title:"Invalid page cursor");
  if(!await Allowed(auth,user,centerId,pageId??Guid.Empty,
     pageId.HasValue?"SITE_DRAFT_HISTORY_READ":"SITE_DRAFT_LIST_READ",ct))
    return Results.Problem(statusCode:403,title:"Independent private site page read grant required");
  var url=config["CENTER_DRAFT_READ_DATABASE_URL"];
  if(string.IsNullOrWhiteSpace(url))
    return Results.Problem(statusCode:503,title:"Private page draft read unavailable");
  try
  {
   await using var db=new NpgsqlConnection(url);await db.OpenAsync(ct);
   await using var tx=await db.BeginTransactionAsync(ct);
   if(!await RestrictedCenterSiteDraftRole.ReaderAsync(db,tx,ct))
    return Results.Problem(statusCode:503,title:"Unsafe site draft read role");
   await using(var scope=new NpgsqlCommand("SELECT set_config('kanoonyar.center_id',@center,true)",db,tx))
   {
    scope.Parameters.AddWithValue("center",centerId);
    await scope.ExecuteNonQueryAsync(ct);
   }
   await using(var member=new NpgsqlCommand(
    """
    SELECT EXISTS (SELECT 1 FROM centers.memberships m JOIN centers.registry c
      ON c.center_id=m.center_id WHERE m.center_id=@center AND m.subject_id=@actor
        AND m.is_active AND c.is_active)
    """,db,tx))
   {
    member.Parameters.AddWithValue("center",centerId);
    member.Parameters.AddWithValue("actor",user.FindFirst("sub")!.Value);
    if(await member.ExecuteScalarAsync(ct) is not true)
     return Results.Problem(statusCode:403,title:"Live center membership required");
   }
   if(pageId.HasValue)
   {
    string? slug=null;
    await using(var exists=new NpgsqlCommand(
     "SELECT slug FROM web.center_pages WHERE center_id=@center AND page_id=@page",db,tx))
    {
     exists.Parameters.AddWithValue("center",centerId);
     exists.Parameters.AddWithValue("page",pageId.Value);
     slug=await exists.ExecuteScalarAsync(ct) as string;
    }
    if(slug is null)return Results.Problem(statusCode:404,title:"Page not found in authorized center");
    await using var cmd=new NpgsqlCommand(
     """
     SELECT revision,title,body_text,recorded_at FROM web.page_draft_revisions
     WHERE center_id=@center AND page_id=@page
       AND (@before::bigint IS NULL OR revision<@before::bigint)
     ORDER BY revision DESC LIMIT 21
     """,db,tx);
    cmd.Parameters.AddWithValue("center",centerId);
    cmd.Parameters.AddWithValue("page",pageId.Value);
    cmd.Parameters.Add("before",NpgsqlDbType.Bigint).Value=
      beforeRevision.HasValue?(object)beforeRevision.Value:DBNull.Value;
    var records=new List<object>();long? cursor=null;int count=0;
    await using(var rdr=await cmd.ExecuteReaderAsync(ct))
    {
     while(await rdr.ReadAsync(ct))
     {
      if(count<20)
      {
       cursor=rdr.GetInt64(0);
       records.Add(new{revision=rdr.GetInt64(0),title=rdr.GetString(1),
         bodyText=rdr.GetString(2),recordedAt=rdr.GetDateTime(3),
         visibility="PRIVATE_DRAFT",isPublished=false});
      }
      count++;
     }
    }
    await tx.CommitAsync(ct);
    return Results.Ok(new{pageId=pageId.Value,slug,revisions=records,
      nextBeforeRevision=count>20?cursor:null});
   }
   await using var list=new NpgsqlCommand(
    """
    SELECT p.page_id,p.slug,v.revision,v.title,v.body_text,v.recorded_at
    FROM web.center_pages p
    JOIN LATERAL(
      SELECT revision,title,body_text,recorded_at FROM web.page_draft_revisions
      WHERE center_id=p.center_id AND page_id=p.page_id
      ORDER BY revision DESC LIMIT 1
    ) v ON true
    WHERE p.center_id=@center
      AND (@before::uuid IS NULL OR p.page_id<@before::uuid)
    ORDER BY p.page_id DESC LIMIT 21
    """,db,tx);
   list.Parameters.AddWithValue("center",centerId);
   list.Parameters.Add("before",NpgsqlDbType.Uuid).Value=beforeId.HasValue?(object)beforeId.Value:DBNull.Value;
   var pages=new List<object>();Guid? next=null;int total=0;
   await using(var rdr=await list.ExecuteReaderAsync(ct))
   {
    while(await rdr.ReadAsync(ct))
    {
     if(total<20)
     {
      next=rdr.GetGuid(0);
      pages.Add(new{pageId=rdr.GetGuid(0),slug=rdr.GetString(1),
       revision=rdr.GetInt64(2),title=rdr.GetString(3),bodyText=rdr.GetString(4),
       recordedAt=rdr.GetDateTime(5),visibility="PRIVATE_DRAFT",isPublished=false});
     }
     total++;
    }
   }
   await tx.CommitAsync(ct);
   return Results.Ok(new{pages,nextBeforePageId=total>20?next:null});
  }
  catch(NpgsqlException){return Results.Problem(statusCode:503,title:"Private page draft read unavailable");}
  catch(TimeoutException){return Results.Problem(statusCode:503,title:"Private page draft read unavailable");}
 }
}
