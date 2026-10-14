using System.Security.Claims;
using System.Text.Json;
using Kanoonyar.Api.Programs;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Xunit;
namespace Kanoonyar.Api.Tests;
public sealed class CenterWorkResourceNeedPostgresTests
{
 private static string? Admin=>Environment.GetEnvironmentVariable("CENTER_DRAFT_INTEGRATION_ADMIN_URL");
 private static string? Reader=>Environment.GetEnvironmentVariable("CENTER_DRAFT_INTEGRATION_READ_DATABASE_URL");
 private static string? Writer=>Environment.GetEnvironmentVariable("CENTER_WORK_RESOURCE_NEED_INTEGRATION_DATABASE_URL");
 private static bool Enabled()
 {
  if(Environment.GetEnvironmentVariable("CENTER_DRAFT_INTEGRATION_REQUIRED")=="1")
  {
   Assert.False(string.IsNullOrWhiteSpace(Admin));Assert.False(string.IsNullOrWhiteSpace(Reader));
   Assert.False(string.IsNullOrWhiteSpace(Writer));
  }
  return !string.IsNullOrWhiteSpace(Admin)&&!string.IsNullOrWhiteSpace(Reader)&&!string.IsNullOrWhiteSpace(Writer);
 }
 private sealed class Need(bool allow=true):ICenterWorkResourceNeedAuthorization
 {
  public ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal a,string c,Guid card,string op,CancellationToken ct)
   =>ValueTask.FromResult(allow);
 }
 private sealed class Card(bool allow=true):ICenterWorkCardAuthorization
 {
  public ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal a,string c,string op,Guid card,CancellationToken ct)
   =>ValueTask.FromResult(allow);
 }
 private sealed class Plan(bool allow=true):ILocalPlanDraftReadAuthorization
 {
  public ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal a,string c,CancellationToken ct)
   =>ValueTask.FromResult(allow);
 }
 private static ClaimsPrincipal Actor(string id)=>new(new ClaimsIdentity(
  [new Claim("sub",id)],"synthetic"));
 private static IConfiguration Config(string? write=null,string? read=null)=>
  new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{
   ["CENTER_WORK_RESOURCE_NEED_WRITE_DATABASE_URL"]=write??Writer,
   ["CENTER_DRAFT_READ_DATABASE_URL"]=read??Reader}).Build();
 private static int? Status(IResult r)=>(r as IStatusCodeHttpResult)?.StatusCode;
 private static JsonDocument Json(IResult r)=>JsonDocument.Parse(JsonSerializer.Serialize(
  Assert.IsAssignableFrom<IValueHttpResult>(r).Value,
  new JsonSerializerOptions(JsonSerializerDefaults.Web)));
 [Fact] public async Task Real_work_card_need_revisions_are_tenant_scoped_atomic_and_not_real_fulfillment()
 {
  if(!Enabled())return;
  const string center="center-work-needs-s39",other="center-work-needs-foreign-s39";
  const string actor="s39-owner";
  var draft=Guid.NewGuid();var foreignDraft=Guid.NewGuid();
  var card=Guid.NewGuid();var foreignCard=Guid.NewGuid();var need=Guid.NewGuid();
  await using(var db=new NpgsqlConnection(Admin))
  {
   await db.OpenAsync();
   await using var q=new NpgsqlCommand("""
    INSERT INTO centers.registry(center_id,display_name,is_active,registered_by_subject,registered_at)
    VALUES('center-work-needs-s39','S39 center',true,'fixture',now()),
          ('center-work-needs-foreign-s39','S39 foreign',true,'fixture',now());
    INSERT INTO centers.memberships(center_id,subject_id,role_code,is_active,recorded_by_subject,recorded_at)
    VALUES('center-work-needs-s39','s39-owner','fixture',true,'fixture',now()),
          ('center-work-needs-s39','s39-revoked','fixture',false,'fixture',now()),
          ('center-work-needs-foreign-s39','s39-foreign','fixture',true,'fixture',now());
    INSERT INTO programs.center_plan_draft_revisions
      (center_id,draft_id,revision,program_id,national_version,local_title,local_activities,actor_subject)
    VALUES('center-work-needs-s39',@draft,1,'national-1',1,'S39 plan','[]'::jsonb,'s39-owner'),
          ('center-work-needs-foreign-s39',@foreignDraft,1,'national-1',1,
            'S39 foreign plan','[]'::jsonb,'s39-foreign');
    INSERT INTO operations.center_work_cards
      (center_id,work_card_id,draft_id,pinned_plan_revision,created_by_subject)
    VALUES('center-work-needs-s39',@card,@draft,1,'s39-owner'),
          ('center-work-needs-foreign-s39',@foreignCard,@foreignDraft,1,'s39-foreign');
    INSERT INTO operations.center_work_card_revisions
      (center_id,work_card_id,revision,title,planning_note,changed_by_subject)
    VALUES('center-work-needs-s39',@card,1,'S39 source work','Planning','s39-owner'),
          ('center-work-needs-foreign-s39',@foreignCard,1,'Foreign','Foreign','s39-foreign');
    """,db);
   q.Parameters.AddWithValue("draft",draft);q.Parameters.AddWithValue("foreignDraft",foreignDraft);
   q.Parameters.AddWithValue("card",card);q.Parameters.AddWithValue("foreignCard",foreignCard);
   await q.ExecuteNonQueryAsync();
  }
  Task<IResult> Post(Guid id,long expected,string title="Laptop planning",
   Guid? work=null,string c=center,string subject=actor,IConfiguration? cfg=null,
   bool grant=true,bool cardGrant=true,bool planGrant=true)=>
   CenterWorkResourceNeedEndpoint.Append(Actor(subject),cfg??Config(),
    new Need(grant),new Card(cardGrant),new Plan(planGrant),c,work??card,
    new AppendWorkResourceNeedRequest(id,expected,title,"Not procured",null),default);
  Task<IResult> List(Guid? before=null,Guid? work=null,string c=center,string subject=actor,
   IConfiguration? cfg=null,bool grant=true)=>
   CenterWorkResourceNeedEndpoint.List(Actor(subject),cfg??Config(),
    new Need(grant),new Card(),new Plan(),c,work??card,before,default);
  Task<IResult> History(Guid id,long? before=null,Guid? work=null,string c=center,
   string subject=actor)=>
   CenterWorkResourceNeedEndpoint.History(Actor(subject),Config(),new Need(),new Card(),
    new Plan(),c,work??card,id,before,default);
  Assert.Equal(403,Status(await Post(need,0,subject:"s39-revoked")));
  Assert.Equal(403,Status(await Post(need,0,subject:"s39-foreign")));
  Assert.Equal(403,Status(await Post(need,0,grant:false)));
  Assert.Equal(403,Status(await Post(need,0,cardGrant:false)));
  Assert.Equal(403,Status(await Post(need,0,planGrant:false)));
  Assert.Equal(503,Status(await Post(need,0,cfg:Config(write:Admin))));
  Assert.Equal(503,Status(await Post(need,0,cfg:Config(write:Reader))));
  Assert.Equal(409,Status(await Post(need,0,work:foreignCard)));
  Assert.Equal(409,Status(await Post(need,0,work:Guid.NewGuid())));
  Assert.Equal(200,Status(await Post(need,0,"Need version 1")));
  Assert.Equal(409,Status(await Post(need,0,"Duplicate")));
  var race=await Task.WhenAll(Post(need,1,"Need version 2A"),Post(need,1,"Need version 2B"));
  Assert.Single(race,x=>Status(x)==200);
  Assert.Single(race,x=>Status(x)==409);
  Assert.Equal(409,Status(await Post(need,1,"Stale")));
  using(var data=Json(await History(need)))
  {
   var versions=data.RootElement.GetProperty("revisions");
   Assert.Equal(2,versions.GetArrayLength());
   Assert.Equal("Need version 1",versions[1].GetProperty("resourceLabel").GetString());
   Assert.False(versions[0].GetProperty("isAllocated").GetBoolean());
   Assert.False(versions[0].GetProperty("isFulfilled").GetBoolean());
  }
  // 21 real additional needs: keyset pagination is precise, not fabricated.
  for(var n=0;n<20;n++)
   Assert.Equal(200,Status(await Post(Guid.NewGuid(),0,"Unfulfilled "+n)));
  Guid? next;
  using(var data=Json(await List()))
  {
   Assert.Equal(20,data.RootElement.GetProperty("needs").GetArrayLength());
   Assert.Equal("PRIVATE_PLANNING",data.RootElement.GetProperty("visibility").GetString());
   Assert.False(data.RootElement.GetProperty("isPurchased").GetBoolean());
   next=data.RootElement.GetProperty("nextBeforeNeedId").GetGuid();
  }
  using(var data=Json(await List(before:next)))
  {
   Assert.Single(data.RootElement.GetProperty("needs").EnumerateArray());
   Assert.Equal(JsonValueKind.Null,data.RootElement.GetProperty("nextBeforeNeedId").ValueKind);
  }
  Assert.Equal(403,Status(await List(subject:"s39-revoked")));
  Assert.Equal(403,Status(await List(subject:"s39-foreign")));
  Assert.Equal(403,Status(await List(c:other)));
  Assert.Equal(404,Status(await List(work:foreignCard)));
  Assert.Equal(404,Status(await History(need,work:foreignCard)));
  Assert.Equal(503,Status(await List(cfg:Config(read:Admin))));
  Assert.Equal(503,Status(await List(cfg:Config(read:Writer))));
  await using var check=new NpgsqlConnection(Admin);await check.OpenAsync();
  await using(var q=new NpgsqlCommand("""
   SELECT
    (SELECT count(*) FROM operations.center_work_resource_need_revisions WHERE center_id=@center),
    (SELECT count(*) FROM governance.command_audit WHERE center_id=@center
      AND action_code='WORK_RESOURCE_NEED_REVISION_APPENDED'),
    (SELECT count(*) FROM governance.outbox_events WHERE event_type='WorkResourceNeedRevisionAppended'
      AND (payload->>'center_id')=@center)
   """,check))
  {
   q.Parameters.AddWithValue("center",center);
   await using var reader=await q.ExecuteReaderAsync();
   Assert.True(await reader.ReadAsync());
   Assert.Equal(22L,reader.GetInt64(0));Assert.Equal(22L,reader.GetInt64(1));
   Assert.Equal(22L,reader.GetInt64(2));
  }
  await using(var mutation=new NpgsqlCommand("""
   UPDATE operations.center_work_resource_need_revisions SET planning_detail='fake'
    WHERE center_id=@center AND need_id=@need
   """,check))
  {
   mutation.Parameters.AddWithValue("center",center);mutation.Parameters.AddWithValue("need",need);
   await Assert.ThrowsAsync<PostgresException>(()=>mutation.ExecuteNonQueryAsync());
  }
 }
}
