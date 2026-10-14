using System.Security.Claims;
using System.Text.Json;
using Kanoonyar.Api.Programs;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Xunit;
namespace Kanoonyar.Api.Tests;
public sealed class CenterWorkCardPostgresTests
{
 private static string? Admin=>Environment.GetEnvironmentVariable("CENTER_DRAFT_INTEGRATION_ADMIN_URL");
 private static string? Read=>Environment.GetEnvironmentVariable("CENTER_DRAFT_INTEGRATION_READ_DATABASE_URL");
 private static string? Write=>Environment.GetEnvironmentVariable("CENTER_WORK_CARD_INTEGRATION_DATABASE_URL");
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
 private sealed class Grant(bool allow=true):ICenterWorkCardAuthorization
 {
  public ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal a,string c,string operation,Guid card,CancellationToken ct)=>
   ValueTask.FromResult(allow);
 }
 private sealed class Source(bool allow=true):ILocalPlanDraftReadAuthorization
 {
  public ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal a,string c,CancellationToken ct)=>
   ValueTask.FromResult(allow);
 }
 private static ClaimsPrincipal Actor(string s)=>new(new ClaimsIdentity([new Claim("sub",s)],"test"));
 private static IConfiguration Config(string? write=null,string? read=null)=>new ConfigurationBuilder()
  .AddInMemoryCollection(new Dictionary<string,string?>{
   ["CENTER_WORK_CARD_WRITE_DATABASE_URL"]=write??Write,
   ["CENTER_DRAFT_READ_DATABASE_URL"]=read??Read}).Build();
 private static int? Status(IResult r)=>(r as IStatusCodeHttpResult)?.StatusCode;
 private static JsonDocument Json(IResult r)=>JsonDocument.Parse(JsonSerializer.Serialize(
  Assert.IsAssignableFrom<IValueHttpResult>(r).Value,new JsonSerializerOptions(JsonSerializerDefaults.Web)));
 [Fact] public async Task Source_pinned_work_cards_have_live_scope_race_safety_and_immutable_history()
 {
  if(!Enabled())return;
  const string center="center-work-s33";
  const string other="center-work-s33-foreign";
  const string actor="s33-actor";
  var draft=Guid.NewGuid();var foreign=Guid.NewGuid();
  await using(var db=new NpgsqlConnection(Admin))
  {
   await db.OpenAsync();
   await using var setup=new NpgsqlCommand("""
    INSERT INTO centers.registry(center_id,display_name,is_active,registered_by_subject,registered_at)
    VALUES('center-work-s33','S33 Work Center',true,'fixture',now()),
          ('center-work-s33-foreign','S33 Foreign Center',true,'fixture',now());
    INSERT INTO centers.memberships(center_id,subject_id,role_code,is_active,recorded_by_subject,recorded_at)
    VALUES('center-work-s33','s33-actor','fixture',true,'fixture',now()),
          ('center-work-s33','s33-revoked','fixture',false,'fixture',now()),
          ('center-work-s33-foreign','s33-other','fixture',true,'fixture',now());
    INSERT INTO programs.center_plan_draft_revisions(center_id,draft_id,revision,
      program_id,national_version,local_title,local_activities,actor_subject)
    VALUES('center-work-s33',@draft,1,'national-1',1,'S33 source plan','[]'::jsonb,'s33-actor'),
          ('center-work-s33',@draft,2,'national-1',1,'S33 latest different','[]'::jsonb,'s33-actor'),
          ('center-work-s33-foreign',@foreign,1,'national-1',1,'Foreign','[]'::jsonb,'s33-other');
    """,db);
   setup.Parameters.AddWithValue("draft",draft);
   setup.Parameters.AddWithValue("foreign",foreign);
   await setup.ExecuteNonQueryAsync();
  }
  var card=Guid.NewGuid();
  Task<IResult> Save(Guid id,long expected,Guid plan,long pinned,string title,
   string subject=actor,string c=center,IConfiguration? conf=null,
   bool grant=true,bool source=true)=>
   CenterWorkCardEndpoint.Append(Actor(subject),conf??Config(),new Grant(grant),new Source(source),
    c,new AppendCenterWorkCardRequest(id,expected,plan,pinned,title,"Private note",null),default);
  Task<IResult> List(string subject=actor,string c=center,IConfiguration? cfg=null)=>
   CenterWorkCardEndpoint.List(Actor(subject),cfg??Config(),new Grant(),new Source(),c,null,default);
  Task<IResult> History(Guid id,long? before=null,string subject=actor,string c=center)=>
   CenterWorkCardEndpoint.History(Actor(subject),Config(),new Grant(),new Source(),c,id,before,default);
  Assert.Equal(403,Status(await Save(card,0,draft,1,"Deny",subject:"s33-revoked")));
  Assert.Equal(403,Status(await Save(card,0,draft,1,"Deny",subject:"s33-other")));
  Assert.Equal(403,Status(await Save(card,0,draft,1,"Deny",grant:false)));
  Assert.Equal(403,Status(await Save(card,0,draft,1,"Deny",source:false)));
  Assert.Equal(503,Status(await Save(card,0,draft,1,"Bad SQL",conf:Config(write:Admin))));
  Assert.Equal(503,Status(await Save(card,0,draft,1,"Bad SQL",conf:Config(write:Read))));
  Assert.Equal(409,Status(await Save(card,0,foreign,1,"Cross-center pin")));
  Assert.Equal(409,Status(await Save(card,0,draft,3,"Missing source revision")));
  Assert.Equal(200,Status(await Save(card,0,draft,1,"Pinned v1")));
  Assert.Equal(409,Status(await Save(card,1,draft,2,"Repin not allowed")));
  var race=await Task.WhenAll(Save(card,1,draft,1,"Variant A"),Save(card,1,draft,1,"Variant B"));
  Assert.Single(race,x=>Status(x)==200);
  Assert.Single(race,x=>Status(x)==409);
  Assert.Equal(409,Status(await Save(card,1,draft,1,"Stale")));
  using(var data=Json(await List()))
  {
   var rows=data.RootElement.GetProperty("cards");
   Assert.Single(rows.EnumerateArray());
   var first=rows[0];
   Assert.Equal(card,first.GetProperty("workCardId").GetGuid());
   Assert.Equal(draft,first.GetProperty("draftId").GetGuid());
   Assert.Equal(1,first.GetProperty("pinnedPlanRevision").GetInt64());
   Assert.Equal(2,first.GetProperty("revision").GetInt64());
   Assert.Equal("UNKNOWN",first.GetProperty("executionStatus").GetString());
   Assert.False(first.GetProperty("isExecuted").GetBoolean());
   Assert.Equal(JsonValueKind.Null,first.GetProperty("plannedDueAt").ValueKind);
  }
  using(var data=Json(await History(card)))
  {
   var rows=data.RootElement.GetProperty("revisions");
   Assert.Equal(2,rows.GetArrayLength());
   Assert.Equal(2,rows[0].GetProperty("revision").GetInt64());
   Assert.Equal("Pinned v1",rows[1].GetProperty("title").GetString());
   Assert.Equal(JsonValueKind.Null,data.RootElement.GetProperty("nextBeforeRevision").ValueKind);
   Assert.Equal(1,data.RootElement.GetProperty("pinnedPlanRevision").GetInt64());
  }
  Assert.Equal(403,Status(await List(subject:"s33-revoked")));
  Assert.Equal(403,Status(await List(c:other)));
  Assert.Equal(404,Status(await History(Guid.NewGuid())));
  Assert.Equal(503,Status(await List(cfg:Config(read:Admin))));
  Assert.Equal(503,Status(await List(cfg:Config(read:Write))));
  await using var check=new NpgsqlConnection(Admin);await check.OpenAsync();
  await using var q=new NpgsqlCommand("""
   SELECT
     (SELECT count(*) FROM operations.center_work_card_revisions WHERE center_id=@center),
     (SELECT count(*) FROM governance.command_audit WHERE center_id=@center
       AND action_code='CENTER_WORK_CARD_REVISION_APPENDED'),
     (SELECT count(*) FROM governance.outbox_events WHERE event_type='CenterWorkCardRevisionAppended'
       AND aggregate_id=@id)
   """,check);
  q.Parameters.AddWithValue("center",center);
  q.Parameters.AddWithValue("id",card.ToString("D"));
  await using(var rd=await q.ExecuteReaderAsync())
  {
   Assert.True(await rd.ReadAsync());
   Assert.Equal(2L,rd.GetInt64(0));Assert.Equal(2L,rd.GetInt64(1));
   Assert.Equal(2L,rd.GetInt64(2));
  }
  await using var attack=new NpgsqlCommand("""
   UPDATE operations.center_work_card_revisions SET title='forged' WHERE center_id=@center AND work_card_id=@id
   """,check);
  attack.Parameters.AddWithValue("center",center);
  attack.Parameters.AddWithValue("id",card);
  await Assert.ThrowsAsync<PostgresException>(()=>attack.ExecuteNonQueryAsync());
 }
}
