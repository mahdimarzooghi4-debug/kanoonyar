using System.Security.Claims;
using System.Text.Json;
using Kanoonyar.Api.Programs;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Xunit;

namespace Kanoonyar.Api.Tests;

/// <summary>Real disposable PostgreSQL; all grants here are synthetic CI only.</summary>
public sealed class CenterCampaignPostgresTests
{
 private static string? Admin=>Environment.GetEnvironmentVariable("CENTER_DRAFT_INTEGRATION_ADMIN_URL");
 private static string? Read=>Environment.GetEnvironmentVariable("CENTER_DRAFT_INTEGRATION_READ_DATABASE_URL");
 private static string? Write=>Environment.GetEnvironmentVariable("CENTER_CAMPAIGN_INTEGRATION_DATABASE_URL");
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
   ["CENTER_CAMPAIGN_WRITE_DATABASE_URL"]=writer??Write,
   ["CENTER_DRAFT_READ_DATABASE_URL"]=reader??Read
  }).Build();
 private static ClaimsPrincipal Actor(string sub)=>
  new(new ClaimsIdentity([new Claim("sub",sub)],"synthetic"));
 private static int? Status(IResult result)=>(result as IStatusCodeHttpResult)?.StatusCode;
 private static JsonDocument Json(IResult result)=>
  JsonDocument.Parse(JsonSerializer.Serialize(Assert.IsAssignableFrom<IValueHttpResult>(result).Value,
    new JsonSerializerOptions(JsonSerializerDefaults.Web)));
 private sealed class Grant:ICenterCampaignAuthorization
 {
  public ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal actor,string center,Guid campaign,
   string operation,CancellationToken ct)=>ValueTask.FromResult(true);
 }
 private sealed class Deny:ICenterCampaignAuthorization
 {
  public ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal actor,string center,Guid campaign,
   string operation,CancellationToken ct)=>ValueTask.FromResult(false);
 }
 [Fact] public async Task Pinned_plan_campaign_is_tenant_safe_versioned_and_never_executed()
 {
  if(!Enabled())return;
  var campaign=Guid.NewGuid();
  var draft=Guid.NewGuid();
  var grant=new Grant();
  // Only the disposable fixture's superuser writes the source-owned plan.
  await using(var admin=new NpgsqlConnection(Admin))
  {
   await admin.OpenAsync();
   await using var seed=new NpgsqlCommand(
    """
    INSERT INTO programs.center_plan_draft_revisions(center_id,draft_id,revision,
       program_id,national_version,local_title,local_activities,actor_subject)
    VALUES('center-a',@draft,1,'national-1',1,'Synthetic local plan','[]'::jsonb,'actor-a')
    """,admin);
   seed.Parameters.AddWithValue("draft",draft);
   await seed.ExecuteNonQueryAsync();
  }
  var planned=new List<PlannedCampaignActivity>{new("workshop","Planned workshop"),
    new("visit","Planned outreach")};
  Task<IResult> Append(Guid id,long expected,Guid pinned,long pinnedRevision,
   string slug="local-campaign",string title="Synthetic campaign",
   string actor="actor-a",string center="center-a",IConfiguration? config=null)=>
   CenterCampaignEndpoint.Append(Actor(actor),config??Config(),grant,center,
     new AppendCenterCampaignRequest(id,expected,slug,pinned,pinnedRevision,
      title,"Community awareness","Area residents",planned),default);
  Task<IResult> List(string center="center-a",string actor="actor-a",IConfiguration? config=null)=>
   CenterCampaignEndpoint.List(Actor(actor),config??Config(),grant,center,null,default);
  Task<IResult> History(string center="center-a",string actor="actor-a",IConfiguration? config=null)=>
   CenterCampaignEndpoint.History(Actor(actor),config??Config(),grant,center,campaign,null,default);

  Assert.Equal(409,Status(await Append(Guid.NewGuid(),0,Guid.NewGuid(),1,"missing-plan")));
  Assert.Equal(409,Status(await Append(Guid.NewGuid(),0,draft,2,"missing-revision")));
  Assert.Equal(403,Status(await Append(Guid.NewGuid(),0,draft,1,"foreign-actor",actor:"actor-b")));
  Assert.Equal(403,Status(await Append(Guid.NewGuid(),0,draft,1,"revoked",actor:"actor-revoked")));
  Assert.Equal(403,Status(await CenterCampaignEndpoint.Append(Actor("actor-a"),Config(),
    new Deny(),"center-a",new AppendCenterCampaignRequest(Guid.NewGuid(),0,"denied",
    draft,1,"Denied","Goal","Audience",planned),default)));
  var first=await Append(campaign,0,draft,1);
  Assert.Equal(200,Status(first));
  using(var json=Json(first))
  {
   var x=json.RootElement;
   Assert.Equal(1L,x.GetProperty("revision").GetInt64());
   Assert.Equal(draft,x.GetProperty("draftId").GetGuid());
   Assert.Equal(1L,x.GetProperty("pinnedPlanRevision").GetInt64());
   Assert.Equal("PRIVATE_DRAFT",x.GetProperty("visibility").GetString());
   Assert.False(x.GetProperty("isPublished").GetBoolean());
   Assert.False(x.GetProperty("isExecuted").GetBoolean());
  }
  var concurrent=await Task.WhenAll(Append(campaign,1,draft,1,title:"Version 2"),
    Append(campaign,1,draft,1,title:"Competing version"));
  Assert.Single(concurrent,x=>Status(x)==200);
  Assert.Single(concurrent,x=>Status(x)==409);
  Assert.Equal(409,Status(await Append(campaign,1,draft,1,title:"stale")));
  Assert.Equal(409,Status(await Append(campaign,2,Guid.NewGuid(),1,title:"rebind")));
  Assert.Equal(409,Status(await Append(Guid.NewGuid(),0,draft,1,title:"slug collision")));
  Assert.Equal(503,Status(await Append(Guid.NewGuid(),0,draft,1,"admin-role",config:Config(writer:Admin))));
  Assert.Equal(503,Status(await Append(Guid.NewGuid(),0,draft,1,"read-role",config:Config(writer:Read))));
  var found=await List();
  Assert.Equal(200,Status(found));
  using(var json=Json(found))
  {
   var rows=json.RootElement.GetProperty("campaigns");
   // Other independent integration suites may persist separate campaigns
   // in the same disposable database; assert this source-owned campaign only.
   var own=Assert.Single(rows.EnumerateArray(),
     row=>row.GetProperty("campaignId").GetGuid()==campaign);
   Assert.Equal(2L,own.GetProperty("revision").GetInt64());
   Assert.False(own.GetProperty("isExecuted").GetBoolean());
  }
  var history=await History();
  Assert.Equal(200,Status(history));
  using(var json=Json(history))
  {
   var rows=json.RootElement.GetProperty("revisions");
   Assert.Equal(2,rows.GetArrayLength());
   Assert.Equal(2L,rows[0].GetProperty("revision").GetInt64());
   Assert.Equal(1L,rows[1].GetProperty("revision").GetInt64());
  }
  Assert.Equal(403,Status(await List(actor:"actor-revoked")));
  Assert.Equal(403,Status(await List(center:"center-b",actor:"actor-a")));
  var foreign=await List(center:"center-b",actor:"actor-b");
  Assert.Equal(200,Status(foreign));
  using(var json=Json(foreign))Assert.Empty(json.RootElement.GetProperty("campaigns").EnumerateArray());
  Assert.Equal(404,Status(await History(center:"center-b",actor:"actor-b")));
  Assert.Equal(503,Status(await List(config:Config(reader:Write))));
  Assert.Equal(503,Status(await History(config:Config(reader:Admin))));

  await using var checkDb=new NpgsqlConnection(Admin);await checkDb.OpenAsync();
  await using var check=new NpgsqlCommand(
   """
   SELECT
    (SELECT count(*) FROM campaigns.center_campaigns WHERE center_id='center-a' AND campaign_id=@campaign),
    (SELECT count(*) FROM campaigns.center_campaign_revisions WHERE center_id='center-a' AND campaign_id=@campaign),
    (SELECT count(*) FROM governance.command_audit
      WHERE action_code='CENTER_CAMPAIGN_DRAFT_REVISION_APPENDED' AND target_id=@id),
    (SELECT count(*) FROM governance.outbox_events
      WHERE event_type='CenterCampaignDraftRevisionAppended' AND aggregate_id=@id),
    (SELECT count(*) FROM governance.command_audit
      WHERE action_code='CENTER_CAMPAIGN_DRAFT_REVISION_APPENDED'
       AND metadata::text LIKE '%Community awareness%')
   """,checkDb);
  check.Parameters.AddWithValue("campaign",campaign);
  check.Parameters.AddWithValue("id",campaign.ToString("D"));
  await using var reader=await check.ExecuteReaderAsync();
  Assert.True(await reader.ReadAsync());
  Assert.Equal(1L,reader.GetInt64(0));
  Assert.Equal(2L,reader.GetInt64(1));
  Assert.Equal(2L,reader.GetInt64(2));
  Assert.Equal(2L,reader.GetInt64(3));
  Assert.Equal(0L,reader.GetInt64(4));
 }
}
