using System.Security.Claims;
using System.Text.Json;
using Kanoonyar.Api.Programs;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Xunit;
namespace Kanoonyar.Api.Tests;

public sealed class CenterEventSourceDossierPostgresTests
{
 private static string? Admin=>Environment.GetEnvironmentVariable("CENTER_DRAFT_INTEGRATION_ADMIN_URL");
 private static string? Read=>Environment.GetEnvironmentVariable("CENTER_DRAFT_INTEGRATION_READ_DATABASE_URL");
 private static string? Writer=>Environment.GetEnvironmentVariable("CENTER_EVENT_PLAN_INTEGRATION_DATABASE_URL");
 private static bool Enabled()
 {
  if(Environment.GetEnvironmentVariable("CENTER_DRAFT_INTEGRATION_REQUIRED")=="1")
  {
   Assert.False(string.IsNullOrWhiteSpace(Admin));
   Assert.False(string.IsNullOrWhiteSpace(Read));
   Assert.False(string.IsNullOrWhiteSpace(Writer));
  }
  return !string.IsNullOrWhiteSpace(Admin)&&!string.IsNullOrWhiteSpace(Read)&&!string.IsNullOrWhiteSpace(Writer);
 }
 private sealed class EventGrant(bool allowed):ICenterEventPlanAuthorization
 {
  public ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal a,string c,Guid e,string operation,CancellationToken ct)=>
   ValueTask.FromResult(allowed && operation=="EVENT_SOURCE_DOSSIER_READ");
 }
 private sealed class ReportGrant(bool allowed):ICenterActivityReportReadAuthorization
 {
  public ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal a,string c,CancellationToken ct)=>
   ValueTask.FromResult(allowed);
 }
 private static ClaimsPrincipal Actor(string actor)=>new(new ClaimsIdentity([new Claim("sub",actor)],"test"));
 private static IConfiguration Config(string? cs=null)=>new ConfigurationBuilder()
  .AddInMemoryCollection(new Dictionary<string,string?>{["CENTER_DRAFT_READ_DATABASE_URL"]=cs??Read}).Build();
 private static int? Status(IResult r)=>(r as IStatusCodeHttpResult)?.StatusCode;
 private static JsonDocument Json(IResult r)=>JsonDocument.Parse(JsonSerializer.Serialize(
  Assert.IsAssignableFrom<IValueHttpResult>(r).Value,new JsonSerializerOptions(JsonSerializerDefaults.Web)));
 [Fact] public async Task Actual_typed_event_reports_are_scoped_to_original_pinned_source_and_paginated()
 {
  if(!Enabled())return;
  var plannedEvent=Guid.NewGuid();var otherEvent=Guid.NewGuid();
  var plan=Guid.NewGuid();var otherPlan=Guid.NewGuid();var link=Guid.NewGuid();
  var otherLink=Guid.NewGuid();var issuance=Guid.NewGuid();
  await using(var db=new NpgsqlConnection(Admin))
  {
   await db.OpenAsync();
   await using(var setup=new NpgsqlCommand("""
    INSERT INTO programs.center_plan_draft_revisions(center_id,draft_id,revision,
     program_id,national_version,local_title,local_activities,actor_subject)
    VALUES('center-a',@plan,1,'national-1',1,'Event pinned plan','[]'::jsonb,'actor-a'),
          ('center-a',@other,1,'national-1',1,'Other plan','[]'::jsonb,'actor-a');
    INSERT INTO events.center_events(center_id,event_id,event_title,scheduled_at)
    VALUES('center-a',@event,'Original immutable planned title','2026-11-10T10:00:00Z'),
          ('center-a',@otherEvent,'Different source event',NULL);
    INSERT INTO events.center_event_plan_bindings(center_id,event_id,draft_id,
     pinned_plan_revision,created_by_subject)
    VALUES('center-a',@event,@plan,1,'actor-a');
    INSERT INTO events.center_event_plan_revisions(center_id,event_id,revision,
     title,purpose,location_description,scheduled_at,changed_by_subject)
    VALUES('center-a',@event,1,'V1','Plan only','Hall 1','2026-11-10T10:00:00Z','actor-a'),
          ('center-a',@event,2,'V2 planned title','Plan only','Hall 2','2026-11-12T11:00:00Z','actor-a');
    """,db))
   {
    setup.Parameters.AddWithValue("plan",plan);
    setup.Parameters.AddWithValue("other",otherPlan);
    setup.Parameters.AddWithValue("event",plannedEvent);
    setup.Parameters.AddWithValue("otherEvent",otherEvent);
    await setup.ExecuteNonQueryAsync();
   }
   await using(var issue=new NpgsqlCommand("""
    INSERT INTO programs.national_program_issuances(issuance_id,center_id,
     program_id,national_version,issued_by_subject,issued_at)
    VALUES(@issue,'center-a','national-1',1,'fixture',now())
    ON CONFLICT(center_id,program_id,national_version) DO NOTHING;
    SELECT issuance_id FROM programs.national_program_issuances
    WHERE center_id='center-a' AND program_id='national-1' AND national_version=1
    """,db))
   {
    issue.Parameters.AddWithValue("issue",issuance);
    issuance=Assert.IsType<Guid>(await issue.ExecuteScalarAsync());
   }
   foreach(var (linkId,draftId) in new[]{(link,plan),(otherLink,otherPlan)})
   {
    await using var attach=new NpgsqlCommand("""
     SELECT programs.record_center_plan_issuance_link(@link,'center-a',@draft,1,
       @issue,'national-1',1,'fixture',@audit,@outbox,'s30-owner-link')
     """,db);
    attach.Parameters.AddWithValue("link",linkId);
    attach.Parameters.AddWithValue("draft",draftId);
    attach.Parameters.AddWithValue("issue",issuance);
    attach.Parameters.AddWithValue("audit",Guid.NewGuid());
    attach.Parameters.AddWithValue("outbox",Guid.NewGuid());
    Assert.Equal(linkId,await attach.ExecuteScalarAsync());
   }
   // 22 real, distinct original source-owned records: two keyset pages.
   for(var i=0;i<22;i++)
   {
    await using var cmd=new NpgsqlCommand("""
     SELECT programs.record_center_sourced_activity_report(@report,'center-a',@link,@draft,
      'CENTER_EVENT',@source,@evidence,'Unverified event claim',NULL,'actor-a',
      @audit,@outbox,'s30-real-original')
     """,db);
    var id=Guid.NewGuid();
    cmd.Parameters.AddWithValue("report",id);cmd.Parameters.AddWithValue("link",link);
    cmd.Parameters.AddWithValue("draft",plan);cmd.Parameters.AddWithValue("source",plannedEvent);
    cmd.Parameters.AddWithValue("evidence","source-evidence-"+id.ToString("N"));
    cmd.Parameters.AddWithValue("audit",Guid.NewGuid());cmd.Parameters.AddWithValue("outbox",Guid.NewGuid());
    Assert.Equal(id,await cmd.ExecuteScalarAsync());
   }
   foreach(var (source,linkId,draftId) in new[]{
    (otherEvent,link,plan),(plannedEvent,otherLink,otherPlan)})
   {
    await using var cmd=new NpgsqlCommand("""
     SELECT programs.record_center_sourced_activity_report(@report,'center-a',@link,@draft,
      'CENTER_EVENT',@source,@evidence,'Should remain outside dossier',NULL,'actor-a',
      @audit,@outbox,'s30-excluded-original')
     """,db);
    var id=Guid.NewGuid();
    cmd.Parameters.AddWithValue("report",id);cmd.Parameters.AddWithValue("link",linkId);
    cmd.Parameters.AddWithValue("draft",draftId);cmd.Parameters.AddWithValue("source",source);
    cmd.Parameters.AddWithValue("evidence","different-source-"+id.ToString("N"));
    cmd.Parameters.AddWithValue("audit",Guid.NewGuid());cmd.Parameters.AddWithValue("outbox",Guid.NewGuid());
    Assert.Equal(id,await cmd.ExecuteScalarAsync());
   }
   // Legacy opaque report has no typed owner binding and must never be upgraded.
   await using var legacy=new NpgsqlCommand("""
    SELECT programs.record_center_activity_report(@report,'center-a',@link,@draft,
     'CENTER_EVENT',@ref,'legacy-evidence','Legacy unbound claim',NULL,'actor-a',
     @audit,@outbox,'s30-legacy')
    """,db);
   legacy.Parameters.AddWithValue("report",Guid.NewGuid());
   legacy.Parameters.AddWithValue("link",link);legacy.Parameters.AddWithValue("draft",plan);
   legacy.Parameters.AddWithValue("ref",plannedEvent.ToString("D"));
   legacy.Parameters.AddWithValue("audit",Guid.NewGuid());legacy.Parameters.AddWithValue("outbox",Guid.NewGuid());
   Assert.IsType<Guid>(await legacy.ExecuteScalarAsync());
  }
  Task<IResult> Get(long? before=null,string actor="actor-a",string center="center-a",
   Guid? source=null,bool eventGrant=true,bool reportGrant=true,string? cs=null)=>
   CenterEventSourceDossierEndpoint.Get(Actor(actor),Config(cs),new EventGrant(eventGrant),
    new ReportGrant(reportGrant),center,source??plannedEvent,before,default);
  Assert.Equal(403,Status(await Get(eventGrant:false)));
  Assert.Equal(403,Status(await Get(reportGrant:false)));
  Assert.Equal(404,Status(await Get(actor:"actor-revoked")));
  Assert.Equal(404,Status(await Get(actor:"actor-b",center:"center-b")));
  Assert.Equal(404,Status(await Get(source:otherEvent)));
  Assert.Equal(503,Status(await Get(cs:Admin)));
  Assert.Equal(503,Status(await Get(cs:Writer)));
  var first=await Get();Assert.Equal(200,Status(first));
  long firstCursor;
  using(var j=Json(first))
  {
   var root=j.RootElement;
   Assert.Equal(plannedEvent,root.GetProperty("eventId").GetGuid());
   Assert.Equal(plan,root.GetProperty("draftId").GetGuid());
   Assert.Equal(1,root.GetProperty("pinnedPlanRevision").GetInt64());
   Assert.Equal(2,root.GetProperty("revision").GetInt64());
   Assert.Equal("V2 planned title",root.GetProperty("title").GetString());
   Assert.False(root.GetProperty("isExecuted").GetBoolean());
   Assert.False(root.GetProperty("isOutcome").GetBoolean());
   var records=root.GetProperty("activityReports");Assert.Equal(20,records.GetArrayLength());
   firstCursor=root.GetProperty("nextBeforeSequence").GetInt64();
   Assert.Equal(firstCursor,records[19].GetProperty("reportSequence").GetInt64());
   foreach(var r in records.EnumerateArray())
   {
    Assert.Equal("UNVERIFIED",r.GetProperty("verificationStatus").GetString());
    Assert.Equal("CENTER_EVENT",r.GetProperty("sourceKind").GetString());
    Assert.Equal(plannedEvent,r.GetProperty("sourceRecordRef").GetGuid());
    Assert.Equal(1,r.GetProperty("pinnedRevision").GetInt64());
    Assert.False(r.GetProperty("isApproved").GetBoolean());
    Assert.False(r.GetProperty("isExecuted").GetBoolean());
   }
  }
  var second=await Get(before:firstCursor);Assert.Equal(200,Status(second));
  using(var j=Json(second))
  {
   Assert.Equal(2,j.RootElement.GetProperty("activityReports").GetArrayLength());
   Assert.Equal(JsonValueKind.Null,j.RootElement.GetProperty("nextBeforeSequence").ValueKind);
  }
  Assert.Equal(400,Status(await Get(before:0)));
  await using var check=new NpgsqlConnection(Admin);await check.OpenAsync();
  await using var q=new NpgsqlCommand("""
   SELECT (SELECT count(*) FROM programs.center_activity_reports
     WHERE center_id='center-a' AND draft_id=@plan AND source_record_ref=@ref),
    (SELECT count(*) FROM events.center_event_plan_revisions
     WHERE center_id='center-a' AND event_id=@event)
   """,check);
  q.Parameters.AddWithValue("plan",plan);
  q.Parameters.AddWithValue("ref",plannedEvent.ToString("D"));
  q.Parameters.AddWithValue("event",plannedEvent);
  await using var rd=await q.ExecuteReaderAsync();Assert.True(await rd.ReadAsync());
  Assert.Equal(23L,rd.GetInt64(0));Assert.Equal(2L,rd.GetInt64(1));
 }
}
