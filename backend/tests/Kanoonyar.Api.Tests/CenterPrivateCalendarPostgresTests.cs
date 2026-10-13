using System.Security.Claims;
using System.Text.Json;
using Kanoonyar.Api.Programs;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Xunit;
namespace Kanoonyar.Api.Tests;
public sealed class CenterPrivateCalendarPostgresTests
{
 private static string? Admin=>Environment.GetEnvironmentVariable("CENTER_DRAFT_INTEGRATION_ADMIN_URL");
 private static string? Read=>Environment.GetEnvironmentVariable("CENTER_DRAFT_INTEGRATION_READ_DATABASE_URL");
 private static string? Writer=>Environment.GetEnvironmentVariable("CENTER_EVENT_PLAN_INTEGRATION_DATABASE_URL");
 private static bool Enabled(){
  if(Environment.GetEnvironmentVariable("CENTER_DRAFT_INTEGRATION_REQUIRED")=="1"){
   Assert.False(string.IsNullOrWhiteSpace(Admin));Assert.False(string.IsNullOrWhiteSpace(Read));
   Assert.False(string.IsNullOrWhiteSpace(Writer));}
  return !string.IsNullOrWhiteSpace(Admin)&&!string.IsNullOrWhiteSpace(Read)&&!string.IsNullOrWhiteSpace(Writer);
 }
 private sealed class Events(bool allow):ICenterEventPlanAuthorization{
  public ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal a,string c,Guid id,string op,CancellationToken ct)=>
   ValueTask.FromResult(allow&&id==Guid.Empty&&op=="CENTER_PRIVATE_CALENDAR_READ");}
 private sealed class Classes(bool allow):ICenterSourceCatalogReadAuthorization{
  public ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal a,string c,CancellationToken ct)=>ValueTask.FromResult(allow);}
 private static IConfiguration Config(string? cs=null)=>new ConfigurationBuilder()
  .AddInMemoryCollection(new Dictionary<string,string?>{["CENTER_DRAFT_READ_DATABASE_URL"]=cs??Read}).Build();
 private static ClaimsPrincipal Actor(string s)=>new(new ClaimsIdentity([new Claim("sub",s)],"test"));
 private static int? Status(IResult r)=>(r as IStatusCodeHttpResult)?.StatusCode;
 private static JsonDocument Json(IResult r)=>JsonDocument.Parse(JsonSerializer.Serialize(
  Assert.IsAssignableFrom<IValueHttpResult>(r).Value,new JsonSerializerOptions(JsonSerializerDefaults.Web)));
 private static readonly DateTimeOffset Start=DateTimeOffset.Parse("2041-11-01T00:00:00Z");
 private static readonly DateTimeOffset End=DateTimeOffset.Parse("2041-12-01T00:00:00Z");
 [Fact] public async Task Private_schedule_uses_actual_owners_latest_event_revision_and_real_keyset()
 {
  if(!Enabled())return;
  var classId=Guid.NewGuid();var eventId=Guid.NewGuid();var draftId=Guid.NewGuid();
  await using(var db=new NpgsqlConnection(Admin)){
   await db.OpenAsync();
   await using var q=new NpgsqlCommand("""
    INSERT INTO programs.center_plan_draft_revisions(center_id,draft_id,revision,
     program_id,national_version,local_title,local_activities,actor_subject)
    VALUES('center-a',@draft,1,'national-1',1,'S31 plan','[]'::jsonb,'actor-a');
    INSERT INTO learning.center_classes(center_id,class_id,title)
    VALUES('center-a',@class,'Class source');
    INSERT INTO events.center_events(center_id,event_id,event_title,scheduled_at)
    VALUES('center-a',@event,'Genesis title','2026-10-04T10:00:00Z');
    INSERT INTO events.center_event_plan_bindings(center_id,event_id,draft_id,
     pinned_plan_revision,created_by_subject)
    VALUES('center-a',@event,@draft,1,'actor-a');
    INSERT INTO events.center_event_plan_revisions(center_id,event_id,revision,
     title,purpose,location_description,scheduled_at,changed_by_subject)
    VALUES('center-a',@event,1,'OLD DATE','plan','room','2026-10-04T10:00:00Z','actor-a'),
          ('center-a',@event,2,'LATEST EVENT','plan','room','2041-11-14T10:00:00Z','actor-a');
    """,db);
   q.Parameters.AddWithValue("draft",draftId);q.Parameters.AddWithValue("class",classId);
   q.Parameters.AddWithValue("event",eventId);await q.ExecuteNonQueryAsync();
   for(var i=1;i<=22;i++){
    await using var item=new NpgsqlCommand("""
     INSERT INTO learning.class_sessions(center_id,session_id,class_id,session_title,scheduled_at)
     VALUES('center-a',@id,@class,@title,@scheduled)
     """,db);
    item.Parameters.AddWithValue("id",Guid.NewGuid());item.Parameters.AddWithValue("class",classId);
    item.Parameters.AddWithValue("title","Original session "+i);
    item.Parameters.AddWithValue("scheduled",new DateTime(2041,11,i,10,0,0,DateTimeKind.Utc));
    await item.ExecuteNonQueryAsync();
   }
   await using var unknown=new NpgsqlCommand("""
    INSERT INTO learning.class_sessions(center_id,session_id,class_id,session_title,scheduled_at)
    VALUES('center-a',@id,@class,'Unknown date source',NULL)
    """,db);
   unknown.Parameters.AddWithValue("id",Guid.NewGuid());
   unknown.Parameters.AddWithValue("class",classId);await unknown.ExecuteNonQueryAsync();
  }
  Task<IResult> Get(string actor="actor-a",string center="center-a",
   DateTimeOffset? before=null,string? kind=null,Guid? id=null,
   bool eventAllowed=true,bool classAllowed=true,string? cs=null)=>
   CenterPrivateCalendarEndpoint.Get(Actor(actor),Config(cs),new Events(eventAllowed),
    new Classes(classAllowed),center,Start,End,before,kind,id,default);
  Assert.Equal(403,Status(await Get(eventAllowed:false)));
  Assert.Equal(403,Status(await Get(classAllowed:false)));
  Assert.Equal(403,Status(await Get(actor:"actor-revoked")));
  Assert.Equal(403,Status(await Get(actor:"actor-a",center:"center-b")));
  Assert.Equal(503,Status(await Get(cs:Admin)));
  Assert.Equal(503,Status(await Get(cs:Writer)));
  var first=await Get();Assert.Equal(200,Status(first));
  DateTimeOffset cursorAt;string cursorKind;Guid cursorId;
  using(var j=Json(first)){
   var root=j.RootElement;
   Assert.False(root.GetProperty("isExecuted").GetBoolean());
   Assert.Equal(20,root.GetProperty("items").GetArrayLength());
   var records=root.GetProperty("items");
   Assert.DoesNotContain("Unknown date source",root.GetRawText());
   Assert.DoesNotContain("OLD DATE",root.GetRawText());
   var at=root.GetProperty("nextCursor");
   cursorAt=at.GetProperty("scheduledAt").GetDateTimeOffset();
   cursorKind=at.GetProperty("sourceKind").GetString()!;
   cursorId=at.GetProperty("sourceId").GetGuid();
   Assert.Equal(records[19].GetProperty("sourceId").GetGuid(),cursorId);
   for(var i=0;i<20;i++){
    var row=records[i];
    Assert.Equal("PRIVATE_PLAN",row.GetProperty("visibility").GetString());
    Assert.False(row.GetProperty("isPublished").GetBoolean());
    Assert.False(row.GetProperty("isExecuted").GetBoolean());
    Assert.False(row.GetProperty("isAttendanceVerified").GetBoolean());
    if(i>0)Assert.True(records[i-1].GetProperty("scheduledAt").GetDateTimeOffset()<=
     row.GetProperty("scheduledAt").GetDateTimeOffset());
   }
  }
  var second=await Get(before:cursorAt,kind:cursorKind,id:cursorId);
  Assert.Equal(200,Status(second));
  using(var j=Json(second)){
   Assert.Equal(3,j.RootElement.GetProperty("items").GetArrayLength());
   Assert.Equal(JsonValueKind.Null,j.RootElement.GetProperty("nextCursor").ValueKind);
  }
  var aroundEvent=await CenterPrivateCalendarEndpoint.Get(Actor("actor-a"),Config(),
   new Events(true),new Classes(true),"center-a",
   DateTimeOffset.Parse("2041-11-14T00:00:00Z"),DateTimeOffset.Parse("2041-11-15T00:00:00Z"),
   null,null,null,default);
  Assert.Equal(200,Status(aroundEvent));
  using(var j=Json(aroundEvent)){
   var rows=j.RootElement.GetProperty("items");
   Assert.Equal(2,rows.GetArrayLength());
   var evt=Assert.Single(rows.EnumerateArray(),e=>e.GetProperty("sourceKind").GetString()=="CENTER_EVENT");
   Assert.Equal(eventId,evt.GetProperty("sourceId").GetGuid());
   Assert.Equal("LATEST EVENT",evt.GetProperty("title").GetString());
   Assert.Equal(2,evt.GetProperty("planRevision").GetInt64());
   Assert.Equal(draftId,evt.GetProperty("draftId").GetGuid());
   Assert.Equal(1,evt.GetProperty("pinnedPlanRevision").GetInt64());
  }
 }
}
