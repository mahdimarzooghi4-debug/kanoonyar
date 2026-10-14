using System.Security.Claims;
using System.Text.Json;
using Kanoonyar.Api.Programs;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Xunit;

namespace Kanoonyar.Api.Tests;
public sealed class CenterWorkSessionLinkPostgresTests
{
 private static string? Admin=>Environment.GetEnvironmentVariable("CENTER_DRAFT_INTEGRATION_ADMIN_URL");
 private static string? Reader=>Environment.GetEnvironmentVariable("CENTER_DRAFT_INTEGRATION_READ_DATABASE_URL");
 private static string? Writer=>Environment.GetEnvironmentVariable("CENTER_WORK_SESSION_LINK_INTEGRATION_DATABASE_URL");
 private static bool Enabled()
 {
  if(Environment.GetEnvironmentVariable("CENTER_DRAFT_INTEGRATION_REQUIRED")=="1")
  {
   Assert.False(string.IsNullOrWhiteSpace(Admin));
   Assert.False(string.IsNullOrWhiteSpace(Reader));
   Assert.False(string.IsNullOrWhiteSpace(Writer));
  }
  return !string.IsNullOrWhiteSpace(Admin)&&!string.IsNullOrWhiteSpace(Reader)
   &&!string.IsNullOrWhiteSpace(Writer);
 }
 private sealed class Link(bool value=true):IWorkCardSessionLinkAuthorization
 {
  public ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal a,string c,Guid card,
    string operation,CancellationToken ct)=>ValueTask.FromResult(value);
 }
 private sealed class Work(bool value=true):ICenterWorkCardAuthorization
 {
  public ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal a,string c,string op,Guid card,
    CancellationToken ct)=>ValueTask.FromResult(value);
 }
 private sealed class Plan(bool value=true):ILocalPlanDraftReadAuthorization
 {
  public ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal a,string c,CancellationToken ct)
   =>ValueTask.FromResult(value);
 }
 private sealed class Source(bool value=true):ICenterSourceCatalogReadAuthorization
 {
  public ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal a,string c,CancellationToken ct)
   =>ValueTask.FromResult(value);
 }
 private static ClaimsPrincipal Actor(string sub)=>new(new ClaimsIdentity(
  [new Claim("sub",sub)],"synthetic"));
 private static IConfiguration Config(string? write=null,string? read=null)=>
  new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{
   ["CENTER_WORK_SESSION_LINK_WRITE_DATABASE_URL"]=write??Writer,
   ["CENTER_DRAFT_READ_DATABASE_URL"]=read??Reader}).Build();
 private static int? Status(IResult r)=>(r as IStatusCodeHttpResult)?.StatusCode;
 private static JsonDocument Json(IResult r)=>JsonDocument.Parse(JsonSerializer.Serialize(
  Assert.IsAssignableFrom<IValueHttpResult>(r).Value,
  new JsonSerializerOptions(JsonSerializerDefaults.Web)));
 [Fact] public async Task Authenticated_original_card_links_only_actual_same_center_sessions_once()
 {
  if(!Enabled())return;
  const string center="center-card-sessions-s41",foreign="center-card-sessions-foreign-s41";
  var draft=Guid.NewGuid();var foreignDraft=Guid.NewGuid();
  var card=Guid.NewGuid();var foreignCard=Guid.NewGuid();
  var clazz=Guid.NewGuid();var foreignClass=Guid.NewGuid();
  var ids=Enumerable.Range(0,21).Select(_=>Guid.NewGuid()).ToList();
  var foreignSession=Guid.NewGuid();
  await using(var db=new NpgsqlConnection(Admin))
  {
   await db.OpenAsync();
   await using(var seed=new NpgsqlCommand("""
    INSERT INTO centers.registry(center_id,display_name,is_active,registered_by_subject,registered_at)
    VALUES('center-card-sessions-s41','S41',true,'fixture',now()),
          ('center-card-sessions-foreign-s41','S41 other',true,'fixture',now());
    INSERT INTO centers.memberships(center_id,subject_id,role_code,is_active,recorded_by_subject,recorded_at)
    VALUES('center-card-sessions-s41','s41-owner','fixture',true,'fixture',now()),
          ('center-card-sessions-s41','s41-revoked','fixture',false,'fixture',now()),
          ('center-card-sessions-foreign-s41','s41-foreign','fixture',true,'fixture',now());
    INSERT INTO programs.center_plan_draft_revisions
      (center_id,draft_id,revision,program_id,national_version,local_title,local_activities,actor_subject)
    VALUES('center-card-sessions-s41',@draft,1,'national-1',1,'S41 local plan','[]'::jsonb,'s41-owner'),
       ('center-card-sessions-foreign-s41',@foreignDraft,1,'national-1',1,'Foreign plan','[]'::jsonb,'s41-foreign');
    INSERT INTO operations.center_work_cards
     (center_id,work_card_id,draft_id,pinned_plan_revision,created_by_subject)
    VALUES('center-card-sessions-s41',@card,@draft,1,'s41-owner'),
       ('center-card-sessions-foreign-s41',@foreignCard,@foreignDraft,1,'s41-foreign');
    INSERT INTO operations.center_work_card_revisions
     (center_id,work_card_id,revision,title,planning_note,changed_by_subject)
    VALUES('center-card-sessions-s41',@card,1,'Local task','Planning','s41-owner'),
       ('center-card-sessions-foreign-s41',@foreignCard,1,'Foreign task','Foreign','s41-foreign');
    INSERT INTO learning.center_classes(center_id,class_id,title)
    VALUES('center-card-sessions-s41',@class,'Real class'),
      ('center-card-sessions-foreign-s41',@foreignClass,'Foreign class');
    INSERT INTO learning.class_sessions(center_id,session_id,class_id,session_title,scheduled_at)
    VALUES('center-card-sessions-foreign-s41',@foreignSession,@foreignClass,'SECRET foreign session',NULL);
    """,db))
   {
    seed.Parameters.AddWithValue("draft",draft);
    seed.Parameters.AddWithValue("foreignDraft",foreignDraft);
    seed.Parameters.AddWithValue("card",card);
    seed.Parameters.AddWithValue("foreignCard",foreignCard);
    seed.Parameters.AddWithValue("class",clazz);
    seed.Parameters.AddWithValue("foreignClass",foreignClass);
    seed.Parameters.AddWithValue("foreignSession",foreignSession);
    await seed.ExecuteNonQueryAsync();
   }
   foreach(var id in ids)
   {
    await using var insert=new NpgsqlCommand("""
     INSERT INTO learning.class_sessions(center_id,session_id,class_id,session_title,scheduled_at)
     VALUES('center-card-sessions-s41',@session,@class,@name,NULL)
     """,db);
    insert.Parameters.AddWithValue("session",id);
    insert.Parameters.AddWithValue("class",clazz);
    insert.Parameters.AddWithValue("name","Actual planned lesson "+id);
    await insert.ExecuteNonQueryAsync();
   }
  }
  Task<IResult> Post(Guid session,Guid? work=null,string actor="s41-owner",
   IConfiguration? config=null,bool link=true,bool cardRight=true,bool plan=true,bool source=true)=>
   CenterWorkSessionLinkEndpoint.Append(Actor(actor),config??Config(),
    new Link(link),new Work(cardRight),new Plan(plan),new Source(source),center,work??card,
    new AppendWorkSessionLinkRequest(session),default);
  Task<IResult> List(Guid? before=null,Guid? work=null,string actor="s41-owner",
   IConfiguration? config=null,bool link=true,bool cardRight=true,bool plan=true,bool source=true)=>
   CenterWorkSessionLinkEndpoint.List(Actor(actor),config??Config(),
    new Link(link),new Work(cardRight),new Plan(plan),new Source(source),center,work??card,
    before,default);
  Assert.Equal(403,Status(await Post(ids[0],actor:"s41-revoked")));
  Assert.Equal(403,Status(await Post(ids[0],actor:"s41-foreign")));
  Assert.Equal(403,Status(await Post(ids[0],link:false)));
  Assert.Equal(403,Status(await Post(ids[0],cardRight:false)));
  Assert.Equal(403,Status(await Post(ids[0],plan:false)));
  Assert.Equal(403,Status(await Post(ids[0],source:false)));
  Assert.Equal(503,Status(await Post(ids[0],config:Config(write:Admin))));
  Assert.Equal(503,Status(await Post(ids[0],config:Config(write:Reader))));
  Assert.Equal(409,Status(await Post(foreignSession)));
  Assert.Equal(409,Status(await Post(Guid.NewGuid())));
  Assert.Equal(409,Status(await Post(ids[0],work:foreignCard)));
  var concurrent=await Task.WhenAll(Post(ids[0]),Post(ids[0]));
  Assert.Single(concurrent,x=>Status(x)==200);
  Assert.Single(concurrent,x=>Status(x)==409);
  foreach(var id in ids.Skip(1))
   Assert.Equal(200,Status(await Post(id)));
  Assert.Equal(409,Status(await Post(ids[0])));
  Guid? cursor;
  using(var result=Json(await List()))
  {
   var root=result.RootElement;
   var records=root.GetProperty("links");
   Assert.Equal(20,records.GetArrayLength());
   Assert.False(root.GetProperty("isExecuted").GetBoolean());
   Assert.False(root.GetProperty("isAttendanceVerified").GetBoolean());
   Assert.All(records.EnumerateArray(),x=>{
    Assert.Equal("Real class",x.GetProperty("classTitle").GetString());
    Assert.Equal(JsonValueKind.Null,x.GetProperty("scheduledAt").ValueKind);
    Assert.False(x.GetProperty("isDeliveryVerified").GetBoolean());
    Assert.DoesNotContain("SECRET foreign session",x.GetRawText());
   });
   cursor=root.GetProperty("nextBeforeSessionId").GetGuid();
  }
  using(var second=Json(await List(before:cursor)))
  {
   Assert.Single(second.RootElement.GetProperty("links").EnumerateArray());
   Assert.Equal(JsonValueKind.Null,
    second.RootElement.GetProperty("nextBeforeSessionId").ValueKind);
  }
  // Missing live membership deliberately hides the existence of the private work card.
  Assert.Equal(404,Status(await List(actor:"s41-revoked")));
  Assert.Equal(404,Status(await List(actor:"s41-foreign")));
  Assert.Equal(403,Status(await List(link:false)));
  Assert.Equal(403,Status(await List(cardRight:false)));
  Assert.Equal(403,Status(await List(plan:false)));
  Assert.Equal(403,Status(await List(source:false)));
  Assert.Equal(404,Status(await List(work:foreignCard)));
  Assert.Equal(503,Status(await List(config:Config(read:Admin))));
  Assert.Equal(503,Status(await List(config:Config(read:Writer))));
  await using var check=new NpgsqlConnection(Admin);await check.OpenAsync();
  await using(var q=new NpgsqlCommand("""
   SELECT
    (SELECT count(*) FROM operations.work_card_session_links WHERE center_id=@center),
    (SELECT count(*) FROM governance.command_audit WHERE center_id=@center
     AND action_code='PRIVATE_WORK_SESSION_LINK_APPENDED'),
    (SELECT count(*) FROM governance.outbox_events WHERE event_type='PrivateWorkSessionLinkAppended'
     AND payload->>'center_id'=@center)
   """,check))
  {
   q.Parameters.AddWithValue("center",center);
   await using var reader=await q.ExecuteReaderAsync();
   Assert.True(await reader.ReadAsync());
   Assert.Equal(21L,reader.GetInt64(0));
   Assert.Equal(21L,reader.GetInt64(1));
   Assert.Equal(21L,reader.GetInt64(2));
  }
  await using(var tamper=new NpgsqlCommand("""
   UPDATE operations.work_card_session_links SET session_id=@foreign
    WHERE center_id=@center AND work_card_id=@card
   """,check))
  {
   tamper.Parameters.AddWithValue("foreign",foreignSession);
   tamper.Parameters.AddWithValue("center",center);
   tamper.Parameters.AddWithValue("card",card);
   await Assert.ThrowsAsync<PostgresException>(()=>tamper.ExecuteNonQueryAsync());
  }
 }
}
