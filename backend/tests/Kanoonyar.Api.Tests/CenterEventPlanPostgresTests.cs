using System.Security.Claims;
using System.Text.Json;
using Kanoonyar.Api.Programs;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Xunit;

namespace Kanoonyar.Api.Tests;
public sealed class CenterEventPlanPostgresTests
{
 private static string? Admin=>Environment.GetEnvironmentVariable("CENTER_DRAFT_INTEGRATION_ADMIN_URL");
 private static string? Read=>Environment.GetEnvironmentVariable("CENTER_DRAFT_INTEGRATION_READ_DATABASE_URL");
 private static string? Write=>Environment.GetEnvironmentVariable("CENTER_EVENT_PLAN_INTEGRATION_DATABASE_URL");
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
   ["CENTER_EVENT_PLAN_WRITE_DATABASE_URL"]=writer??Write,
   ["CENTER_DRAFT_READ_DATABASE_URL"]=reader??Read
  }).Build();
 private static ClaimsPrincipal Actor(string sub)=>new(new ClaimsIdentity([new Claim("sub",sub)],"synthetic"));
 private static int? Status(IResult r)=>(r as IStatusCodeHttpResult)?.StatusCode;
 private static JsonDocument Json(IResult result)=>JsonDocument.Parse(
  JsonSerializer.Serialize(Assert.IsAssignableFrom<IValueHttpResult>(result).Value,
   new JsonSerializerOptions(JsonSerializerDefaults.Web)));
 private sealed class Grant(bool allowed):ICenterEventPlanAuthorization
 {
  public ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal actor,string centerId,Guid eventId,
   string operation,CancellationToken ct)=>ValueTask.FromResult(allowed);
 }
 [Fact] public async Task Canonical_source_event_is_private_versioned_tenant_scoped_and_audited()
 {
  if(!Enabled())return;
  var id=Guid.NewGuid();var plan=Guid.NewGuid();var yes=new Grant(true);
  await using(var db=new NpgsqlConnection(Admin))
  {
   await db.OpenAsync();
   await using var seed=new NpgsqlCommand("""
    INSERT INTO programs.center_plan_draft_revisions(center_id,draft_id,revision,program_id,
      national_version,local_title,local_activities,actor_subject)
    VALUES('center-a',@draft,1,'national-1',1,'Event plan source','[]'::jsonb,'actor-a')
    """,db);
   seed.Parameters.AddWithValue("draft",plan);await seed.ExecuteNonQueryAsync();
  }
  Task<IResult> Append(Guid eventId,long expected,Guid source,long version,
    string title="Planned event",string actor="actor-a",string center="center-a",
    IConfiguration? config=null,ICenterEventPlanAuthorization? permission=null)=>
   CenterEventPlanEndpoint.Append(Actor(actor),config??Config(),permission??yes,center,
    new AppendCenterEventPlanRequest(eventId,expected,source,version,title,"Community purpose",
     "Public venue",DateTimeOffset.Parse("2026-11-10T10:00:00Z")),default);
  Task<IResult> List(string actor="actor-a",string center="center-a",IConfiguration? config=null)=>
   CenterEventPlanEndpoint.List(Actor(actor),config??Config(),yes,center,null,default);
  Task<IResult> History(string actor="actor-a",string center="center-a",IConfiguration? config=null)=>
   CenterEventPlanEndpoint.History(Actor(actor),config??Config(),yes,center,id,null,default);
  Assert.Equal(409,Status(await Append(Guid.NewGuid(),0,Guid.NewGuid(),1)));
  Assert.Equal(409,Status(await Append(Guid.NewGuid(),0,plan,2)));
  Assert.Equal(403,Status(await Append(Guid.NewGuid(),0,plan,1,actor:"actor-revoked")));
  Assert.Equal(403,Status(await Append(Guid.NewGuid(),0,plan,1,permission:new Grant(false))));
  Assert.Equal(503,Status(await Append(Guid.NewGuid(),0,plan,1,config:Config(writer:Admin))));
  Assert.Equal(503,Status(await Append(Guid.NewGuid(),0,plan,1,config:Config(writer:Read))));
  Assert.Equal(200,Status(await Append(id,0,plan,1)));
  var raced=await Task.WhenAll(Append(id,1,plan,1,"Second revision"),
    Append(id,1,plan,1,"Competing revision"));
  Assert.Single(raced,x=>Status(x)==200);
  Assert.Single(raced,x=>Status(x)==409);
  Assert.Equal(409,Status(await Append(id,1,plan,1,"Stale")));
  Assert.Equal(409,Status(await Append(id,2,Guid.NewGuid(),1,"Rebound")));
  var list=await List();
  Assert.Equal(200,Status(list));
  using(var j=Json(list))
  {
   var row=Assert.Single(j.RootElement.GetProperty("events").EnumerateArray(),
    r=>r.GetProperty("eventId").GetGuid()==id);
   Assert.Equal(2L,row.GetProperty("revision").GetInt64());
   Assert.Equal(plan,row.GetProperty("draftId").GetGuid());
   Assert.False(row.GetProperty("isPublished").GetBoolean());
   Assert.False(row.GetProperty("isExecuted").GetBoolean());
   Assert.False(row.GetProperty("isRegistrationOpen").GetBoolean());
  }
  var history=await History();Assert.Equal(200,Status(history));
  using(var j=Json(history))
  {
   var rows=j.RootElement.GetProperty("revisions");
   Assert.Equal(2,rows.GetArrayLength());
   Assert.Equal(2L,rows[0].GetProperty("revision").GetInt64());
   Assert.Equal(1L,rows[1].GetProperty("revision").GetInt64());
  }
  Assert.Equal(403,Status(await List(actor:"actor-revoked")));
  Assert.Equal(403,Status(await List(actor:"actor-a",center:"center-b")));
  Assert.Equal(200,Status(await List(actor:"actor-b",center:"center-b")));
  Assert.Equal(404,Status(await History(actor:"actor-b",center:"center-b")));
  Assert.Equal(503,Status(await List(config:Config(reader:Admin))));
  Assert.Equal(503,Status(await List(config:Config(reader:Write))));
  await using var checkDb=new NpgsqlConnection(Admin);await checkDb.OpenAsync();
  await using var check=new NpgsqlCommand("""
   SELECT
    (SELECT count(*) FROM events.center_events WHERE center_id='center-a' AND event_id=@event),
    (SELECT count(*) FROM events.center_event_plan_revisions WHERE center_id='center-a' AND event_id=@event),
    (SELECT count(*) FROM governance.command_audit
     WHERE action_code='CENTER_EVENT_PLAN_REVISION_APPENDED' AND target_id=@id),
    (SELECT count(*) FROM governance.outbox_events
     WHERE event_type='CenterEventPlanRevisionAppended' AND aggregate_id=@id),
    (SELECT event_title FROM events.center_events WHERE center_id='center-a' AND event_id=@event)
   """,checkDb);
  check.Parameters.AddWithValue("event",id);check.Parameters.AddWithValue("id",id.ToString("D"));
  await using var reader=await check.ExecuteReaderAsync();
  Assert.True(await reader.ReadAsync());
  Assert.Equal(1L,reader.GetInt64(0));Assert.Equal(2L,reader.GetInt64(1));
  Assert.Equal(2L,reader.GetInt64(2));Assert.Equal(2L,reader.GetInt64(3));
  Assert.Equal("Planned event",reader.GetString(4));
 }
}
