using System.Security.Claims;
using System.Text.Json;
using Kanoonyar.Api.Programs;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Xunit;

namespace Kanoonyar.Api.Tests;

/// <summary>Real disposable PostgreSQL with separate minimal write/read logins.</summary>
public sealed class CenterSiteDraftPostgresTests
{
 private static string? Admin=>Environment.GetEnvironmentVariable("CENTER_DRAFT_INTEGRATION_ADMIN_URL");
 private static string? Read=>Environment.GetEnvironmentVariable("CENTER_DRAFT_INTEGRATION_READ_DATABASE_URL");
 private static string? Write=>Environment.GetEnvironmentVariable("CENTER_SITE_DRAFT_INTEGRATION_DATABASE_URL");
 private static bool Enabled()
 {
  if(Environment.GetEnvironmentVariable("CENTER_DRAFT_INTEGRATION_REQUIRED")=="1")
  {
   Assert.False(string.IsNullOrWhiteSpace(Admin));
   Assert.False(string.IsNullOrWhiteSpace(Read));
   Assert.False(string.IsNullOrWhiteSpace(Write));
  }
  return !string.IsNullOrWhiteSpace(Admin)&&!string.IsNullOrWhiteSpace(Read)&&!string.IsNullOrWhiteSpace(Write);
 }
 private static IConfiguration Config(string? writer=null,string? reader=null)=>
  new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{
   ["CENTER_SITE_DRAFT_WRITE_DATABASE_URL"]=writer??Write,
   ["CENTER_DRAFT_READ_DATABASE_URL"]=reader??Read
  }).Build();
 private static ClaimsPrincipal Actor(string subject)=>
  new(new ClaimsIdentity([new Claim("sub",subject)],"fixture"));
 private static int? Status(IResult response)=>(response as IStatusCodeHttpResult)?.StatusCode;
 private sealed class Grant:ICenterSiteDraftAuthorization
 {
  public ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal actor,string center,Guid page,
   string op,CancellationToken ct)=>ValueTask.FromResult(true);
 }
 [Fact]
 public async Task Site_drafts_are_versioned_private_audited_and_isolated()
 {
  if(!Enabled())return;
  var id=Guid.NewGuid(),competing=Guid.NewGuid();
  var grant=new Grant();
  Task<IResult> WritePage(Guid page,long expected,string slug,string title="Synthetic title",
   string actor="actor-a",string center="center-a",IConfiguration? config=null)=>
    CenterSiteDraftEndpoint.Append(Actor(actor),config??Config(),grant,center,
      new AppendCenterSiteDraftRequest(page,expected,slug,title,"Synthetic plain body"),default);
  Task<IResult> List(string center="center-a",string actor="actor-a",IConfiguration? config=null)=>
    CenterSiteDraftEndpoint.List(Actor(actor),config??Config(),grant,center,null,default);
  Task<IResult> History(string center="center-a",string actor="actor-a",IConfiguration? config=null)=>
    CenterSiteDraftEndpoint.History(Actor(actor),config??Config(),grant,center,id,null,default);
  var created=await WritePage(id,0,"about-center");
  Assert.Equal(200,Status(created));
  using(var json=JsonDocument.Parse(JsonSerializer.Serialize(
   Assert.IsAssignableFrom<IValueHttpResult>(created).Value)))
  {
   var root=json.RootElement;
   Assert.Equal(id,root.GetProperty("pageId").GetGuid());
   Assert.Equal(1L,root.GetProperty("revision").GetInt64());
   Assert.Equal("PRIVATE_DRAFT",root.GetProperty("visibility").GetString());
   Assert.False(root.GetProperty("isPublished").GetBoolean());
  }
  var conflicting=await Task.WhenAll(
    WritePage(id,1,"about-center","v2"),
    WritePage(id,1,"about-center","concurrent"));
  Assert.Single(conflicting.Where(x=>Status(x)==200));
  Assert.Single(conflicting.Where(x=>Status(x)==409));
  Assert.Equal(409,Status(await WritePage(id,1,"about-center","stale")));
  Assert.Equal(409,Status(await WritePage(competing,0,"about-center","duplicate slug")));
  Assert.Equal(409,Status(await WritePage(id,2,"changed-slug","must not rename")));
  Assert.Equal(403,Status(await WritePage(Guid.NewGuid(),0,"other","foreign",actor:"actor-b")));
  Assert.Equal(403,Status(await WritePage(Guid.NewGuid(),0,"revoked","revoked",actor:"actor-revoked")));
  Assert.Equal(403,Status(await CenterSiteDraftEndpoint.Append(Actor("actor-a"),Config(),
     new UnapprovedCenterSiteDraftAuthorization(),"center-a",
     new AppendCenterSiteDraftRequest(Guid.NewGuid(),0,"forbidden","No","No"),default)));
  foreach(var unsafeUrl in new[]{Admin,Read})
   Assert.Equal(503,Status(await WritePage(Guid.NewGuid(),0,"unsafe",config:Config(writer:unsafeUrl))));

  var pages=await List();
  Assert.Equal(200,Status(pages));
  using(var doc=JsonDocument.Parse(JsonSerializer.Serialize(
   Assert.IsAssignableFrom<IValueHttpResult>(pages).Value)))
  {
   var list=doc.RootElement.GetProperty("pages");
   Assert.Single(list.EnumerateArray());
   Assert.Equal(id,list[0].GetProperty("pageId").GetGuid());
   Assert.Equal(2L,list[0].GetProperty("revision").GetInt64());
   Assert.Equal("PRIVATE_DRAFT",list[0].GetProperty("visibility").GetString());
   Assert.False(list[0].GetProperty("isPublished").GetBoolean());
  }
  var history=await History();
  Assert.Equal(200,Status(history));
  using(var doc=JsonDocument.Parse(JsonSerializer.Serialize(
   Assert.IsAssignableFrom<IValueHttpResult>(history).Value)))
  {
   var items=doc.RootElement.GetProperty("revisions");
   Assert.Equal(2,items.GetArrayLength());
   Assert.Equal(2L,items[0].GetProperty("revision").GetInt64());
   Assert.Equal(1L,items[1].GetProperty("revision").GetInt64());
  }
  Assert.Equal(403,Status(await List(actor:"actor-revoked")));
  Assert.Equal(403,Status(await List(center:"center-b",actor:"actor-a")));
  Assert.Equal(200,Status(await List(center:"center-b",actor:"actor-b")));
  Assert.Equal(404,Status(await History(center:"center-b",actor:"actor-b")));
  Assert.Equal(503,Status(await List(config:Config(reader:Write))));
  Assert.Equal(503,Status(await History(config:Config(reader:Admin))));

  await using var db=new NpgsqlConnection(Admin);await db.OpenAsync();
  await using var command=new NpgsqlCommand(
   """
   SELECT
    (SELECT count(*) FROM web.center_pages WHERE center_id='center-a' AND page_id=@id),
    (SELECT count(*) FROM web.page_draft_revisions WHERE center_id='center-a' AND page_id=@id),
    (SELECT count(*) FROM governance.command_audit
      WHERE action_code='CENTER_SITE_DRAFT_REVISION_APPENDED' AND target_id=@text),
    (SELECT count(*) FROM governance.outbox_events
      WHERE event_type='CenterSiteDraftRevisionAppended' AND aggregate_id=@text)
   """,db);
  command.Parameters.AddWithValue("id",id);
  command.Parameters.AddWithValue("text",id.ToString("D"));
  await using var reader=await command.ExecuteReaderAsync();
  Assert.True(await reader.ReadAsync());
  Assert.Equal(1L,reader.GetInt64(0));
  Assert.Equal(2L,reader.GetInt64(1));
  Assert.Equal(2L,reader.GetInt64(2));
  Assert.Equal(2L,reader.GetInt64(3));
 }
}
