using System.Security.Claims;
using System.Text.Json;
using System.Text.RegularExpressions;
using Kanoonyar.Api.Security;
using Npgsql;
using NpgsqlTypes;

namespace Kanoonyar.Api.Programs;

public sealed record FormDraftField(string Key,string Label,string Kind,bool Required,string[] Options);
public sealed record AppendCenterFormDraftRequest(Guid FormId,long ExpectedRevision,string Slug,
 string Title,List<FormDraftField> Fields);

/// <summary>Independent, revocable center+resource+operation permission; default deny.</summary>
public interface ICenterFormDraftAuthorization
{
 ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal actor,string centerId,Guid formId,
  string operation,CancellationToken ct);
}
public sealed class UnapprovedCenterFormDraftAuthorization:ICenterFormDraftAuthorization
{
 public ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal actor,string centerId,Guid formId,
  string operation,CancellationToken ct)=>ValueTask.FromResult(false);
}

public static class CenterFormDraftEndpoint
{
 private static readonly Regex Slug=new("^[a-z0-9]+(-[a-z0-9]+)*$",
   RegexOptions.CultureInvariant|RegexOptions.Compiled);
 private static readonly Regex FieldKey=new("^[a-z][a-z0-9_]{0,31}$",
   RegexOptions.CultureInvariant|RegexOptions.Compiled);
 private static readonly HashSet<string> Kinds=new(StringComparer.Ordinal)
   {"SHORT_TEXT","LONG_TEXT","SINGLE_SELECT"};
 private static readonly JsonSerializerOptions JsonOptions=new(JsonSerializerDefaults.Web);

 private static IResult? Identity(ClaimsPrincipal actor,string center,Guid? formId=null)
 {
  if(actor.Identity?.IsAuthenticated!=true)
    return Results.Problem(statusCode:401,title:"Authentication required");
  if(string.IsNullOrWhiteSpace(actor.FindFirst("sub")?.Value))
    return Results.Problem(statusCode:403,title:"Verified subject required");
  if(string.IsNullOrWhiteSpace(center)||formId==Guid.Empty)
    return Results.Problem(statusCode:400,title:"Invalid center/form identity");
  return null;
 }
 private static async Task<bool> Grant(ICenterFormDraftAuthorization authorization,
  ClaimsPrincipal actor,string center,Guid formId,string operation,CancellationToken ct)
 {
  try{return await authorization.IsAuthorizedAsync(actor,center,formId,operation,ct);}
  catch(OperationCanceledException) when(ct.IsCancellationRequested){throw;}
  catch{return false;}
 }
 private static bool FieldsValid(List<FormDraftField>? fields)
 {
  if(fields is null||fields.Count is <1 or >12)return false;
  var keys=new HashSet<string>(StringComparer.Ordinal);
  foreach(var field in fields)
  {
   if(field is null||field.Key is null||!FieldKey.IsMatch(field.Key)
      ||!keys.Add(field.Key)||field.Label is null||field.Label.Trim().Length is <1 or >120
      ||field.Kind is null||!Kinds.Contains(field.Kind)||field.Options is null)return false;
   if(field.Kind=="SINGLE_SELECT")
   {
    if(field.Options.Length is <2 or >8 || field.Options.Any(x=>x is null
        ||x.Trim().Length is <1 or >80)||
       field.Options.Distinct(StringComparer.Ordinal).Count()!=field.Options.Length)
      return false;
   }
   else if(field.Options.Length!=0)return false;
  }
  return true;
 }

 public static async Task<IResult> Append(ClaimsPrincipal actor,IConfiguration config,
  ICenterFormDraftAuthorization authorization,string centerId,
  AppendCenterFormDraftRequest? body,CancellationToken ct)
 {
  var invalid=Identity(actor,centerId,body?.FormId??Guid.Empty);
  if(invalid is not null)return invalid;
  if(body is null||body.ExpectedRevision<0||body.Slug is null||body.Slug.Length>80
      ||!Slug.IsMatch(body.Slug)||body.Title is null||
      body.Title.Trim().Length is <1 or >200 ||!FieldsValid(body.Fields))
    return Results.Problem(statusCode:400,title:"Invalid private form definition");
  if(!await Grant(authorization,actor,centerId,body.FormId,"FORM_DRAFT_APPEND",ct))
    return Results.Problem(statusCode:403,title:"Independent form authoring grant required");
  var url=config["CENTER_FORM_DRAFT_WRITE_DATABASE_URL"];
  if(string.IsNullOrWhiteSpace(url))
    return Results.Problem(statusCode:503,title:"Private form authoring unavailable");
  try
  {
   await using var db=new NpgsqlConnection(url);await db.OpenAsync(ct);
   await using var tx=await db.BeginTransactionAsync(ct);
   if(!await RestrictedCenterFormRole.WriterAsync(db,tx,ct))
      return Results.Problem(statusCode:503,title:"Unsafe form authoring SQL role");
   await using(var scope=new NpgsqlCommand("SELECT set_config('kanoonyar.center_id',@center,true)",db,tx))
   {scope.Parameters.AddWithValue("center",centerId);await scope.ExecuteNonQueryAsync(ct);}
   await using var cmd=new NpgsqlCommand(
    """
    SELECT web.append_center_form_draft(
      @center,@form,@expected,@slug,@title,@fields,@actor,@audit,@event,@correlation)
    """,db,tx);
   cmd.Parameters.AddWithValue("center",centerId);
   cmd.Parameters.AddWithValue("form",body.FormId);
   cmd.Parameters.AddWithValue("expected",body.ExpectedRevision);
   cmd.Parameters.AddWithValue("slug",body.Slug);
   cmd.Parameters.AddWithValue("title",body.Title.Trim());
   cmd.Parameters.Add("fields",NpgsqlDbType.Jsonb).Value=JsonSerializer.Serialize(body.Fields,JsonOptions);
   cmd.Parameters.AddWithValue("actor",actor.FindFirst("sub")!.Value);
   cmd.Parameters.AddWithValue("audit",Guid.NewGuid());
   cmd.Parameters.AddWithValue("event",Guid.NewGuid());
   cmd.Parameters.AddWithValue("correlation",Guid.NewGuid().ToString("N"));
   if(await cmd.ExecuteScalarAsync(ct) is not long rev||rev!=body.ExpectedRevision+1)
     throw new InvalidOperationException("Form revision not persisted");
   await tx.CommitAsync(ct);
   return Results.Ok(new{formId=body.FormId,slug=body.Slug,revision=rev,
     visibility="PRIVATE_DRAFT",isPublished=false,acceptsSubmissions=false});
  }
  catch(PostgresException e) when(e.SqlState is "40001" or "23505" or "23503" or "23514")
  {return Results.Problem(statusCode:409,title:"Stale or duplicate form definition");}
  catch(PostgresException e) when(e.SqlState=="42501"&&
    e.MessageText=="Center scope or active membership absent")
  {return Results.Problem(statusCode:403,title:"Active center membership required");}
  catch(PostgresException e) when(e.SqlState=="42501")
  {return Results.Problem(statusCode:503,title:"Insufficient form SQL privileges");}
  catch(PostgresException e) when(e.SqlState=="22023")
  {return Results.Problem(statusCode:400,title:"Invalid bounded form definition");}
  catch(NpgsqlException){return Results.Problem(statusCode:503,title:"Form definition storage unavailable");}
  catch(TimeoutException){return Results.Problem(statusCode:503,title:"Form definition storage unavailable");}
 }

 public static Task<IResult> List(ClaimsPrincipal actor,IConfiguration config,
  ICenterFormDraftAuthorization authorization,string centerId,Guid? beforeFormId,
  CancellationToken ct)=>Read(actor,config,authorization,centerId,null,beforeFormId,null,ct);
 public static Task<IResult> History(ClaimsPrincipal actor,IConfiguration config,
  ICenterFormDraftAuthorization authorization,string centerId,Guid formId,
  long? beforeRevision,CancellationToken ct)=>
  Read(actor,config,authorization,centerId,formId,null,beforeRevision,ct);
 private static async Task<IResult> Read(ClaimsPrincipal actor,IConfiguration config,
  ICenterFormDraftAuthorization authorization,string centerId,Guid? formId,Guid? beforeFormId,
  long? beforeRevision,CancellationToken ct)
 {
  var invalid=Identity(actor,centerId,formId);
  if(invalid is not null)return invalid;
  if(beforeFormId==Guid.Empty||(beforeRevision.HasValue&&beforeRevision.Value<=0))
     return Results.Problem(statusCode:400,title:"Invalid form paging cursor");
  if(!await Grant(authorization,actor,centerId,formId??Guid.Empty,
       formId.HasValue?"FORM_DRAFT_HISTORY_READ":"FORM_DRAFT_LIST_READ",ct))
     return Results.Problem(statusCode:403,title:"Independent form read grant required");
  var url=config["CENTER_DRAFT_READ_DATABASE_URL"];
  if(string.IsNullOrWhiteSpace(url))
     return Results.Problem(statusCode:503,title:"Private form read unavailable");
  try
  {
   await using var db=new NpgsqlConnection(url);await db.OpenAsync(ct);
   await using var tx=await db.BeginTransactionAsync(ct);
   if(!await RestrictedCenterFormRole.ReaderAsync(db,tx,ct))
     return Results.Problem(statusCode:503,title:"Unsafe form read SQL role");
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
     return Results.Problem(statusCode:403,title:"Live center membership required");
   }
   if(formId.HasValue)
   {
    string? slug;
    await using(var lookup=new NpgsqlCommand(
      "SELECT slug FROM web.center_forms WHERE center_id=@center AND form_id=@form",db,tx))
    {
     lookup.Parameters.AddWithValue("center",centerId);
     lookup.Parameters.AddWithValue("form",formId.Value);
     slug=await lookup.ExecuteScalarAsync(ct) as string;
    }
    if(slug is null)return Results.Problem(statusCode:404,title:"Form not found in authorized center");
    await using var cmd=new NpgsqlCommand(
     """
     SELECT revision,title,fields_definition::text,recorded_at
     FROM web.center_form_draft_revisions
     WHERE center_id=@center AND form_id=@form
       AND (@before::bigint IS NULL OR revision<@before::bigint)
     ORDER BY revision DESC LIMIT 21
     """,db,tx);
    cmd.Parameters.AddWithValue("center",centerId);
    cmd.Parameters.AddWithValue("form",formId.Value);
    cmd.Parameters.Add("before",NpgsqlDbType.Bigint).Value=
       beforeRevision.HasValue?(object)beforeRevision.Value:DBNull.Value;
    var rows=new List<object>();long? cursor=null;int count=0;
    await using(var reader=await cmd.ExecuteReaderAsync(ct))
    {
     while(await reader.ReadAsync(ct))
     {
      if(count<20)
      {
       cursor=reader.GetInt64(0);
       rows.Add(new{revision=reader.GetInt64(0),title=reader.GetString(1),
        fields=JsonSerializer.Deserialize<List<FormDraftField>>(reader.GetString(2),JsonOptions),
        recordedAt=reader.GetDateTime(3),visibility="PRIVATE_DRAFT",
        isPublished=false,acceptsSubmissions=false});
      }
      count++;
     }
    }
    await tx.CommitAsync(ct);
    return Results.Ok(new{formId=formId.Value,slug,revisions=rows,
      nextBeforeRevision=count>20?cursor:null});
   }
   await using var list=new NpgsqlCommand(
    """
    SELECT f.form_id,f.slug,v.revision,v.title,v.fields_definition::text,v.recorded_at
    FROM web.center_forms f
    JOIN LATERAL (
      SELECT revision,title,fields_definition,recorded_at FROM web.center_form_draft_revisions
      WHERE center_id=f.center_id AND form_id=f.form_id ORDER BY revision DESC LIMIT 1
    ) v ON true
    WHERE f.center_id=@center AND (@before::uuid IS NULL OR f.form_id<@before::uuid)
    ORDER BY f.form_id DESC LIMIT 21
    """,db,tx);
   list.Parameters.AddWithValue("center",centerId);
   list.Parameters.Add("before",NpgsqlDbType.Uuid).Value=
     beforeFormId.HasValue?(object)beforeFormId.Value:DBNull.Value;
   var forms=new List<object>();Guid? next=null;int n=0;
   await using(var reader=await list.ExecuteReaderAsync(ct))
   {
    while(await reader.ReadAsync(ct))
    {
     if(n<20)
     {
      next=reader.GetGuid(0);
      forms.Add(new{formId=reader.GetGuid(0),slug=reader.GetString(1),
       revision=reader.GetInt64(2),title=reader.GetString(3),
       fields=JsonSerializer.Deserialize<List<FormDraftField>>(reader.GetString(4),JsonOptions),
       recordedAt=reader.GetDateTime(5),visibility="PRIVATE_DRAFT",
       isPublished=false,acceptsSubmissions=false});
     }
     n++;
    }
   }
   await tx.CommitAsync(ct);
   return Results.Ok(new{forms,nextBeforeFormId=n>20?next:null});
  }
  catch(NpgsqlException){return Results.Problem(statusCode:503,title:"Private form read unavailable");}
  catch(TimeoutException){return Results.Problem(statusCode:503,title:"Private form read unavailable");}
 }
}
