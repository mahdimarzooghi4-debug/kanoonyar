using System.Security.Claims;
using System.Text.Json;
using Kanoonyar.Api.Programs;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Xunit;
namespace Kanoonyar.Api.Tests;
public sealed class CenterAssembledSitePreviewPostgresTests
{
 private static string? Admin=>Environment.GetEnvironmentVariable("CENTER_DRAFT_INTEGRATION_ADMIN_URL");
 private static string? Reader=>Environment.GetEnvironmentVariable("CENTER_DRAFT_INTEGRATION_READ_DATABASE_URL");
 private static string? PageWrite=>Environment.GetEnvironmentVariable("CENTER_SITE_DRAFT_INTEGRATION_DATABASE_URL");
 private static string? DesignWrite=>Environment.GetEnvironmentVariable("CENTER_SITE_DESIGN_INTEGRATION_DATABASE_URL");
 private static string? NavWrite=>Environment.GetEnvironmentVariable("CENTER_SITE_NAVIGATION_INTEGRATION_DATABASE_URL");
 private static bool Enabled()
 {
  if(Environment.GetEnvironmentVariable("CENTER_DRAFT_INTEGRATION_REQUIRED")=="1"){
   Assert.False(string.IsNullOrWhiteSpace(Admin));Assert.False(string.IsNullOrWhiteSpace(Reader));
   Assert.False(string.IsNullOrWhiteSpace(PageWrite));Assert.False(string.IsNullOrWhiteSpace(DesignWrite));
   Assert.False(string.IsNullOrWhiteSpace(NavWrite));
  }
  return !string.IsNullOrWhiteSpace(Admin)&&!string.IsNullOrWhiteSpace(Reader)&&
   !string.IsNullOrWhiteSpace(PageWrite)&&!string.IsNullOrWhiteSpace(DesignWrite)&&
   !string.IsNullOrWhiteSpace(NavWrite);
 }
 private sealed class Nav(bool value=true):ICenterSiteNavigationAuthorization{
  public ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal a,string c,string op,CancellationToken ct)
   =>ValueTask.FromResult(value);
 }
 private sealed class Pages(bool value=true):ICenterSiteDraftAuthorization{
  public ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal a,string c,Guid id,string op,CancellationToken ct)
   =>ValueTask.FromResult(value);
 }
 private sealed class Design(bool value=true):ICenterSiteDesignAuthorization{
  public ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal a,string c,string op,CancellationToken ct)
   =>ValueTask.FromResult(value);
 }
 private static ClaimsPrincipal Actor(string id)=>new(new ClaimsIdentity([new Claim("sub",id)],"test"));
 private static IConfiguration Config(string? reader=null)=>new ConfigurationBuilder()
  .AddInMemoryCollection(new Dictionary<string,string?>{
   ["CENTER_DRAFT_READ_DATABASE_URL"]=reader??Reader,
   ["CENTER_SITE_DRAFT_WRITE_DATABASE_URL"]=PageWrite,
   ["CENTER_SITE_DESIGN_WRITE_DATABASE_URL"]=DesignWrite,
   ["CENTER_SITE_NAVIGATION_WRITE_DATABASE_URL"]=NavWrite}).Build();
 private static int? Status(IResult r)=>(r as IStatusCodeHttpResult)?.StatusCode;
 private static JsonDocument Json(IResult r)=>JsonDocument.Parse(JsonSerializer.Serialize(
  Assert.IsAssignableFrom<IValueHttpResult>(r).Value,new JsonSerializerOptions(JsonSerializerDefaults.Web)));
 [Fact] public async Task Private_site_uses_exact_pinned_page_content_and_stable_versions_without_cross_center_leak()
 {
  if(!Enabled())return;
  const string center="center-assembled-s37",other="center-assembled-foreign-s37",subject="s37-owner";
  var first=Guid.NewGuid();var foreign=Guid.NewGuid();
  await using(var db=new NpgsqlConnection(Admin)){
   await db.OpenAsync();
   await using var insert=new NpgsqlCommand("""
    INSERT INTO centers.registry(center_id,display_name,is_active,registered_by_subject,registered_at)
    VALUES('center-assembled-s37','S37 site',true,'fixture',now()),
          ('center-assembled-foreign-s37','S37 other',true,'fixture',now());
    INSERT INTO centers.memberships(center_id,subject_id,role_code,is_active,recorded_by_subject,recorded_at)
    VALUES('center-assembled-s37','s37-owner','fixture',true,'fixture',now()),
          ('center-assembled-s37','s37-revoked','fixture',false,'fixture',now()),
          ('center-assembled-foreign-s37','s37-foreign','fixture',true,'fixture',now());
    """,db);
   await insert.ExecuteNonQueryAsync();
  }
  Task<IResult> Read(string actor=subject,string scope=center,IConfiguration? config=null,
   long? nr=null,long? dr=null,int? after=null,
   bool nav=true,bool page=true,bool design=true)=>
   CenterAssembledSitePreviewEndpoint.Read(Actor(actor),config??Config(),new Nav(nav),
    new Pages(page),new Design(design),scope,nr,dr,after,default);
  Task<IResult> SavePage(Guid page,long expected,string title,string body,
   string scope=center,string actor=subject)=>
   CenterSiteDraftEndpoint.Append(Actor(actor),Config(),new Pages(),scope,
    new AppendCenterSiteDraftRequest(page,expected,page==first?"home":"other",
      title,body),default);
  Task<IResult> SaveDesign()=>CenterSiteDesignEndpoint.Append(Actor(subject),Config(),new Design(),
   center,new AppendCenterSiteDesignRequest(0,"HERO","FOREST","SYSTEM",
    "Actual site header","Footer",true,true),default);
  Task<IResult> SaveNav()=>CenterSiteNavigationEndpoint.Append(Actor(subject),Config(),
   new Nav(),new Pages(),center,new AppendCenterSiteNavigationRequest(0,[new(first,1)]),default);
  using(var missing=Json(await Read())){
   Assert.False(missing.RootElement.GetProperty("ready").GetBoolean());
   Assert.True(missing.RootElement.GetProperty("missingNavigation").GetBoolean());
   Assert.True(missing.RootElement.GetProperty("missingDesign").GetBoolean());
   Assert.Empty(missing.RootElement.GetProperty("items").EnumerateArray());
  }
  Assert.Equal(200,Status(await SavePage(first,0,"Real page v1","Original pinned body")));
  Assert.Equal(200,Status(await SavePage(first,1,"Real page v2","Newer unpinned body")));
  Assert.Equal(200,Status(await SavePage(foreign,0,"Other center","Not for S37",
   scope:other,actor:"s37-foreign")));
  Assert.Equal(200,Status(await SaveDesign()));
  Assert.Equal(200,Status(await SaveNav()));
  using(var built=Json(await Read())){
   var root=built.RootElement;
   Assert.True(root.GetProperty("ready").GetBoolean());
   Assert.Equal(1,root.GetProperty("navigationRevision").GetInt64());
   Assert.Equal(1,root.GetProperty("designRevision").GetInt64());
   Assert.False(root.GetProperty("isPublished").GetBoolean());
   Assert.False(root.GetProperty("deliveryVerified").GetBoolean());
   Assert.Equal("FOREST",root.GetProperty("design").GetProperty("paletteKey").GetString());
   var rows=root.GetProperty("items");Assert.Single(rows.EnumerateArray());
   Assert.Equal(first,rows[0].GetProperty("pageId").GetGuid());
   Assert.Equal(1,rows[0].GetProperty("pinnedPageRevision").GetInt64());
   Assert.Equal("Real page v1",rows[0].GetProperty("title").GetString());
   Assert.Equal("Original pinned body",rows[0].GetProperty("bodyText").GetString());
   Assert.Equal(JsonValueKind.Null,root.GetProperty("nextAfterPosition").ValueKind);
   Assert.DoesNotContain("Newer unpinned body",root.GetRawText());
   Assert.DoesNotContain("Not for S37",root.GetRawText());
  }
  using(var exact=Json(await Read(nr:1,dr:1))){
   Assert.Equal("Original pinned body",exact.RootElement.GetProperty("items")[0]
    .GetProperty("bodyText").GetString());
  }
  Assert.Equal(404,Status(await Read(nr:99,dr:1)));
  Assert.Equal(404,Status(await Read(nr:1,dr:99)));
  Assert.Equal(403,Status(await Read(actor:"s37-revoked")));
  Assert.Equal(403,Status(await Read(actor:"s37-foreign")));
  Assert.Equal(403,Status(await Read(scope:other)));
  Assert.Equal(403,Status(await Read(nav:false)));
  Assert.Equal(403,Status(await Read(page:false)));
  Assert.Equal(403,Status(await Read(design:false)));
  Assert.Equal(503,Status(await Read(config:Config(reader:Admin))));
  Assert.Equal(503,Status(await Read(config:Config(reader:NavWrite))));
  using(var unchanged=Json(await Read(nr:1,dr:1,after:1))){
   Assert.Empty(unchanged.RootElement.GetProperty("items").EnumerateArray());
  }
 }
}
