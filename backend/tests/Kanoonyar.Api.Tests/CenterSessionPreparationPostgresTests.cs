using System.Security.Claims;
using System.Text.Json;
using Kanoonyar.Api.Programs;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Xunit;
namespace Kanoonyar.Api.Tests;
public sealed class CenterSessionPreparationPostgresTests
{
 private static string? Admin=>Environment.GetEnvironmentVariable("CENTER_DRAFT_INTEGRATION_ADMIN_URL");
 private static string? Read=>Environment.GetEnvironmentVariable("CENTER_DRAFT_INTEGRATION_READ_DATABASE_URL");
 private static string? Write=>Environment.GetEnvironmentVariable("CENTER_SESSION_PREPARATION_INTEGRATION_DATABASE_URL");
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
 private sealed class Grant(bool allowed=true):ICenterSessionPreparationAuthorization
 {
  public ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal p,string c,string op,Guid id,CancellationToken ct)
   =>ValueTask.FromResult(allowed);
 }
 private sealed class Source(bool allowed=true):ICenterSourceCatalogReadAuthorization
 {
  public ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal p,string c,CancellationToken ct)
   =>ValueTask.FromResult(allowed);
 }
 private static ClaimsPrincipal Actor(string subject)=>new(new ClaimsIdentity(
  [new Claim("sub",subject)],"signed-synthetic"));
 private static IConfiguration Config(string? writer=null,string? reader=null)=>
  new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{
   ["CENTER_SESSION_PREPARATION_WRITE_DATABASE_URL"]=writer??Write,
   ["CENTER_DRAFT_READ_DATABASE_URL"]=reader??Read}).Build();
 private static int? Status(IResult r)=>(r as IStatusCodeHttpResult)?.StatusCode;
 private static JsonDocument Json(IResult r)=>JsonDocument.Parse(JsonSerializer.Serialize(
  Assert.IsAssignableFrom<IValueHttpResult>(r).Value,new JsonSerializerOptions(JsonSerializerDefaults.Web)));
 [Fact] public async Task Exact_session_bindings_history_race_rls_and_atomic_audit_are_verified()
 {
  if(!Enabled())return;
  const string center="session-prep-s35",other="session-prep-s35-foreign",actor="s35-editor";
  var classId=Guid.NewGuid();var sessionId=Guid.NewGuid(),foreignClass=Guid.NewGuid(),foreignSession=Guid.NewGuid();
  await using(var db=new NpgsqlConnection(Admin))
  {
   await db.OpenAsync();
   await using var setup=new NpgsqlCommand("""
    INSERT INTO centers.registry(center_id,display_name,is_active,registered_by_subject,registered_at)
    VALUES('session-prep-s35','S35 Center',true,'fixture',now()),
          ('session-prep-s35-foreign','S35 Foreign',true,'fixture',now());
    INSERT INTO centers.memberships(center_id,subject_id,role_code,is_active,recorded_by_subject,recorded_at)
    VALUES('session-prep-s35','s35-editor','fixture',true,'fixture',now()),
          ('session-prep-s35','s35-revoked','fixture',false,'fixture',now()),
          ('session-prep-s35-foreign','s35-other','fixture',true,'fixture',now());
    INSERT INTO learning.center_classes(center_id,class_id,title)
    VALUES('session-prep-s35',@class,'Owned class'),
          ('session-prep-s35-foreign',@foreignclass,'Foreign class');
    INSERT INTO learning.class_sessions(center_id,session_id,class_id,session_title,scheduled_at)
    VALUES('session-prep-s35',@session,@class,'Preparation source session',NULL),
          ('session-prep-s35-foreign',@foreignsession,@foreignclass,'Foreign session',NULL);
    """,db);
   setup.Parameters.AddWithValue("class",classId);
   setup.Parameters.AddWithValue("session",sessionId);
   setup.Parameters.AddWithValue("foreignclass",foreignClass);
   setup.Parameters.AddWithValue("foreignsession",foreignSession);
   await setup.ExecuteNonQueryAsync();
  }
  Task<IResult> Post(long expected,string title="Session outline",Guid? session=null,Guid? cls=null,
   string subject=actor,string c=center,IConfiguration? config=null,
   bool granted=true,bool src=true)=>
   CenterSessionPreparationEndpoint.Append(Actor(subject),config??Config(),new Grant(granted),
    new Source(src),c,new AppendSessionPreparationRequest(session??sessionId,cls??classId,
      expected,title,"Private teaching outline","Private materials list"),default);
  Task<IResult> List(string subject=actor,string c=center,IConfiguration? cfg=null)=>
   CenterSessionPreparationEndpoint.List(Actor(subject),cfg??Config(),new Grant(),
    new Source(),c,null,default);
  Task<IResult> History(Guid s,long? before=null,string subject=actor,string c=center)=>
   CenterSessionPreparationEndpoint.History(Actor(subject),Config(),new Grant(),
    new Source(),c,s,before,default);
  Assert.Equal(403,Status(await Post(0,subject:"s35-revoked")));
  Assert.Equal(403,Status(await Post(0,subject:"s35-other")));
  Assert.Equal(403,Status(await Post(0,granted:false)));
  Assert.Equal(403,Status(await Post(0,src:false)));
  Assert.Equal(503,Status(await Post(0,config:Config(writer:Admin))));
  Assert.Equal(503,Status(await Post(0,config:Config(writer:Read))));
  Assert.Equal(409,Status(await Post(0,session:foreignSession,cls:foreignClass)));
  Assert.Equal(409,Status(await Post(0,cls:foreignClass)));
  Assert.Equal(409,Status(await Post(0,session:Guid.NewGuid())));
  Assert.Equal(200,Status(await Post(0,"V1 genuine")));
  var race=await Task.WhenAll(Post(1,"Variant A"),Post(1,"Variant B"));
  Assert.Single(race,x=>Status(x)==200);
  Assert.Single(race,x=>Status(x)==409);
  Assert.Equal(409,Status(await Post(1,"Stale")));
  using(var value=Json(await List()))
  {
   var cards=value.RootElement.GetProperty("preparations");
   Assert.Single(cards.EnumerateArray());
   var first=cards[0];
   Assert.Equal(sessionId,first.GetProperty("sessionId").GetGuid());
   Assert.Equal(classId,first.GetProperty("classId").GetGuid());
   Assert.Equal("Preparation source session",first.GetProperty("sessionTitle").GetString());
   Assert.Equal(2,first.GetProperty("revision").GetInt64());
   Assert.False(first.GetProperty("deliveryVerified").GetBoolean());
   Assert.False(first.GetProperty("isPublished").GetBoolean());
   Assert.False(first.GetProperty("isAssessed").GetBoolean());
  }
  using(var value=Json(await History(sessionId)))
  {
   var versions=value.RootElement.GetProperty("revisions");
   Assert.Equal(2,versions.GetArrayLength());
   Assert.Equal(2,versions[0].GetProperty("revision").GetInt64());
   Assert.Equal("V1 genuine",versions[1].GetProperty("title").GetString());
   Assert.Equal(classId,value.RootElement.GetProperty("classId").GetGuid());
   Assert.False(value.RootElement.GetProperty("isAttendanceVerified").GetBoolean());
  }
  Assert.Equal(403,Status(await List(subject:"s35-revoked")));
  Assert.Equal(403,Status(await List(subject:"s35-other")));
  Assert.Equal(403,Status(await List(c:other)));
  Assert.Equal(404,Status(await History(Guid.NewGuid())));
  Assert.Equal(503,Status(await List(cfg:Config(reader:Admin))));
  Assert.Equal(503,Status(await List(cfg:Config(reader:Write))));
  await using var admin=new NpgsqlConnection(Admin);await admin.OpenAsync();
  await using(var q=new NpgsqlCommand("""
   SELECT
    (SELECT count(*) FROM learning.session_preparation_revisions WHERE center_id=@center),
    (SELECT count(*) FROM governance.command_audit
     WHERE center_id=@center AND action_code='SESSION_PREPARATION_REVISION_APPENDED'),
    (SELECT count(*) FROM governance.outbox_events
     WHERE event_type='SessionPreparationRevisionAppended' AND aggregate_id=@session)
   """,admin))
  {
   q.Parameters.AddWithValue("center",center);
   q.Parameters.AddWithValue("session",sessionId.ToString("D"));
   await using var rd=await q.ExecuteReaderAsync();
   Assert.True(await rd.ReadAsync());
   Assert.Equal(2L,rd.GetInt64(0));Assert.Equal(2L,rd.GetInt64(1));
   Assert.Equal(2L,rd.GetInt64(2));
  }
  await using(var tamper=new NpgsqlCommand("""
   UPDATE learning.session_preparation_revisions SET teaching_outline='forged'
   WHERE center_id=@center AND session_id=@session
   """,admin))
  {
   tamper.Parameters.AddWithValue("center",center);
   tamper.Parameters.AddWithValue("session",sessionId);
   await Assert.ThrowsAsync<PostgresException>(()=>tamper.ExecuteNonQueryAsync());
  }
 }
}
