using System.Security.Claims;
using Kanoonyar.Api.Security;
using Npgsql;
using NpgsqlTypes;

namespace Kanoonyar.Api.Programs;

public sealed record AppendCenterSiteDesignRequest(long ExpectedRevision,string LayoutKey,
 string PaletteKey,string TypeKey,string HeaderText,string FooterText,bool ShowIntro,bool ShowFooter);

public interface ICenterSiteDesignAuthorization
{
 ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal actor,string centerId,string operation,CancellationToken ct);
}
public sealed class UnapprovedCenterSiteDesignAuthorization:ICenterSiteDesignAuthorization
{
 public ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal actor,string centerId,string operation,
  CancellationToken ct)=>ValueTask.FromResult(false);
}
public static class CenterSiteDesignEndpoint
{
 private static readonly HashSet<string> Layouts=new(StringComparer.Ordinal){"EDITORIAL","HERO","COMPACT"};
 private static readonly HashSet<string> Palettes=new(StringComparer.Ordinal){"OCEAN","FOREST","EARTH"};
 private static readonly HashSet<string> Types=new(StringComparer.Ordinal){"SYSTEM","SERIF"};
 private static IResult? Authn(ClaimsPrincipal actor,string center)
 {
  if(actor.Identity?.IsAuthenticated!=true)
   return Results.Problem(statusCode:401,title:"Authentication required");
  if(string.IsNullOrWhiteSpace(actor.FindFirst("sub")?.Value))
   return Results.Problem(statusCode:403,title:"Verified subject required");
  if(string.IsNullOrWhiteSpace(center))
   return Results.Problem(statusCode:400,title:"Invalid center");
  return null;
 }
 private static async Task<bool> Grant(ICenterSiteDesignAuthorization grant,
  ClaimsPrincipal actor,string center,string operation,CancellationToken ct)
 {
  try{return await grant.IsAuthorizedAsync(actor,center,operation,ct);}
  catch(OperationCanceledException) when(ct.IsCancellationRequested){throw;}
  catch{return false;}
 }
 public static async Task<IResult> Append(ClaimsPrincipal actor,IConfiguration config,
  ICenterSiteDesignAuthorization grant,string centerId,AppendCenterSiteDesignRequest? body,
  CancellationToken ct)
 {
  var invalid=Authn(actor,centerId);if(invalid is not null)return invalid;
  if(body is null||body.ExpectedRevision<0||
    body.LayoutKey is null||!Layouts.Contains(body.LayoutKey)||
    body.PaletteKey is null||!Palettes.Contains(body.PaletteKey)||
    body.TypeKey is null||!Types.Contains(body.TypeKey)||
    body.HeaderText is null||body.HeaderText.Trim().Length is <1 or >120||
    body.FooterText is null||body.FooterText.Length>240)
   return Results.Problem(statusCode:400,title:"Invalid bounded site design");
  if(!await Grant(grant,actor,centerId,"SITE_DESIGN_APPEND",ct))
   return Results.Problem(statusCode:403,title:"Independent site design write grant required");
  var url=config["CENTER_SITE_DESIGN_WRITE_DATABASE_URL"];
  if(string.IsNullOrWhiteSpace(url))
   return Results.Problem(statusCode:503,title:"Site design command unavailable");
  try
  {
   await using var db=new NpgsqlConnection(url);await db.OpenAsync(ct);
   await using var tx=await db.BeginTransactionAsync(ct);
   if(!await RestrictedSiteDesignRole.WriterAsync(db,tx,ct))
    return Results.Problem(statusCode:503,title:"Unsafe site design writer role");
   await using(var scope=new NpgsqlCommand("SELECT set_config('kanoonyar.center_id',@center,true)",db,tx))
   {scope.Parameters.AddWithValue("center",centerId);await scope.ExecuteNonQueryAsync(ct);}
   await using var cmd=new NpgsqlCommand(
    """
    SELECT web.append_center_site_design(@center,@expected,@layout,@palette,@type,
      @header,@footer,@intro,@showFooter,@actor,@audit,@event,@correlation)
    """,db,tx);
   cmd.Parameters.AddWithValue("center",centerId);
   cmd.Parameters.AddWithValue("expected",body.ExpectedRevision);
   cmd.Parameters.AddWithValue("layout",body.LayoutKey);
   cmd.Parameters.AddWithValue("palette",body.PaletteKey);
   cmd.Parameters.AddWithValue("type",body.TypeKey);
   cmd.Parameters.AddWithValue("header",body.HeaderText.Trim());
   cmd.Parameters.AddWithValue("footer",body.FooterText);
   cmd.Parameters.AddWithValue("intro",body.ShowIntro);
   cmd.Parameters.AddWithValue("showFooter",body.ShowFooter);
   cmd.Parameters.AddWithValue("actor",actor.FindFirst("sub")!.Value);
   cmd.Parameters.AddWithValue("audit",Guid.NewGuid());
   cmd.Parameters.AddWithValue("event",Guid.NewGuid());
   cmd.Parameters.AddWithValue("correlation",Guid.NewGuid().ToString("N"));
   if(await cmd.ExecuteScalarAsync(ct) is not long v||v!=body.ExpectedRevision+1)
    throw new InvalidOperationException("Design revision acknowledgement mismatch");
   await tx.CommitAsync(ct);
   return Results.Ok(new{revision=v,layoutKey=body.LayoutKey,paletteKey=body.PaletteKey,
     typeKey=body.TypeKey,headerText=body.HeaderText.Trim(),footerText=body.FooterText,
     showIntro=body.ShowIntro,showFooter=body.ShowFooter,
     visibility="PRIVATE_DRAFT",isPublished=false});
  }
  catch(PostgresException ex) when(ex.SqlState is "40001" or "23505" or "23503" or "23514")
  {return Results.Problem(statusCode:409,title:"Stale site design revision");}
  catch(PostgresException ex) when(ex.SqlState=="42501"&&ex.MessageText=="Center scope or active membership absent")
  {return Results.Problem(statusCode:403,title:"Active center membership required");}
  catch(PostgresException ex) when(ex.SqlState=="42501")
  {return Results.Problem(statusCode:503,title:"Site design role permissions insufficient");}
  catch(PostgresException ex) when(ex.SqlState=="22023")
  {return Results.Problem(statusCode:400,title:"Invalid private site design");}
  catch(NpgsqlException){return Results.Problem(statusCode:503,title:"Site design command unavailable");}
  catch(TimeoutException){return Results.Problem(statusCode:503,title:"Site design command unavailable");}
 }
 public static Task<IResult> Latest(ClaimsPrincipal actor,IConfiguration config,
  ICenterSiteDesignAuthorization grant,string centerId,CancellationToken ct)=>
  Read(actor,config,grant,centerId,null,ct);
 public static Task<IResult> History(ClaimsPrincipal actor,IConfiguration config,
  ICenterSiteDesignAuthorization grant,string centerId,long? beforeRevision,CancellationToken ct)=>
  Read(actor,config,grant,centerId,beforeRevision,ct,true);
 private static async Task<IResult> Read(ClaimsPrincipal actor,IConfiguration config,
  ICenterSiteDesignAuthorization grant,string centerId,long? before,CancellationToken ct,bool history=false)
 {
  var invalid=Authn(actor,centerId);if(invalid is not null)return invalid;
  if(history&&before.HasValue&&before.Value<=0)
   return Results.Problem(statusCode:400,title:"Invalid design history cursor");
  if(!await Grant(grant,actor,centerId,history?"SITE_DESIGN_HISTORY_READ":"SITE_DESIGN_READ",ct))
   return Results.Problem(statusCode:403,title:"Independent site design read grant required");
  var url=config["CENTER_DRAFT_READ_DATABASE_URL"];
  if(string.IsNullOrWhiteSpace(url))return Results.Problem(statusCode:503,title:"Site design read unavailable");
  try
  {
   await using var db=new NpgsqlConnection(url);await db.OpenAsync(ct);
   await using var tx=await db.BeginTransactionAsync(ct);
   if(!await RestrictedSiteDesignRole.ReaderAsync(db,tx,ct))
    return Results.Problem(statusCode:503,title:"Unsafe site design read role");
   await using(var scope=new NpgsqlCommand("SELECT set_config('kanoonyar.center_id',@center,true)",db,tx))
   {scope.Parameters.AddWithValue("center",centerId);await scope.ExecuteNonQueryAsync(ct);}
   await using(var member=new NpgsqlCommand(
    """
    SELECT EXISTS(SELECT 1 FROM centers.memberships m JOIN centers.registry c
     ON c.center_id=m.center_id WHERE m.center_id=@center AND m.subject_id=@actor
       AND m.is_active AND c.is_active)
    """,db,tx))
   {
    member.Parameters.AddWithValue("center",centerId);
    member.Parameters.AddWithValue("actor",actor.FindFirst("sub")!.Value);
    if(await member.ExecuteScalarAsync(ct) is not true)
     return Results.Problem(statusCode:403,title:"Active center membership required");
   }
   await using var cmd=new NpgsqlCommand(
    """
    SELECT revision,layout_key,palette_key,type_key,header_text,footer_text,
      show_intro,show_footer,recorded_at
    FROM web.center_site_design_revisions
    WHERE center_id=@center AND (@before::bigint IS NULL OR revision<@before::bigint)
    ORDER BY revision DESC LIMIT @limit
    """,db,tx);
   cmd.Parameters.AddWithValue("center",centerId);
   cmd.Parameters.Add("before",NpgsqlDbType.Bigint).Value=before.HasValue?(object)before.Value:DBNull.Value;
   cmd.Parameters.AddWithValue("limit",history?21:1);
   var designs=new List<object>();long? last=null;int n=0;
   await using(var rdr=await cmd.ExecuteReaderAsync(ct))
   {
    while(await rdr.ReadAsync(ct))
    {
     if(n<(history?20:1))
     {
      last=rdr.GetInt64(0);
      designs.Add(new{
       revision=rdr.GetInt64(0),layoutKey=rdr.GetString(1),paletteKey=rdr.GetString(2),
       typeKey=rdr.GetString(3),headerText=rdr.GetString(4),footerText=rdr.GetString(5),
       showIntro=rdr.GetBoolean(6),showFooter=rdr.GetBoolean(7),
       recordedAt=rdr.GetDateTime(8),visibility="PRIVATE_DRAFT",isPublished=false
      });
     }
     n++;
    }
   }
   await tx.CommitAsync(ct);
   if(history)
    return Results.Ok(new{revisions=designs,nextBeforeRevision=n>20?last:null,
      visibility="PRIVATE_DRAFT",isPublished=false});
   return Results.Ok(new{revision=last,design=designs.Count==0?null:designs[0],
     visibility="PRIVATE_DRAFT",isPublished=false});
  }
  catch(NpgsqlException){return Results.Problem(statusCode:503,title:"Site design read unavailable");}
  catch(TimeoutException){return Results.Problem(statusCode:503,title:"Site design read unavailable");}
 }
}
