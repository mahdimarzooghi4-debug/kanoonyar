using System.Security.Claims;
using System.Text.Json;
using Kanoonyar.Api.Programs;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Xunit;

namespace Kanoonyar.Api.Tests;
public sealed class CenterLocalPlanWorkspacePostgresTests
{
 private static string? Admin=>Environment.GetEnvironmentVariable("CENTER_DRAFT_INTEGRATION_ADMIN_URL");
 private static string? Reader=>Environment.GetEnvironmentVariable("CENTER_DRAFT_INTEGRATION_READ_DATABASE_URL");
 private static string? Writer=>Environment.GetEnvironmentVariable("CENTER_WORK_RESOURCE_NEED_INTEGRATION_DATABASE_URL");
 private static bool Enabled()
 {
  if(Environment.GetEnvironmentVariable("CENTER_DRAFT_INTEGRATION_REQUIRED")=="1"){
   Assert.False(string.IsNullOrWhiteSpace(Admin));
   Assert.False(string.IsNullOrWhiteSpace(Reader));
   Assert.False(string.IsNullOrWhiteSpace(Writer));
  }
  return !string.IsNullOrWhiteSpace(Admin)&&!string.IsNullOrWhiteSpace(Reader)&&
   !string.IsNullOrWhiteSpace(Writer);
 }
 private sealed class Plan(bool yes=true):ILocalPlanDraftReadAuthorization
 {
  public ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal a,string c,CancellationToken ct)
   =>ValueTask.FromResult(yes);
 }
 private sealed class Card(bool yes=true,Guid? deniedCard=null):ICenterWorkCardAuthorization
 {
  public ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal a,string c,string op,Guid id,CancellationToken ct)
   =>ValueTask.FromResult(yes&&id!=deniedCard);
 }
 private sealed class Need(bool yes=true,Guid? deniedCard=null):ICenterWorkResourceNeedAuthorization
 {
  public ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal a,string c,Guid id,string op,CancellationToken ct)
   =>ValueTask.FromResult(yes&&id!=deniedCard);
 }
 private static ClaimsPrincipal Actor(string sub)=>new(new ClaimsIdentity(
  [new Claim("sub",sub)],"synthetic"));
 private static IConfiguration Config(string? read=null)=>
  new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{
   ["CENTER_DRAFT_READ_DATABASE_URL"]=read??Reader}).Build();
 private static int? Status(IResult r)=>(r as IStatusCodeHttpResult)?.StatusCode;
 private static JsonDocument Json(IResult r)=>JsonDocument.Parse(JsonSerializer.Serialize(
  Assert.IsAssignableFrom<IValueHttpResult>(r).Value,
  new JsonSerializerOptions(JsonSerializerDefaults.Web)));
 [Fact] public async Task True_pinned_plan_cards_and_resource_needs_compose_with_tenant_isolation_and_paging()
 {
  if(!Enabled())return;
  const string center="center-workspace-s40",other="center-workspace-foreign-s40";
  var draft=Guid.NewGuid();var foreignDraft=Guid.NewGuid();
  var firstCard=Guid.NewGuid();var foreignCard=Guid.NewGuid();
  var needIds=new List<Guid>();
  await using(var admin=new NpgsqlConnection(Admin))
  {
   await admin.OpenAsync();
   await using(var seed=new NpgsqlCommand("""
    INSERT INTO centers.registry(center_id,display_name,is_active,registered_by_subject,registered_at)
     VALUES('center-workspace-s40','S40',true,'fixture',now()),
      ('center-workspace-foreign-s40','Other',true,'fixture',now());
    INSERT INTO centers.memberships(center_id,subject_id,role_code,is_active,recorded_by_subject,recorded_at)
     VALUES('center-workspace-s40','s40-owner','fixture',true,'fixture',now()),
       ('center-workspace-s40','s40-revoked','fixture',false,'fixture',now()),
       ('center-workspace-foreign-s40','s40-foreign','fixture',true,'fixture',now());
    INSERT INTO programs.center_plan_draft_revisions(center_id,draft_id,revision,program_id,
      national_version,local_title,local_activities,actor_subject)
     VALUES('center-workspace-s40',@draft,1,'national-1',1,'Genuine S40 local plan',
       '[]'::jsonb,'s40-owner'),
      ('center-workspace-s40',@draft,2,'national-1',1,'Later changed plan',
       '[]'::jsonb,'s40-owner'),
      ('center-workspace-foreign-s40',@foreign,1,'national-1',1,'Other private plan',
       '[]'::jsonb,'s40-foreign');
    INSERT INTO operations.center_work_cards
      (center_id,work_card_id,draft_id,pinned_plan_revision,created_by_subject)
     VALUES('center-workspace-s40',@card,@draft,1,'s40-owner'),
      ('center-workspace-foreign-s40',@foreignCard,@foreign,1,'s40-foreign');
    INSERT INTO operations.center_work_card_revisions
     (center_id,work_card_id,revision,title,planning_note,changed_by_subject)
     VALUES('center-workspace-s40',@card,1,'S40 original work','Author plan only','s40-owner'),
      ('center-workspace-foreign-s40',@foreignCard,1,'Foreign secret',
       'Private other data','s40-foreign');
    """,admin))
   {
    seed.Parameters.AddWithValue("draft",draft);seed.Parameters.AddWithValue("foreign",foreignDraft);
    seed.Parameters.AddWithValue("card",firstCard);seed.Parameters.AddWithValue("foreignCard",foreignCard);
    await seed.ExecuteNonQueryAsync();
   }
   // Six authentic needs: five-item bounded preview plus explicit next cursor.
   for(var n=0;n<6;n++)
   {
    var id=Guid.NewGuid();needIds.Add(id);
    await using var add=new NpgsqlCommand("""
     INSERT INTO operations.center_work_resource_needs
      (center_id,need_id,work_card_id,created_by_subject)
      VALUES('center-workspace-s40',@need,@card,'s40-owner');
     INSERT INTO operations.center_work_resource_need_revisions
      (center_id,need_id,revision,resource_label,planning_detail,changed_by_subject)
      VALUES('center-workspace-s40',@need,1,@label,'Not procured','s40-owner');
     """,admin);
    add.Parameters.AddWithValue("need",id);add.Parameters.AddWithValue("card",firstCard);
    add.Parameters.AddWithValue("label","Recorded resource "+n);
    await add.ExecuteNonQueryAsync();
   }
   await using(var newVersion=new NpgsqlCommand("""
    INSERT INTO operations.center_work_resource_need_revisions
      (center_id,need_id,revision,resource_label,planning_detail,changed_by_subject)
    VALUES('center-workspace-s40',@need,2,'Latest original need',
      'Still not fulfilled','s40-owner');
    """,admin))
   {
    newVersion.Parameters.AddWithValue("need",needIds[0]);
    await newVersion.ExecuteNonQueryAsync();
   }
   // Twenty additional genuine cards, to prove work-card page1/page2.
   for(var n=0;n<20;n++)
   {
    await using var add=new NpgsqlCommand("""
     INSERT INTO operations.center_work_cards
      (center_id,work_card_id,draft_id,pinned_plan_revision,created_by_subject)
      VALUES('center-workspace-s40',@card,@draft,1,'s40-owner');
     INSERT INTO operations.center_work_card_revisions
      (center_id,work_card_id,revision,title,planning_note,changed_by_subject)
      VALUES('center-workspace-s40',@card,1,@title,'Planned only','s40-owner');
    """,admin);
    add.Parameters.AddWithValue("card",Guid.NewGuid());add.Parameters.AddWithValue("draft",draft);
    add.Parameters.AddWithValue("title","Work item "+n);
    await add.ExecuteNonQueryAsync();
   }
  }
  Task<IResult> Get(Guid? before=null,long revision=1,string centerId=center,
   string sub="s40-owner",IConfiguration? cfg=null,bool planGrant=true,
   bool cardGrant=true,bool needGrant=true,Guid? cardDeny=null,Guid? needDeny=null)=>
   CenterLocalPlanWorkspaceEndpoint.Get(Actor(sub),cfg??Config(),
    new Plan(planGrant),new Card(cardGrant,cardDeny),new Need(needGrant,needDeny),
    centerId,draft,revision,before,default);
  Assert.Equal(403,Status(await Get(sub:"s40-revoked")));
  Assert.Equal(403,Status(await Get(sub:"s40-foreign")));
  Assert.Equal(403,Status(await Get(centerId:other)));
  Assert.Equal(403,Status(await Get(planGrant:false)));
  Assert.Equal(403,Status(await Get(cardGrant:false)));
  Assert.Equal(403,Status(await Get(needGrant:false)));
  Assert.Equal(503,Status(await Get(cfg:Config(read:Admin))));
  Assert.Equal(503,Status(await Get(cfg:Config(read:Writer))));
  Assert.Equal(404,Status(await Get(revision:99)));
  using(var newer=Json(await Get(revision:2)))
  {
   Assert.Empty(newer.RootElement.GetProperty("cards").EnumerateArray());
   Assert.Equal("Later changed plan",newer.RootElement.GetProperty("sourcePlan")
    .GetProperty("localTitle").GetString());
  }
  var all=new List<JsonElement>();Guid? cursor;
  using(var page1=Json(await Get()))
  {
   var root=page1.RootElement;
   Assert.Equal(20,root.GetProperty("cards").GetArrayLength());
   Assert.Equal("Genuine S40 local plan",root.GetProperty("sourcePlan")
    .GetProperty("localTitle").GetString());
   Assert.False(root.GetProperty("isExecuted").GetBoolean());
   Assert.False(root.GetProperty("isPurchased").GetBoolean());
   cursor=root.GetProperty("nextBeforeCardId").GetGuid();
   all.AddRange(root.GetProperty("cards").EnumerateArray().Select(x=>x.Clone()));
  }
  using(var page2=Json(await Get(before:cursor)))
  {
   var root=page2.RootElement;
   Assert.Single(root.GetProperty("cards").EnumerateArray());
   Assert.Equal(JsonValueKind.Null,root.GetProperty("nextBeforeCardId").ValueKind);
   all.AddRange(root.GetProperty("cards").EnumerateArray().Select(x=>x.Clone()));
  }
  Assert.Equal(21,all.Count);
  var original=Assert.Single(all.Where(x=>x.GetProperty("workCardId").GetGuid()==firstCard));
  Assert.Equal(1L,original.GetProperty("pinnedPlanRevision").GetInt64());
  Assert.Equal("S40 original work",original.GetProperty("title").GetString());
  Assert.Equal(5,original.GetProperty("needs").GetArrayLength());
  Assert.Equal(5,original.GetProperty("needsPreviewLimit").GetInt32());
  Assert.Equal(6,needIds.Count);
  Assert.Equal(original.GetProperty("needs")[4].GetProperty("needId").GetGuid(),
   original.GetProperty("nextBeforeNeedId").GetGuid());
  foreach(var need in original.GetProperty("needs").EnumerateArray())
  {
   Assert.False(need.GetProperty("isFulfilled").GetBoolean());
   Assert.False(need.GetProperty("isPurchased").GetBoolean());
   Assert.Equal("UNKNOWN",need.GetProperty("fulfillmentStatus").GetString());
   Assert.DoesNotContain("Foreign secret",need.GetRawText());
  }
  // Per-resource denial blocks the full joined page: cannot elevate via overview grant.
  var deniedPage=all.Take(20).Any(x=>x.GetProperty("workCardId").GetGuid()==firstCard)
   ?(Guid?)null:cursor;
  Assert.Equal(403,Status(await Get(before:deniedPage,cardDeny:firstCard)));
  Assert.Equal(403,Status(await Get(before:deniedPage,needDeny:firstCard)));
  Assert.Equal(200,Status(await Get(before:cursor)));
 }
}
