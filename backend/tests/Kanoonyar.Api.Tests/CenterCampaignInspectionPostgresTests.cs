using System.Security.Claims;
using System.Text.Json;
using Kanoonyar.Api.Programs;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Xunit;

namespace Kanoonyar.Api.Tests;

/// <summary>Checks source-bound observations against disposable PostgreSQL with real least-privilege logins.</summary>
public sealed class CenterCampaignInspectionPostgresTests
{
 private static string? Admin=>Environment.GetEnvironmentVariable("CENTER_DRAFT_INTEGRATION_ADMIN_URL");
 private static string? Read=>Environment.GetEnvironmentVariable("CENTER_DRAFT_INTEGRATION_READ_DATABASE_URL");
 private static string? Write=>Environment.GetEnvironmentVariable("CENTER_CAMPAIGN_INSPECTION_INTEGRATION_DATABASE_URL");
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
   ["CENTER_CAMPAIGN_INSPECTION_WRITE_DATABASE_URL"]=writer??Write,
   ["CENTER_DRAFT_READ_DATABASE_URL"]=reader??Read
  }).Build();
 private static ClaimsPrincipal Actor(string sub)=>
  new(new ClaimsIdentity([new Claim("sub",sub)],"synthetic"));
 private static int? Status(IResult r)=>(r as IStatusCodeHttpResult)?.StatusCode;
 private sealed class Granted:ICenterCampaignReportLinkAuthorization
 {public ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal p,string c,Guid id,string op,CancellationToken ct)=>ValueTask.FromResult(true);}
 private sealed class ReportGranted:ICenterActivityReportReadAuthorization
 {public ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal p,string c,CancellationToken ct)=>ValueTask.FromResult(true);}
 [Fact] public async Task Note_is_immutable_source_bound_and_never_approves_execution()
 {
  if(!Enabled())return;
  var plan=Guid.NewGuid();var campaign=Guid.NewGuid();var association=Guid.NewGuid();
  var report=Guid.NewGuid();var link=Guid.NewGuid();var source=Guid.NewGuid();
  var classId=Guid.NewGuid();var noteId=Guid.NewGuid();var issuance=Guid.NewGuid();
  await using(var admin=new NpgsqlConnection(Admin))
  {
   await admin.OpenAsync();
   await using(var seed=new NpgsqlCommand(
    """
    INSERT INTO programs.center_plan_draft_revisions
      (center_id,draft_id,revision,program_id,national_version,local_title,local_activities,actor_subject)
    VALUES('center-a',@plan,1,'national-1',1,'S25 plan','[]'::jsonb,'actor-a');
    INSERT INTO learning.center_classes(center_id,class_id,title) VALUES('center-a',@class,'S25 class');
    INSERT INTO learning.class_sessions(center_id,session_id,class_id,session_title)
      VALUES('center-a',@source,@class,'S25 planned session');
    INSERT INTO campaigns.center_campaigns
      (center_id,campaign_id,slug,draft_id,pinned_plan_revision,created_by_subject)
    VALUES('center-a',@campaign,@slug,@plan,1,'actor-a');
    INSERT INTO campaigns.center_campaign_revisions
      (center_id,campaign_id,revision,title,goal_text,audience_description,planned_activities,changed_by_subject)
    VALUES('center-a',@campaign,1,'S25 campaign','Goal','Residents',
    '[{"activityKey":"workshop","title":"Planned workshop"}]'::jsonb,'actor-a');
    """,admin))
   {
    seed.Parameters.AddWithValue("plan",plan);
    seed.Parameters.AddWithValue("class",classId);
    seed.Parameters.AddWithValue("source",source);
    seed.Parameters.AddWithValue("campaign",campaign);
    seed.Parameters.AddWithValue("slug","inspection-"+Guid.NewGuid().ToString("N"));
    await seed.ExecuteNonQueryAsync();
   }
   await using(var issue=new NpgsqlCommand(
    """
    INSERT INTO programs.national_program_issuances
      (issuance_id,center_id,program_id,national_version,issued_by_subject,issued_at)
    VALUES(@issue,'center-a','national-1',1,'fixture',now())
    ON CONFLICT(center_id,program_id,national_version) DO NOTHING;
    SELECT issuance_id FROM programs.national_program_issuances
    WHERE center_id='center-a' AND program_id='national-1' AND national_version=1
    """,admin))
   {
    issue.Parameters.AddWithValue("issue",issuance);
    issuance=Assert.IsType<Guid>(await issue.ExecuteScalarAsync());
   }
   await using(var recorded=new NpgsqlCommand(
    """
    SELECT programs.record_center_plan_issuance_link(
      @link,'center-a',@plan,1,@issue,'national-1',1,'fixture',
      @audit,@event,'s25-plan')
    """,admin))
   {
    recorded.Parameters.AddWithValue("link",link);
    recorded.Parameters.AddWithValue("plan",plan);
    recorded.Parameters.AddWithValue("issue",issuance);
    recorded.Parameters.AddWithValue("audit",Guid.NewGuid());
    recorded.Parameters.AddWithValue("event",Guid.NewGuid());
    Assert.Equal(link,await recorded.ExecuteScalarAsync());
   }
   await using(var sourced=new NpgsqlCommand(
    """
    SELECT programs.record_center_sourced_activity_report(
      @report,'center-a',@link,@plan,'CLASS_SESSION',@source,
      'opaque-s25-evidence','Unverified report',NULL,'actor-a',
      @audit,@event,'s25-source')
    """,admin))
   {
    sourced.Parameters.AddWithValue("report",report);
    sourced.Parameters.AddWithValue("link",link);
    sourced.Parameters.AddWithValue("plan",plan);
    sourced.Parameters.AddWithValue("source",source);
    sourced.Parameters.AddWithValue("audit",Guid.NewGuid());
    sourced.Parameters.AddWithValue("event",Guid.NewGuid());
    Assert.Equal(report,await sourced.ExecuteScalarAsync());
   }
   await using(var assoc=new NpgsqlCommand(
    """
    INSERT INTO campaigns.campaign_report_links(center_id,association_id,campaign_id,
      campaign_revision,draft_id,pinned_plan_revision,activity_key,report_id,linked_by_subject)
    VALUES('center-a',@assoc,@campaign,1,@plan,1,'workshop',@report,'actor-a')
    """,admin))
   {
    assoc.Parameters.AddWithValue("assoc",association);
    assoc.Parameters.AddWithValue("campaign",campaign);
    assoc.Parameters.AddWithValue("plan",plan);
    assoc.Parameters.AddWithValue("report",report);
    await assoc.ExecuteNonQueryAsync();
   }
  }
  Task<IResult> Add(Guid id,string text="Inspection of report only",string actor="actor-a",
   string center="center-a",Guid? assoc=null,IConfiguration? config=null,
   ICenterActivityReportReadAuthorization? reportGrant=null)=>
   CenterCampaignInspectionNotesEndpoint.Append(Actor(actor),config??Config(),
    new Granted(),reportGrant??new ReportGranted(),center,campaign,assoc??association,
    new AppendCampaignInspectionNoteRequest(id,text),default);
  Task<IResult> List(string actor="actor-a",string center="center-a",
   IConfiguration? config=null)=>
   CenterCampaignInspectionNotesEndpoint.List(Actor(actor),config??Config(),
    new Granted(),new ReportGranted(),center,campaign,association,null,default);
  Assert.Equal(409,Status(await Add(Guid.NewGuid(),assoc:Guid.NewGuid())));
  Assert.Equal(403,Status(await Add(Guid.NewGuid(),actor:"actor-b")));
  Assert.Equal(403,Status(await Add(Guid.NewGuid(),actor:"actor-revoked")));
  Assert.Equal(403,Status(await Add(Guid.NewGuid(),
    reportGrant:new UnapprovedCenterActivityReportReadAuthorization())));
  Assert.Equal(503,Status(await Add(Guid.NewGuid(),config:Config(writer:Admin))));
  Assert.Equal(503,Status(await Add(Guid.NewGuid(),config:Config(writer:Read))));
  var done=await Add(noteId);Assert.Equal(200,Status(done));
  using(var json=JsonDocument.Parse(JsonSerializer.Serialize(
   Assert.IsAssignableFrom<IValueHttpResult>(done).Value,new JsonSerializerOptions(JsonSerializerDefaults.Web))))
  {
   var x=json.RootElement;
   Assert.Equal(noteId,x.GetProperty("noteId").GetGuid());
   Assert.False(x.GetProperty("isApproval").GetBoolean());
   Assert.False(x.GetProperty("isExecuted").GetBoolean());
   Assert.Equal("UNVERIFIED",x.GetProperty("verificationStatus").GetString());
  }
  Assert.Equal(409,Status(await Add(noteId)));
  var history=await List();Assert.Equal(200,Status(history));
  using(var json=JsonDocument.Parse(JsonSerializer.Serialize(
    Assert.IsAssignableFrom<IValueHttpResult>(history).Value,new JsonSerializerOptions(JsonSerializerDefaults.Web))))
  {
   var x=json.RootElement;
   Assert.False(x.GetProperty("isApproval").GetBoolean());
   var rows=x.GetProperty("notes");
   Assert.Single(rows.EnumerateArray());
   Assert.Equal("Inspection of report only",rows[0].GetProperty("noteText").GetString());
   Assert.False(rows[0].GetProperty("isApproval").GetBoolean());
   Assert.Equal("UNVERIFIED",rows[0].GetProperty("verificationStatus").GetString());
  }
  Assert.Equal(404,Status(await List(actor:"actor-revoked")));
  Assert.Equal(404,Status(await List(center:"center-b",actor:"actor-b")));
  Assert.Equal(503,Status(await List(config:Config(reader:Admin))));
  Assert.Equal(503,Status(await List(config:Config(reader:Write))));
  await using var db=new NpgsqlConnection(Admin);await db.OpenAsync();
  await using var count=new NpgsqlCommand(
   """
   SELECT
    (SELECT count(*) FROM campaigns.campaign_report_inspection_notes WHERE center_id='center-a' AND note_id=@note),
    (SELECT count(*) FROM programs.center_activity_reports WHERE report_id=@report),
    (SELECT count(*) FROM campaigns.campaign_report_links WHERE center_id='center-a' AND association_id=@assoc),
    (SELECT count(*) FROM governance.command_audit WHERE action_code='CAMPAIGN_REPORT_INSPECTION_NOTE_RECORDED' AND target_id=@id),
    (SELECT count(*) FROM governance.outbox_events WHERE event_type='CampaignReportInspectionNoteRecorded' AND aggregate_id=@id),
    (SELECT count(*) FROM governance.command_audit
      WHERE action_code='CAMPAIGN_REPORT_INSPECTION_NOTE_RECORDED' AND metadata::text LIKE '%Inspection of report only%')
   """,db);
  count.Parameters.AddWithValue("note",noteId);
  count.Parameters.AddWithValue("report",report);
  count.Parameters.AddWithValue("assoc",association);
  count.Parameters.AddWithValue("id",noteId.ToString("D"));
  await using var reader=await count.ExecuteReaderAsync();
  Assert.True(await reader.ReadAsync());
  for(var i=0;i<5;i++)Assert.Equal(1L,reader.GetInt64(i));
  Assert.Equal(0L,reader.GetInt64(5));
 }
}
