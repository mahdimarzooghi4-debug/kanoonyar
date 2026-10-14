using System.Security.Claims;
using System.Text.Json;
using Kanoonyar.Api.Programs;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Xunit;
namespace Kanoonyar.Api.Tests;
public sealed class CenterPrivatePageTemplatePostgresTests
{
 private static string? Admin=>Environment.GetEnvironmentVariable("CENTER_DRAFT_INTEGRATION_ADMIN_URL");
 private static string? Read=>Environment.GetEnvironmentVariable("CENTER_DRAFT_INTEGRATION_READ_DATABASE_URL");
 private static string? TemplateWrite=>Environment.GetEnvironmentVariable("CENTER_PRIVATE_TEMPLATE_INTEGRATION_DATABASE_URL");
 private static string? PageWrite=>Environment.GetEnvironmentVariable("CENTER_SITE_DRAFT_INTEGRATION_DATABASE_URL");
 private static bool Enabled()
 {
  if(Environment.GetEnvironmentVariable("CENTER_DRAFT_INTEGRATION_REQUIRED")=="1")
  {
   Assert.False(string.IsNullOrWhiteSpace(Admin));Assert.False(string.IsNullOrWhiteSpace(Read));
   Assert.False(string.IsNullOrWhiteSpace(TemplateWrite));Assert.False(string.IsNullOrWhiteSpace(PageWrite));
  }
  return !string.IsNullOrWhiteSpace(Admin)&&!string.IsNullOrWhiteSpace(Read)&&
   !string.IsNullOrWhiteSpace(TemplateWrite)&&!string.IsNullOrWhiteSpace(PageWrite);
 }
 private sealed class Template(bool yes=true):ICenterPrivatePageTemplateAuthorization
 {
  public ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal a,string c,string operation,CancellationToken ct)
   =>ValueTask.FromResult(yes);
 }
 private sealed class Page(bool yes=true):ICenterSiteDraftAuthorization
 {
  public ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal a,string c,Guid page,string operation,CancellationToken ct)
   =>ValueTask.FromResult(yes);
 }
 private static ClaimsPrincipal Actor(string subject)=>new(new ClaimsIdentity([new Claim("sub",subject)],"synthetic"));
 private static IConfiguration Config(string? write=null,string? read=null)=>
  new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{
   ["CENTER_PRIVATE_TEMPLATE_WRITE_DATABASE_URL"]=write??TemplateWrite,
   ["CENTER_SITE_DRAFT_WRITE_DATABASE_URL"]=PageWrite,
   ["CENTER_DRAFT_READ_DATABASE_URL"]=read??Read}).Build();
 private static int? Status(IResult r)=>(r as IStatusCodeHttpResult)?.StatusCode;
 private static JsonDocument Json(IResult response)=>JsonDocument.Parse(JsonSerializer.Serialize(
  Assert.IsAssignableFrom<IValueHttpResult>(response).Value,
  new JsonSerializerOptions(JsonSerializerDefaults.Web)));
 [Fact] public async Task Real_source_pins_are_immutable_center_scoped_and_audited_with_CAS()
 {
  if(!Enabled())return;
  const string center="center-templates-s38",other="center-templates-foreign-s38";
  var pageId=Guid.NewGuid();var foreign=Guid.NewGuid();var templateId=Guid.NewGuid();
  await using(var admin=new NpgsqlConnection(Admin))
  {
   await admin.OpenAsync();
   await using var setup=new NpgsqlCommand("""
    INSERT INTO centers.registry(center_id,display_name,is_active,registered_by_subject,registered_at)
    VALUES('center-templates-s38','S38 Center',true,'fixture',now()),
          ('center-templates-foreign-s38','S38 Other',true,'fixture',now());
    INSERT INTO centers.memberships(center_id,subject_id,role_code,is_active,recorded_by_subject,recorded_at)
    VALUES('center-templates-s38','s38-editor','fixture',true,'fixture',now()),
          ('center-templates-s38','s38-revoked','fixture',false,'fixture',now()),
          ('center-templates-foreign-s38','s38-other','fixture',true,'fixture',now());
    """,admin);
   await setup.ExecuteNonQueryAsync();
  }
  Task<IResult> SavePage(Guid id,long expected,string slug,string title,string content,
   string actor="s38-editor",string scope=center,ICenterSiteDraftAuthorization? permission=null)=>
   CenterSiteDraftEndpoint.Append(Actor(actor),Config(),permission??new Page(),scope,
    new AppendCenterSiteDraftRequest(id,expected,slug,title,content),default);
  Task<IResult> Save(long expected,long pin,string label="Local reusable template",Guid? source=null,
    string actor="s38-editor",string scope=center,IConfiguration? config=null,
    ICenterPrivatePageTemplateAuthorization? permission=null,ICenterSiteDraftAuthorization? pageGrant=null)=>
   CenterPrivatePageTemplateEndpoint.Append(Actor(actor),config??Config(),
    permission??new Template(),pageGrant??new Page(),scope,
    new AppendPrivatePageTemplateRequest(templateId,source??pageId,expected,pin,label),default);
  Task<IResult> List(string actor="s38-editor",string scope=center,IConfiguration? config=null)=>
   CenterPrivatePageTemplateEndpoint.List(Actor(actor),config??Config(),new Template(),
    new Page(),scope,null,default);
  Task<IResult> History(long? before=null,string actor="s38-editor",string scope=center)=>
   CenterPrivatePageTemplateEndpoint.History(Actor(actor),Config(),new Template(),
    new Page(),scope,templateId,before,default);
  Assert.Equal(200,Status(await SavePage(pageId,0,"s38-source","Source title v1","Source body v1")));
  Assert.Equal(200,Status(await SavePage(pageId,1,"s38-source","Source title v2","Source body v2")));
  Assert.Equal(200,Status(await SavePage(foreign,0,"s38-foreign","Foreign","Do not leak",
    actor:"s38-other",scope:other)));
  Assert.Equal(403,Status(await Save(0,1,actor:"s38-revoked")));
  Assert.Equal(403,Status(await Save(0,1,actor:"s38-other")));
  Assert.Equal(403,Status(await Save(0,1,permission:new Template(false))));
  Assert.Equal(403,Status(await Save(0,1,pageGrant:new Page(false))));
  Assert.Equal(409,Status(await Save(0,999)));
  Assert.Equal(409,Status(await Save(0,1,source:foreign)));
  Assert.Equal(503,Status(await Save(0,1,config:Config(write:Admin))));
  Assert.Equal(503,Status(await Save(0,1,config:Config(write:Read))));
  Assert.Equal(200,Status(await Save(0,1,"First source version")));
  using(var rows=Json(await List()))
  {
   var entries=rows.RootElement.GetProperty("templates");
   Assert.Single(entries.EnumerateArray());
   Assert.Equal(pageId,entries[0].GetProperty("sourcePageId").GetGuid());
   Assert.Equal(1L,entries[0].GetProperty("pinnedPageRevision").GetInt64());
   Assert.Equal("Source body v1",entries[0].GetProperty("bodyText").GetString());
   Assert.Equal("Source title v1",entries[0].GetProperty("title").GetString());
   Assert.False(entries[0].GetProperty("isShared").GetBoolean());
   Assert.False(entries[0].GetProperty("isPublished").GetBoolean());
  }
  var race=await Task.WhenAll(Save(1,2,"Current source A"),Save(1,2,"Current source B"));
  Assert.Single(race,x=>Status(x)==200);
  Assert.Single(race,x=>Status(x)==409);
  Assert.Equal(409,Status(await Save(1,2)));
  Assert.Equal(409,Status(await Save(2,1,source:foreign)));
  using(var rows=Json(await History()))
  {
   Assert.Equal(2,rows.RootElement.GetProperty("revisions").GetArrayLength());
   Assert.Equal("Source body v1",rows.RootElement.GetProperty("revisions")[1]
    .GetProperty("bodyText").GetString());
   Assert.Equal("Source body v2",rows.RootElement.GetProperty("revisions")[0]
    .GetProperty("bodyText").GetString());
   Assert.Equal(pageId,rows.RootElement.GetProperty("sourcePageId").GetGuid());
  }
  Assert.Equal(403,Status(await List(actor:"s38-revoked")));
  Assert.Equal(403,Status(await List(actor:"s38-other")));
  Assert.Equal(403,Status(await List(scope:other)));
  Assert.Equal(403,Status(await History(actor:"s38-revoked")));
  Assert.Equal(503,Status(await List(config:Config(read:Admin))));
  Assert.Equal(503,Status(await List(config:Config(read:TemplateWrite))));
  // Reusing template text via the genuine existing page command requires a separate page write grant.
  Assert.Equal(403,Status(await SavePage(Guid.NewGuid(),0,"s38-clone","New page","Source body v1",
   permission:new Page(false))));
  Assert.Equal(200,Status(await SavePage(Guid.NewGuid(),0,"s38-clone","New page","Source body v1")));
  await using var check=new NpgsqlConnection(Admin);await check.OpenAsync();
  await using(var audit=new NpgsqlCommand("""
   SELECT
    (SELECT count(*) FROM web.center_private_page_template_revisions WHERE center_id=@center),
    (SELECT count(*) FROM governance.command_audit WHERE center_id=@center
     AND action_code='PRIVATE_PAGE_TEMPLATE_REVISION_APPENDED'),
    (SELECT count(*) FROM governance.outbox_events
     WHERE aggregate_id=@template AND event_type='PrivatePageTemplateRevisionAppended')
   """,check))
  {
   audit.Parameters.AddWithValue("center",center);
   audit.Parameters.AddWithValue("template",templateId.ToString("D"));
   await using var reader=await audit.ExecuteReaderAsync();
   Assert.True(await reader.ReadAsync());
   Assert.Equal(2L,reader.GetInt64(0));Assert.Equal(2L,reader.GetInt64(1));Assert.Equal(2L,reader.GetInt64(2));
  }
  await using(var tamper=new NpgsqlCommand("""
   UPDATE web.center_private_page_template_revisions
    SET pinned_page_revision=999 WHERE center_id=@center AND template_id=@template
   """,check))
  {
   tamper.Parameters.AddWithValue("center",center);
   tamper.Parameters.AddWithValue("template",templateId);
   await Assert.ThrowsAsync<PostgresException>(()=>tamper.ExecuteNonQueryAsync());
  }
 }
}
