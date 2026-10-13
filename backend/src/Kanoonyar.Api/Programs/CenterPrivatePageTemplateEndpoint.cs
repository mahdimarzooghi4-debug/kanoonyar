using System.Data;
using System.Security.Claims;
using Kanoonyar.Api.Security;
using Npgsql;
using NpgsqlTypes;

namespace Kanoonyar.Api.Programs;

public sealed record AppendPrivatePageTemplateRequest(Guid TemplateId,Guid SourcePageId,
 long ExpectedRevision,long PinnedPageRevision,string Label);
public interface ICenterPrivatePageTemplateAuthorization
{
 ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal actor,string centerId,string operation,
  CancellationToken ct);
}
public sealed class UnapprovedPrivatePageTemplateAuthorization:ICenterPrivatePageTemplateAuthorization
{
 public ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal actor,string centerId,string operation,
  CancellationToken ct)=>ValueTask.FromResult(false);
}
public static class CenterPrivatePageTemplateEndpoint
{
 private static IResult? Identity(ClaimsPrincipal actor,string centerId,Guid? templateId=null)
 {
  if(actor.Identity?.IsAuthenticated!=true)
   return Results.Problem(statusCode:401,title:"Authentication required");
  if(string.IsNullOrWhiteSpace(actor.FindFirst("sub")?.Value))
   return Results.Problem(statusCode:403,title:"Verified OIDC subject required");
  if(string.IsNullOrWhiteSpace(centerId)||templateId==Guid.Empty)
   return Results.Problem(statusCode:400,title:"Invalid private template identity");
  return null;
 }
 private static async Task<bool> Allowed(ClaimsPrincipal actor,string centerId,
  ICenterPrivatePageTemplateAuthorization grant,ICenterSiteDraftAuthorization pages,
  string op,CancellationToken ct)
 {
  try
  {
   return await grant.IsAuthorizedAsync(actor,centerId,op,ct)
    && await pages.IsAuthorizedAsync(actor,centerId,Guid.Empty,"SITE_DRAFT_LIST_READ",ct);
  }
  catch(OperationCanceledException) when(ct.IsCancellationRequested){throw;}
  catch{return false;}
 }
 public static async Task<IResult> Append(ClaimsPrincipal actor,IConfiguration config,
  ICenterPrivatePageTemplateAuthorization grant,ICenterSiteDraftAuthorization pages,
  string centerId,AppendPrivatePageTemplateRequest? request,CancellationToken ct)
 {
  var invalid=Identity(actor,centerId,request?.TemplateId);if(invalid is not null)return invalid;
  if(request is null||request.SourcePageId==Guid.Empty||request.TemplateId==Guid.Empty||
   request.ExpectedRevision<0||request.PinnedPageRevision<=0||request.Label is null||
   string.IsNullOrWhiteSpace(request.Label)||request.Label.Length>160)
   return Results.Problem(statusCode:400,title:"Invalid source-pinned private template");
  if(!await Allowed(actor,centerId,grant,pages,"PRIVATE_PAGE_TEMPLATE_APPEND",ct))
   return Results.Problem(statusCode:403,title:"Separate template and source-page grants required");
  var url=config["CENTER_PRIVATE_TEMPLATE_WRITE_DATABASE_URL"];
  if(string.IsNullOrWhiteSpace(url))
   return Results.Problem(statusCode:503,title:"Private template writer unavailable");
  try
  {
   await using var db=new NpgsqlConnection(url);await db.OpenAsync(ct);
   await using var tx=await db.BeginTransactionAsync(ct);
   if(!await RestrictedPrivatePageTemplateRole.WriterAsync(db,tx,ct))
    return Results.Problem(statusCode:503,title:"Unsafe private template writer");
   await using(var scope=new NpgsqlCommand("SELECT set_config('kanoonyar.center_id',@center,true)",db,tx))
   {scope.Parameters.AddWithValue("center",centerId);await scope.ExecuteNonQueryAsync(ct);}
   await using var cmd=new NpgsqlCommand("""
    SELECT web.append_center_private_page_template(@center,@template,@page,@expected,
      @pin,@label,@actor,@audit,@event,@correlation)
    """,db,tx);
   cmd.Parameters.AddWithValue("center",centerId);
   cmd.Parameters.AddWithValue("template",request.TemplateId);
   cmd.Parameters.AddWithValue("page",request.SourcePageId);
   cmd.Parameters.AddWithValue("expected",request.ExpectedRevision);
   cmd.Parameters.AddWithValue("pin",request.PinnedPageRevision);
   cmd.Parameters.AddWithValue("label",request.Label.Trim());
   cmd.Parameters.AddWithValue("actor",actor.FindFirst("sub")!.Value);
   cmd.Parameters.AddWithValue("audit",Guid.NewGuid());
   cmd.Parameters.AddWithValue("event",Guid.NewGuid());
   cmd.Parameters.AddWithValue("correlation",Guid.NewGuid().ToString("N"));
   if(await cmd.ExecuteScalarAsync(ct) is not long version||
     version!=request.ExpectedRevision+1)
    throw new InvalidOperationException("Invalid template acknowledgement");
   await tx.CommitAsync(ct);
   return Results.Ok(new{templateId=request.TemplateId,sourcePageId=request.SourcePageId,
    revision=version,pinnedPageRevision=request.PinnedPageRevision,
    visibility="PRIVATE_TEMPLATE",isPublished=false,isShared=false});
  }
  catch(PostgresException e) when(e.SqlState is "40001" or "23503" or "23505" or "23514")
   {return Results.Problem(statusCode:409,title:"Stale template or missing/foreign source revision");}
  catch(PostgresException e) when(e.SqlState=="42501"&&
   e.MessageText=="Center scope or active membership absent")
   {return Results.Problem(statusCode:403,title:"Live source-center membership required");}
  catch(PostgresException e) when(e.SqlState=="42501")
   {return Results.Problem(statusCode:503,title:"Restricted template storage unavailable");}
  catch(PostgresException e) when(e.SqlState=="22023")
   {return Results.Problem(statusCode:400,title:"Invalid template request");}
  catch(NpgsqlException){return Results.Problem(statusCode:503,title:"Template storage unavailable");}
  catch(TimeoutException){return Results.Problem(statusCode:503,title:"Template storage unavailable");}
 }
 public static Task<IResult> List(ClaimsPrincipal actor,IConfiguration config,
  ICenterPrivatePageTemplateAuthorization grant,ICenterSiteDraftAuthorization pages,
  string centerId,Guid? beforeTemplateId,CancellationToken ct)=>
  Read(actor,config,grant,pages,centerId,null,beforeTemplateId,null,ct);
 public static Task<IResult> History(ClaimsPrincipal actor,IConfiguration config,
  ICenterPrivatePageTemplateAuthorization grant,ICenterSiteDraftAuthorization pages,
  string centerId,Guid templateId,long? beforeRevision,CancellationToken ct)=>
  Read(actor,config,grant,pages,centerId,templateId,null,beforeRevision,ct);
 private static async Task<IResult> Read(ClaimsPrincipal actor,IConfiguration config,
  ICenterPrivatePageTemplateAuthorization grant,ICenterSiteDraftAuthorization pages,
  string centerId,Guid? templateId,Guid? beforeId,long? beforeRevision,CancellationToken ct)
 {
  var invalid=Identity(actor,centerId,templateId);if(invalid is not null)return invalid;
  if(beforeId==Guid.Empty||(beforeRevision.HasValue&&beforeRevision.Value<=0))
   return Results.Problem(statusCode:400,title:"Invalid private template cursor");
  var op=templateId.HasValue?"PRIVATE_PAGE_TEMPLATE_HISTORY_READ":"PRIVATE_PAGE_TEMPLATE_LIST_READ";
  if(!await Allowed(actor,centerId,grant,pages,op,ct))
   return Results.Problem(statusCode:403,title:"Separate template and source-page read grants required");
  var url=config["CENTER_DRAFT_READ_DATABASE_URL"];
  if(string.IsNullOrWhiteSpace(url))
   return Results.Problem(statusCode:503,title:"Private template reader unavailable");
  try
  {
   await using var db=new NpgsqlConnection(url);await db.OpenAsync(ct);
   await using var tx=await db.BeginTransactionAsync(IsolationLevel.RepeatableRead,ct);
   if(!await RestrictedPrivatePageTemplateRole.ReaderAsync(db,tx,ct))
    return Results.Problem(statusCode:503,title:"Unsafe private template reader");
   await using(var scope=new NpgsqlCommand("SELECT set_config('kanoonyar.center_id',@center,true)",db,tx))
   {scope.Parameters.AddWithValue("center",centerId);await scope.ExecuteNonQueryAsync(ct);}
   await using(var member=new NpgsqlCommand("""
    SELECT EXISTS(SELECT 1 FROM centers.memberships m JOIN centers.registry c
     ON c.center_id=m.center_id WHERE m.center_id=@center AND m.subject_id=@actor
      AND m.is_active AND c.is_active)
    """,db,tx))
   {
    member.Parameters.AddWithValue("center",centerId);
    member.Parameters.AddWithValue("actor",actor.FindFirst("sub")!.Value);
    if(await member.ExecuteScalarAsync(ct) is not true)
     return Results.Problem(statusCode:403,title:"Live center membership required");
   }
   if(templateId.HasValue)
   {
    Guid source;
    await using(var identity=new NpgsqlCommand("""
     SELECT source_page_id FROM web.center_private_page_templates
      WHERE center_id=@center AND template_id=@template
     """,db,tx))
    {
     identity.Parameters.AddWithValue("center",centerId);
     identity.Parameters.AddWithValue("template",templateId.Value);
     var raw=await identity.ExecuteScalarAsync(ct);
     if(raw is not Guid value)
      return Results.Problem(statusCode:404,title:"Private template not found for center");
     source=value;
    }
    await using var command=new NpgsqlCommand("""
     SELECT v.revision,v.pinned_page_revision,v.template_label,p.slug,d.title,
       d.body_text,v.recorded_at
     FROM web.center_private_page_template_revisions v
     JOIN web.page_draft_revisions d
       ON d.center_id=v.center_id AND d.page_id=v.source_page_id
       AND d.revision=v.pinned_page_revision
     JOIN web.center_pages p ON p.center_id=v.center_id AND p.page_id=v.source_page_id
     WHERE v.center_id=@center AND v.template_id=@template
       AND (@before::bigint IS NULL OR v.revision<@before::bigint)
     ORDER BY v.revision DESC LIMIT 21
     """,db,tx);
    command.Parameters.AddWithValue("center",centerId);
    command.Parameters.AddWithValue("template",templateId.Value);
    command.Parameters.Add("before",NpgsqlDbType.Bigint).Value=
     beforeRevision.HasValue?(object)beforeRevision.Value:DBNull.Value;
    var history=new List<object>();long? cursor=null;int seen=0;
    await using(var rdr=await command.ExecuteReaderAsync(ct))
    {
     while(await rdr.ReadAsync(ct))
     {
      if(seen<20)
      {
       cursor=rdr.GetInt64(0);
       history.Add(new{revision=cursor,pinnedPageRevision=rdr.GetInt64(1),
        label=rdr.GetString(2),sourceSlug=rdr.GetString(3),
        title=rdr.GetString(4),bodyText=rdr.GetString(5),
        recordedAt=rdr.GetDateTime(6),visibility="PRIVATE_TEMPLATE",
        isPublished=false,isShared=false});
      }
      seen++;
     }
    }
    await tx.CommitAsync(ct);
    return Results.Ok(new{templateId=templateId.Value,sourcePageId=source,
     revisions=history,nextBeforeRevision=seen>20?cursor:null,
     visibility="PRIVATE_TEMPLATE",isPublished=false,isShared=false});
   }
   await using var query=new NpgsqlCommand("""
    SELECT t.template_id,t.source_page_id,v.revision,v.pinned_page_revision,
      v.template_label,p.slug,d.title,d.body_text,v.recorded_at
    FROM web.center_private_page_templates t
    JOIN LATERAL(SELECT revision,pinned_page_revision,template_label,recorded_at
      FROM web.center_private_page_template_revisions r
      WHERE r.center_id=t.center_id AND r.template_id=t.template_id
      ORDER BY revision DESC LIMIT 1) v ON true
    JOIN web.page_draft_revisions d ON d.center_id=t.center_id
      AND d.page_id=t.source_page_id AND d.revision=v.pinned_page_revision
    JOIN web.center_pages p ON p.center_id=t.center_id AND p.page_id=t.source_page_id
    WHERE t.center_id=@center AND (@before::uuid IS NULL OR t.template_id<@before::uuid)
    ORDER BY t.template_id DESC LIMIT 21
    """,db,tx);
   query.Parameters.AddWithValue("center",centerId);
   query.Parameters.Add("before",NpgsqlDbType.Uuid).Value=
    beforeId.HasValue?(object)beforeId.Value:DBNull.Value;
   var records=new List<object>();Guid? next=null;int total=0;
   await using(var rdr=await query.ExecuteReaderAsync(ct))
   {
    while(await rdr.ReadAsync(ct))
    {
     if(total<20)
     {
      next=rdr.GetGuid(0);
      records.Add(new{templateId=next,sourcePageId=rdr.GetGuid(1),
       revision=rdr.GetInt64(2),pinnedPageRevision=rdr.GetInt64(3),
       label=rdr.GetString(4),sourceSlug=rdr.GetString(5),
       title=rdr.GetString(6),bodyText=rdr.GetString(7),
       recordedAt=rdr.GetDateTime(8),visibility="PRIVATE_TEMPLATE",
       isPublished=false,isShared=false});
     }
     total++;
    }
   }
   await tx.CommitAsync(ct);
   return Results.Ok(new{templates=records,nextBeforeTemplateId=total>20?next:null,
    visibility="PRIVATE_TEMPLATE",isPublished=false,isShared=false});
  }
  catch(NpgsqlException){return Results.Problem(statusCode:503,title:"Private template read unavailable");}
  catch(TimeoutException){return Results.Problem(statusCode:503,title:"Private template read unavailable");}
 }
}
