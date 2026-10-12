using System.Security.Claims;
using System.Text.Json;
using Kanoonyar.Api.Programs;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Xunit;

namespace Kanoonyar.Api.Tests;

/// <summary>Real disposable PostgreSQL, test-only synthetic actors, isolated credentials.</summary>
public sealed class CenterSiteDesignPostgresTests
{
 private static string? Admin=>Environment.GetEnvironmentVariable("CENTER_DRAFT_INTEGRATION_ADMIN_URL");
 private static string? Read=>Environment.GetEnvironmentVariable("CENTER_DRAFT_INTEGRATION_READ_DATABASE_URL");
 private static string? Write=>Environment.GetEnvironmentVariable("CENTER_SITE_DESIGN_INTEGRATION_DATABASE_URL");
 private static bool Enabled()
 {
  if(Environment.GetEnvironmentVariable("CENTER_DRAFT_INTEGRATION_REQUIRED")=="1")
  {
   Assert.False(string.IsNullOrWhiteSpace(Admin));
   Assert.False(string.IsNullOrWhiteSpace(Read));
   Assert.False(string.IsNullOrWhiteSpace(Write));
  }
  return !string.IsNullOrWhiteSpace(Admin)&&!string.IsNullOrWhiteSpace(Read)
     &&!string.IsNullOrWhiteSpace(Write);
 }
 private static IConfiguration Config(string? writer=null,string? reader=null)=>
  new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{
    ["CENTER_SITE_DESIGN_WRITE_DATABASE_URL"]=writer??Write,
    ["CENTER_DRAFT_READ_DATABASE_URL"]=reader??Read
  }).Build();
 private static ClaimsPrincipal Actor(string sub)=>
  new(new ClaimsIdentity([new Claim("sub",sub)],"synthetic"));
 private static int? Status(IResult r)=>(r as IStatusCodeHttpResult)?.StatusCode;
 private sealed class Grant:ICenterSiteDesignAuthorization
 {
  public ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal actor,string center,
   string operation,CancellationToken ct)=>ValueTask.FromResult(true);
 }
 [Fact]
 public async Task Design_revisions_are_center_owned_atomic_and_never_published()
 {
  if(!Enabled())return;
  var grant=new Grant();
  Task<IResult> Submit(long expected,string layout,string header="Synthetic private heading",
   string actor="actor-a",string center="center-a",IConfiguration? config=null)=>
   CenterSiteDesignEndpoint.Append(Actor(actor),config??Config(),grant,center,
    new AppendCenterSiteDesignRequest(expected,layout,"FOREST","SYSTEM",header,
      "Synthetic footer",true,true),default);
  Task<IResult> Latest(string actor="actor-a",string center="center-a",IConfiguration? config=null)=>
   CenterSiteDesignEndpoint.Latest(Actor(actor),config??Config(),grant,center,default);
  Task<IResult> History(string actor="actor-a",string center="center-a",IConfiguration? config=null)=>
   CenterSiteDesignEndpoint.History(Actor(actor),config??Config(),grant,center,null,default);
  var empty=await Latest();
  Assert.Equal(200,Status(empty));
  using(var data=JsonDocument.Parse(JsonSerializer.Serialize(
   Assert.IsAssignableFrom<IValueHttpResult>(empty).Value)))
  {
   Assert.Equal(JsonValueKind.Null,data.RootElement.GetProperty("revision").ValueKind);
   Assert.Equal(JsonValueKind.Null,data.RootElement.GetProperty("design").ValueKind);
   Assert.Equal("PRIVATE_DRAFT",data.RootElement.GetProperty("visibility").GetString());
  }
  var inserted=await Submit(0,"HERO");
  Assert.Equal(200,Status(inserted));
  using(var data=JsonDocument.Parse(JsonSerializer.Serialize(
   Assert.IsAssignableFrom<IValueHttpResult>(inserted).Value)))
  {
   Assert.Equal(1L,data.RootElement.GetProperty("revision").GetInt64());
   Assert.Equal("PRIVATE_DRAFT",data.RootElement.GetProperty("visibility").GetString());
   Assert.False(data.RootElement.GetProperty("isPublished").GetBoolean());
  }
  var concurrent=await Task.WhenAll(Submit(1,"EDITORIAL"),
                                   Submit(1,"COMPACT"));
  Assert.Single(concurrent,x=>Status(x)==200);
  Assert.Single(concurrent,x=>Status(x)==409);
  Assert.Equal(409,Status(await Submit(1,"HERO")));
  Assert.Equal(400,Status(await Submit(2,"CUSTOM_CSS")));
  Assert.Equal(403,Status(await Submit(2,"HERO",actor:"actor-b")));
  Assert.Equal(403,Status(await Submit(2,"HERO",actor:"actor-revoked")));
  Assert.Equal(403,Status(await CenterSiteDesignEndpoint.Append(
    Actor("actor-a"),Config(),new UnapprovedCenterSiteDesignAuthorization(),"center-a",
    new AppendCenterSiteDesignRequest(2,"HERO","OCEAN","SYSTEM","No right","",true,false),default)));
  Assert.Equal(503,Status(await Submit(2,"HERO",config:Config(writer:Admin))));
  Assert.Equal(503,Status(await Submit(2,"HERO",config:Config(writer:Read))));
  var current=await Latest();
  Assert.Equal(200,Status(current));
  using(var data=JsonDocument.Parse(JsonSerializer.Serialize(
   Assert.IsAssignableFrom<IValueHttpResult>(current).Value)))
  {
   var root=data.RootElement;
   Assert.Equal(2L,root.GetProperty("revision").GetInt64());
   Assert.Equal(2L,root.GetProperty("design").GetProperty("revision").GetInt64());
   Assert.False(root.GetProperty("design").GetProperty("isPublished").GetBoolean());
  }
  var history=await History();
  Assert.Equal(200,Status(history));
  using(var data=JsonDocument.Parse(JsonSerializer.Serialize(
   Assert.IsAssignableFrom<IValueHttpResult>(history).Value)))
  {
   var root=data.RootElement;
   Assert.Equal(2,root.GetProperty("revisions").GetArrayLength());
   Assert.Equal(2L,root.GetProperty("revisions")[0].GetProperty("revision").GetInt64());
   Assert.Equal(1L,root.GetProperty("revisions")[1].GetProperty("revision").GetInt64());
  }
  Assert.Equal(403,Status(await Latest(actor:"actor-revoked")));
  Assert.Equal(403,Status(await Latest(center:"center-b",actor:"actor-a")));
  var other=await Latest(center:"center-b",actor:"actor-b");
  Assert.Equal(200,Status(other));
  using(var data=JsonDocument.Parse(JsonSerializer.Serialize(
   Assert.IsAssignableFrom<IValueHttpResult>(other).Value)))
    Assert.Equal(JsonValueKind.Null,data.RootElement.GetProperty("design").ValueKind);
  Assert.Equal(503,Status(await Latest(config:Config(reader:Write))));
  Assert.Equal(503,Status(await History(config:Config(reader:Admin))));
  await using var admin=new NpgsqlConnection(Admin);
  await admin.OpenAsync();
  await using var check=new NpgsqlCommand(
    """
    SELECT (SELECT count(*) FROM web.center_site_design_revisions WHERE center_id='center-a'),
           (SELECT count(*) FROM governance.command_audit
              WHERE action_code='CENTER_SITE_DESIGN_REVISION_APPENDED' AND center_id='center-a'),
           (SELECT count(*) FROM governance.outbox_events
              WHERE event_type='CenterSiteDesignRevisionAppended' AND aggregate_id='center-a'),
           (SELECT count(*) FROM governance.command_audit
              WHERE action_code='CENTER_SITE_DESIGN_REVISION_APPENDED'
                AND metadata::text LIKE '%Synthetic private heading%')
    """,admin);
  await using var reader=await check.ExecuteReaderAsync();
  Assert.True(await reader.ReadAsync());
  Assert.Equal(2L,reader.GetInt64(0));
  Assert.Equal(2L,reader.GetInt64(1));
  Assert.Equal(2L,reader.GetInt64(2));
  Assert.Equal(0L,reader.GetInt64(3));
 }
}
