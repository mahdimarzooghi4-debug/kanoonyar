using System.Security.Claims;
using System.Text.Json;
using Kanoonyar.Api.Programs;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Xunit;

namespace Kanoonyar.Api.Tests;
public sealed class CenterSiteNavigationPostgresTests
{
 private static string? Admin=>Environment.GetEnvironmentVariable("CENTER_DRAFT_INTEGRATION_ADMIN_URL");
 private static string? Reader=>Environment.GetEnvironmentVariable("CENTER_DRAFT_INTEGRATION_READ_DATABASE_URL");
 private static string? NavigationWriter=>Environment.GetEnvironmentVariable("CENTER_SITE_NAVIGATION_INTEGRATION_DATABASE_URL");
 private static string? PageWriter=>Environment.GetEnvironmentVariable("CENTER_SITE_DRAFT_INTEGRATION_DATABASE_URL");
 private static bool Enabled()
 {
  if(Environment.GetEnvironmentVariable("CENTER_DRAFT_INTEGRATION_REQUIRED")=="1")
  {
   Assert.False(string.IsNullOrWhiteSpace(Admin));
   Assert.False(string.IsNullOrWhiteSpace(Reader));
   Assert.False(string.IsNullOrWhiteSpace(NavigationWriter));
   Assert.False(string.IsNullOrWhiteSpace(PageWriter));
  }
  return !string.IsNullOrWhiteSpace(Admin)&&!string.IsNullOrWhiteSpace(Reader)&&
    !string.IsNullOrWhiteSpace(NavigationWriter)&&!string.IsNullOrWhiteSpace(PageWriter);
 }
 private sealed class Nav(bool allow=true):ICenterSiteNavigationAuthorization
 {
  public ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal actor,string center,string operation,
   CancellationToken ct)=>ValueTask.FromResult(allow);
 }
 private sealed class Pages(bool allow=true):ICenterSiteDraftAuthorization
 {
  public ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal actor,string center,Guid page,
   string operation,CancellationToken ct)=>ValueTask.FromResult(allow);
 }
 private static ClaimsPrincipal Actor(string sub)=>new(new ClaimsIdentity([new Claim("sub",sub)],"test"));
 private static IConfiguration Config(string? writer=null,string? reader=null)=>
  new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{
   ["CENTER_SITE_NAVIGATION_WRITE_DATABASE_URL"]=writer??NavigationWriter,
   ["CENTER_DRAFT_READ_DATABASE_URL"]=reader??Reader,
   ["CENTER_SITE_DRAFT_WRITE_DATABASE_URL"]=PageWriter
  }).Build();
 private static int? Status(IResult r)=>(r as IStatusCodeHttpResult)?.StatusCode;
 private static JsonDocument Json(IResult response)=>JsonDocument.Parse(
  JsonSerializer.Serialize(Assert.IsAssignableFrom<IValueHttpResult>(response).Value,
   new JsonSerializerOptions(JsonSerializerDefaults.Web)));
 [Fact] public async Task Navigation_pins_true_pages_has_immutable_history_and_race_safe_audit()
 {
  if(!Enabled())return;
  var p1=Guid.NewGuid();var p2=Guid.NewGuid();var foreign=Guid.NewGuid();
  var nav=new Nav();var pages=new Pages();
  // Dedicated disposable centers prevent interference with S20 tests sharing the same DB.
  await using(var fixture=new NpgsqlConnection(Admin))
  {
   await fixture.OpenAsync();
   await using var seed=new NpgsqlCommand("""
    INSERT INTO centers.registry(center_id,display_name,is_active,registered_by_subject,registered_at)
    VALUES('center-navigation-s32','S32 Center',true,'fixture',now()),
          ('center-navigation-foreign-s32','S32 Foreign',true,'fixture',now());
    INSERT INTO centers.memberships(center_id,subject_id,role_code,is_active,recorded_by_subject,recorded_at)
    VALUES('center-navigation-s32','s32-actor','fixture-member',true,'fixture',now()),
          ('center-navigation-foreign-s32','s32-foreign-actor','fixture-member',true,'fixture',now()),
          ('center-navigation-s32','s32-revoked','fixture-member',false,'fixture',now());
    """,fixture);
   await seed.ExecuteNonQueryAsync();
  }

  Task<IResult> Page(Guid id,long expected,string slug,string title,
   string center="center-navigation-s32",string actor="s32-actor")=>
   CenterSiteDraftEndpoint.Append(Actor(actor),Config(),pages,center,
     new AppendCenterSiteDraftRequest(id,expected,slug,title,"Plain private page"),default);
  Task<IResult> Save(long expected,List<SiteNavigationPageRef> links,
   string center="center-navigation-s32",string actor="s32-actor",
   IConfiguration? cfg=null,ICenterSiteNavigationAuthorization? grant=null,
   ICenterSiteDraftAuthorization? pageGrant=null)=>
   CenterSiteNavigationEndpoint.Append(Actor(actor),cfg??Config(),
    grant??nav,pageGrant??pages,center,
    new AppendCenterSiteNavigationRequest(expected,links),default);
  Task<IResult> Latest(string center="center-navigation-s32",string actor="s32-actor",
   IConfiguration? cfg=null)=>
   CenterSiteNavigationEndpoint.Latest(Actor(actor),cfg??Config(),nav,pages,center,default);
  Task<IResult> History(long? before=null,string center="center-navigation-s32",string actor="s32-actor")=>
   CenterSiteNavigationEndpoint.History(Actor(actor),Config(),nav,pages,center,before,default);
  Assert.Equal(200,Status(await Page(p1,0,"nav-first","First v1")));
  Assert.Equal(200,Status(await Page(p2,0,"nav-second","Second v1")));
  Assert.Equal(200,Status(await Page(p1,1,"nav-first","First v2")));
  Assert.Equal(200,Status(await Page(foreign,0,"foreign-page","Foreign",
   center:"center-navigation-foreign-s32",actor:"s32-foreign-actor")));
  using(var none=Json(await Latest()))
  {
   Assert.Equal(JsonValueKind.Null,none.RootElement.GetProperty("revision").ValueKind);
   Assert.Equal(0,none.RootElement.GetProperty("items").GetArrayLength());
   Assert.False(none.RootElement.GetProperty("isPublished").GetBoolean());
  }
  var firstLinks=new List<SiteNavigationPageRef>{new(p1,1),new(p2,1)};
  Assert.Equal(403,Status(await Save(0,firstLinks,actor:"s32-revoked")));
  Assert.Equal(403,Status(await Save(0,firstLinks,actor:"s32-foreign-actor")));
  Assert.Equal(403,Status(await Save(0,firstLinks,grant:new Nav(false))));
  Assert.Equal(403,Status(await Save(0,firstLinks,pageGrant:new Pages(false))));
  Assert.Equal(503,Status(await Save(0,firstLinks,cfg:Config(writer:Admin))));
  Assert.Equal(503,Status(await Save(0,firstLinks,cfg:Config(writer:Reader))));
  Assert.Equal(409,Status(await Save(0,[new(foreign,1)])));
  Assert.Equal(409,Status(await Save(0,[new(p1,3)])));
  Assert.Equal(200,Status(await Save(0,firstLinks)));
  using(var current=Json(await Latest()))
  {
   var root=current.RootElement;
   Assert.Equal(1,root.GetProperty("revision").GetInt64());
   var rows=root.GetProperty("items");
   Assert.Equal(2,rows.GetArrayLength());
   Assert.Equal("First v1",rows[0].GetProperty("title").GetString());
   Assert.Equal(1,rows[0].GetProperty("pinnedPageRevision").GetInt64());
   Assert.Equal("nav-first",rows[0].GetProperty("slug").GetString());
  }
  var candidateA=new List<SiteNavigationPageRef>{new(p2,1),new(p1,2)};
  var candidateB=new List<SiteNavigationPageRef>{new(p1,2),new(p2,1)};
  var result=await Task.WhenAll(Save(1,candidateA),Save(1,candidateB));
  Assert.Single(result,x=>Status(x)==200);
  Assert.Single(result,x=>Status(x)==409);
  Assert.Equal(409,Status(await Save(1,firstLinks)));
  Assert.Equal(200,Status(await Save(2,[])));
  using(var latest=Json(await Latest()))
  {
   Assert.Equal(3,latest.RootElement.GetProperty("revision").GetInt64());
   Assert.Equal(0,latest.RootElement.GetProperty("items").GetArrayLength());
   Assert.False(latest.RootElement.GetProperty("isPublished").GetBoolean());
  }
  using(var history=Json(await History()))
  {
   var versions=history.RootElement.GetProperty("revisions");
   Assert.Equal(3,versions.GetArrayLength());
   Assert.Equal(3,versions[0].GetProperty("revision").GetInt64());
   Assert.Equal(0,versions[0].GetProperty("items").GetArrayLength());
   Assert.Equal(2,versions[1].GetProperty("revision").GetInt64());
   Assert.Equal(1,versions[2].GetProperty("revision").GetInt64());
   Assert.Equal("First v1",versions[2].GetProperty("items")[0].GetProperty("title").GetString());
   Assert.False(versions[2].GetProperty("isPublished").GetBoolean());
  }
  Assert.Equal(403,Status(await Latest(actor:"s32-revoked")));
  Assert.Equal(403,Status(await Latest(center:"center-navigation-foreign-s32",actor:"s32-actor")));
  using(var differentCenter=Json(await Latest(center:"center-navigation-foreign-s32",actor:"s32-foreign-actor")))
  {
   Assert.Equal(JsonValueKind.Null,differentCenter.RootElement.GetProperty("revision").ValueKind);
  }
  Assert.Equal(503,Status(await Latest(cfg:Config(reader:Admin))));
  Assert.Equal(503,Status(await Latest(cfg:Config(reader:NavigationWriter))));
  await using var db=new NpgsqlConnection(Admin);await db.OpenAsync();
  await using(var count=new NpgsqlCommand("""
   SELECT
     (SELECT count(*) FROM web.center_site_navigation_revisions WHERE center_id='center-navigation-s32'),
     (SELECT count(*) FROM web.center_site_navigation_items WHERE center_id='center-navigation-s32'),
     (SELECT count(*) FROM governance.command_audit WHERE center_id='center-navigation-s32'
      AND action_code='CENTER_SITE_NAVIGATION_REVISION_APPENDED'),
     (SELECT count(*) FROM governance.outbox_events WHERE event_type='CenterSiteNavigationRevisionAppended'
      AND aggregate_id='center-navigation-s32')
   """,db))
  {
   await using var rd=await count.ExecuteReaderAsync();
   Assert.True(await rd.ReadAsync());
   Assert.Equal(3L,rd.GetInt64(0));
   Assert.Equal(4L,rd.GetInt64(1));
   Assert.Equal(3L,rd.GetInt64(2));
   Assert.Equal(3L,rd.GetInt64(3));
  }
  await using(var attack=new NpgsqlCommand("""
   UPDATE web.center_site_navigation_items SET pinned_page_revision=2
   WHERE center_id='center-navigation-s32' AND navigation_revision=1 AND page_id=@id
   """,db))
  {
   attack.Parameters.AddWithValue("id",p1);
   await Assert.ThrowsAsync<PostgresException>(()=>attack.ExecuteNonQueryAsync());
  }
 }
}
