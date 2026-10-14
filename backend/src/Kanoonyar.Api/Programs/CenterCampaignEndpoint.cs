using System.Security.Claims;
using System.Text.Json;
using System.Text.RegularExpressions;
using Kanoonyar.Api.Security;
using Npgsql;
using NpgsqlTypes;

namespace Kanoonyar.Api.Programs;

public sealed record PlannedCampaignActivity(string ActivityKey,string Title);
public sealed record AppendCenterCampaignRequest(Guid CampaignId,long ExpectedRevision,string Slug,
 Guid DraftId,long PinnedPlanRevision,string Title,string GoalText,
 string AudienceDescription,List<PlannedCampaignActivity> PlannedActivities);
public interface ICenterCampaignAuthorization
{
 ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal actor,string centerId,Guid campaignId,
  string operation,CancellationToken ct);
}
public sealed class UnapprovedCenterCampaignAuthorization:ICenterCampaignAuthorization
{
 public ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal actor,string centerId,Guid campaignId,
  string operation,CancellationToken ct)=>ValueTask.FromResult(false);
}
public static class CenterCampaignEndpoint
{
 private static readonly Regex Slug=new("^[a-z0-9]+(-[a-z0-9]+)*$",
   RegexOptions.CultureInvariant|RegexOptions.Compiled);
 private static readonly Regex ActivityKey=new("^[a-z][a-z0-9_]{0,31}$",
   RegexOptions.CultureInvariant|RegexOptions.Compiled);
 private static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web);
 private static IResult? Identity(ClaimsPrincipal actor,string center,Guid? campaign=null)
 {
  if(actor.Identity?.IsAuthenticated!=true)return Results.Problem(statusCode:401,title:"Authentication required");
  if(string.IsNullOrWhiteSpace(actor.FindFirst("sub")?.Value))
   return Results.Problem(statusCode:403,title:"Verified OIDC subject required");
  if(string.IsNullOrWhiteSpace(center)||campaign==Guid.Empty)
   return Results.Problem(statusCode:400,title:"Invalid campaign scope");
  return null;
 }
 private static async Task<bool> Granted(ICenterCampaignAuthorization authority,
  ClaimsPrincipal actor,string center,Guid id,string operation,CancellationToken ct)
 {
  try{return await authority.IsAuthorizedAsync(actor,center,id,operation,ct);}
  catch(OperationCanceledException) when(ct.IsCancellationRequested){throw;}
  catch{return false;}
 }
 private static bool Valid(AppendCenterCampaignRequest? b)
 {
  if(b is null||b.CampaignId==Guid.Empty||b.DraftId==Guid.Empty||b.ExpectedRevision<0||
    b.PinnedPlanRevision<1||b.Slug is null||b.Slug.Length>80||!Slug.IsMatch(b.Slug)||
    b.Title is null||b.Title.Trim().Length is <1 or >200||
    b.GoalText is null||b.GoalText.Trim().Length is <1 or >500||
    b.AudienceDescription is null||b.AudienceDescription.Trim().Length is <1 or >240||
    b.PlannedActivities is null||b.PlannedActivities.Count is <1 or >12)return false;
  var keys=new HashSet<string>(StringComparer.Ordinal);
  foreach(var item in b.PlannedActivities)
   if(item is null||item.ActivityKey is null||!ActivityKey.IsMatch(item.ActivityKey)||
     !keys.Add(item.ActivityKey)||item.Title is null||item.Title.Trim().Length is <1 or >160)
      return false;
  return true;
 }
 public static async Task<IResult> Append(ClaimsPrincipal actor,IConfiguration config,
  ICenterCampaignAuthorization auth,string centerId,AppendCenterCampaignRequest? body,CancellationToken ct)
 {
  var invalid=Identity(actor,centerId,body?.CampaignId??Guid.Empty);
  if(invalid is not null)return invalid;
  if(!Valid(body))return Results.Problem(statusCode:400,title:"Invalid planned campaign draft");
  var b=body!;
  if(!await Granted(auth,actor,centerId,b.CampaignId,"CAMPAIGN_DRAFT_APPEND",ct))
   return Results.Problem(statusCode:403,title:"Independent campaign authoring grant required");
  var connection=config["CENTER_CAMPAIGN_WRITE_DATABASE_URL"];
  if(string.IsNullOrWhiteSpace(connection))
   return Results.Problem(statusCode:503,title:"Campaign draft command unavailable");
  try
  {
   await using var db=new NpgsqlConnection(connection);await db.OpenAsync(ct);
   await using var tx=await db.BeginTransactionAsync(ct);
   if(!await RestrictedCenterCampaignRole.WriterAsync(db,tx,ct))
    return Results.Problem(statusCode:503,title:"Unsafe campaign writer role");
   await using(var scope=new NpgsqlCommand("SELECT set_config('kanoonyar.center_id',@center,true)",db,tx))
   {scope.Parameters.AddWithValue("center",centerId);await scope.ExecuteNonQueryAsync(ct);}
   await using var q=new NpgsqlCommand(
    """
    SELECT campaigns.append_center_campaign(@center,@campaign,@expected,@slug,@draft,@pinned,
     @title,@goal,@audience,@activities,@actor,@audit,@event,@correlation)
    """,db,tx);
   q.Parameters.AddWithValue("center",centerId);
   q.Parameters.AddWithValue("campaign",b.CampaignId);
   q.Parameters.AddWithValue("expected",b.ExpectedRevision);
   q.Parameters.AddWithValue("slug",b.Slug);
   q.Parameters.AddWithValue("draft",b.DraftId);
   q.Parameters.AddWithValue("pinned",b.PinnedPlanRevision);
   q.Parameters.AddWithValue("title",b.Title.Trim());
   q.Parameters.AddWithValue("goal",b.GoalText.Trim());
   q.Parameters.AddWithValue("audience",b.AudienceDescription.Trim());
   q.Parameters.Add("activities",NpgsqlDbType.Jsonb).Value=JsonSerializer.Serialize(b.PlannedActivities,Json);
   q.Parameters.AddWithValue("actor",actor.FindFirst("sub")!.Value);
   q.Parameters.AddWithValue("audit",Guid.NewGuid());
   q.Parameters.AddWithValue("event",Guid.NewGuid());
   q.Parameters.AddWithValue("correlation",Guid.NewGuid().ToString("N"));
   if(await q.ExecuteScalarAsync(ct) is not long revision||revision!=b.ExpectedRevision+1)
    throw new InvalidOperationException("Campaign revision was not persisted");
   await tx.CommitAsync(ct);
   return Results.Ok(new{campaignId=b.CampaignId,draftId=b.DraftId,pinnedPlanRevision=b.PinnedPlanRevision,
     slug=b.Slug,revision,visibility="PRIVATE_DRAFT",isPublished=false,isExecuted=false});
  }
  catch(PostgresException x) when(x.SqlState is "40001" or "23503" or "23505" or "23514")
   {return Results.Problem(statusCode:409,title:"Stale campaign or invalid pinned plan");}
  catch(PostgresException x) when(x.SqlState=="42501"&&x.MessageText=="Center scope or active membership absent")
   {return Results.Problem(statusCode:403,title:"Live center membership required");}
  catch(PostgresException x) when(x.SqlState=="42501")
   {return Results.Problem(statusCode:503,title:"Campaign SQL permissions unsafe");}
  catch(PostgresException x) when(x.SqlState=="22023")
   {return Results.Problem(statusCode:400,title:"Invalid bounded campaign");}
  catch(NpgsqlException){return Results.Problem(statusCode:503,title:"Campaign storage unavailable");}
  catch(TimeoutException){return Results.Problem(statusCode:503,title:"Campaign storage unavailable");}
 }
 public static Task<IResult> List(ClaimsPrincipal actor,IConfiguration config,
  ICenterCampaignAuthorization auth,string centerId,Guid? beforeCampaignId,CancellationToken ct)=>
  Read(actor,config,auth,centerId,null,beforeCampaignId,null,ct);
 public static Task<IResult> History(ClaimsPrincipal actor,IConfiguration config,
  ICenterCampaignAuthorization auth,string centerId,Guid campaignId,long? beforeRevision,CancellationToken ct)=>
  Read(actor,config,auth,centerId,campaignId,null,beforeRevision,ct);
 private static async Task<IResult> Read(ClaimsPrincipal actor,IConfiguration config,
  ICenterCampaignAuthorization auth,string centerId,Guid? campaignId,
  Guid? beforeId,long? beforeRevision,CancellationToken ct)
 {
  var invalid=Identity(actor,centerId,campaignId);
  if(invalid is not null)return invalid;
  if(beforeId==Guid.Empty||(beforeRevision.HasValue&&beforeRevision.Value<=0))
   return Results.Problem(statusCode:400,title:"Invalid campaign cursor");
  if(!await Granted(auth,actor,centerId,campaignId??Guid.Empty,
    campaignId.HasValue?"CAMPAIGN_DRAFT_HISTORY_READ":"CAMPAIGN_DRAFT_LIST_READ",ct))
   return Results.Problem(statusCode:403,title:"Independent campaign read grant required");
  var connection=config["CENTER_DRAFT_READ_DATABASE_URL"];
  if(string.IsNullOrWhiteSpace(connection))
   return Results.Problem(statusCode:503,title:"Private campaign read unavailable");
  try
  {
   await using var db=new NpgsqlConnection(connection);await db.OpenAsync(ct);
   await using var tx=await db.BeginTransactionAsync(ct);
   if(!await RestrictedCenterCampaignRole.ReaderAsync(db,tx,ct))
    return Results.Problem(statusCode:503,title:"Unsafe campaign reader role");
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
     return Results.Problem(statusCode:403,title:"Live center membership required");
   }
   if(campaignId.HasValue)
   {
    Guid draft;long pinned;string? slug;
    await using(var find=new NpgsqlCommand(
      "SELECT slug,draft_id,pinned_plan_revision FROM campaigns.center_campaigns WHERE center_id=@center AND campaign_id=@campaign",db,tx))
    {
     find.Parameters.AddWithValue("center",centerId);
     find.Parameters.AddWithValue("campaign",campaignId.Value);
     await using var reader=await find.ExecuteReaderAsync(ct);
     if(!await reader.ReadAsync(ct))
      return Results.Problem(statusCode:404,title:"Campaign not found in authorized center");
     slug=reader.GetString(0);draft=reader.GetGuid(1);pinned=reader.GetInt64(2);
    }
    await using var cmd=new NpgsqlCommand(
     """
     SELECT revision,title,goal_text,audience_description,planned_activities::text,recorded_at
     FROM campaigns.center_campaign_revisions
     WHERE center_id=@center AND campaign_id=@campaign
      AND (@before::bigint IS NULL OR revision<@before::bigint)
     ORDER BY revision DESC LIMIT 21
     """,db,tx);
    cmd.Parameters.AddWithValue("center",centerId);
    cmd.Parameters.AddWithValue("campaign",campaignId.Value);
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
         goalText=reader.GetString(2),audienceDescription=reader.GetString(3),
         plannedActivities=JsonSerializer.Deserialize<List<PlannedCampaignActivity>>(reader.GetString(4),Json),
         recordedAt=reader.GetDateTime(5),visibility="PRIVATE_DRAFT",isPublished=false,isExecuted=false});
      }
      count++;
     }
    }
    await tx.CommitAsync(ct);
    return Results.Ok(new{campaignId=campaignId.Value,slug,draftId=draft,
      pinnedPlanRevision=pinned,revisions=rows,nextBeforeRevision=count>20?cursor:null});
   }
   await using var list=new NpgsqlCommand(
    """
    SELECT c.campaign_id,c.slug,c.draft_id,c.pinned_plan_revision,
      v.revision,v.title,v.goal_text,v.audience_description,v.planned_activities::text,v.recorded_at
    FROM campaigns.center_campaigns c
    JOIN LATERAL(
      SELECT revision,title,goal_text,audience_description,planned_activities,recorded_at
      FROM campaigns.center_campaign_revisions WHERE center_id=c.center_id
        AND campaign_id=c.campaign_id ORDER BY revision DESC LIMIT 1
    ) v ON true
    WHERE c.center_id=@center AND (@before::uuid IS NULL OR c.campaign_id<@before::uuid)
    ORDER BY c.campaign_id DESC LIMIT 21
    """,db,tx);
   list.Parameters.AddWithValue("center",centerId);
   list.Parameters.Add("before",NpgsqlDbType.Uuid).Value=beforeId.HasValue?(object)beforeId.Value:DBNull.Value;
   var campaigns=new List<object>();Guid? next=null;int countList=0;
   await using(var reader=await list.ExecuteReaderAsync(ct))
   {
    while(await reader.ReadAsync(ct))
    {
     if(countList<20)
     {
      next=reader.GetGuid(0);
      campaigns.Add(new{campaignId=reader.GetGuid(0),slug=reader.GetString(1),
       draftId=reader.GetGuid(2),pinnedPlanRevision=reader.GetInt64(3),
       revision=reader.GetInt64(4),title=reader.GetString(5),
       goalText=reader.GetString(6),audienceDescription=reader.GetString(7),
       plannedActivities=JsonSerializer.Deserialize<List<PlannedCampaignActivity>>(reader.GetString(8),Json),
       recordedAt=reader.GetDateTime(9),visibility="PRIVATE_DRAFT",isPublished=false,isExecuted=false});
     }
     countList++;
    }
   }
   await tx.CommitAsync(ct);
   return Results.Ok(new{campaigns,nextBeforeCampaignId=countList>20?next:null});
  }
  catch(NpgsqlException){return Results.Problem(statusCode:503,title:"Private campaigns unavailable");}
  catch(TimeoutException){return Results.Problem(statusCode:503,title:"Private campaigns unavailable");}
 }
}
