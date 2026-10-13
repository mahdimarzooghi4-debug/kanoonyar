using System.Security.Claims;
using System.Text.Json;
using Kanoonyar.Api.Programs;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Xunit;

namespace Kanoonyar.Api.Tests;
public sealed class CenterCampaignEvidenceRegisterPostgresTests
{
 private static string? Admin=>Environment.GetEnvironmentVariable("CENTER_DRAFT_INTEGRATION_ADMIN_URL");
 private static string? Read=>Environment.GetEnvironmentVariable("CENTER_DRAFT_INTEGRATION_READ_DATABASE_URL");
 private static string? Write=>Environment.GetEnvironmentVariable("CENTER_CAMPAIGN_FOLLOWUP_INTEGRATION_DATABASE_URL");
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
 private static IConfiguration Config(string? url=null)=>new ConfigurationBuilder()
  .AddInMemoryCollection(new Dictionary<string,string?>{["CENTER_DRAFT_READ_DATABASE_URL"]=url??Read}).Build();
 private static ClaimsPrincipal Actor(string sub)=>new(new ClaimsIdentity([new Claim("sub",sub)],"test"));
 private static int? Status(IResult r)=>(r as IStatusCodeHttpResult)?.StatusCode;
 private static JsonDocument Payload(IResult r)=>JsonDocument.Parse(JsonSerializer.Serialize(
  Assert.IsAssignableFrom<IValueHttpResult>(r).Value,new JsonSerializerOptions(JsonSerializerDefaults.Web)));
 private sealed class Grant:ICenterCampaignReportLinkAuthorization
 {
  public string? Denied {get;init;}
  public ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal p,string c,Guid id,string op,CancellationToken ct)=>
   ValueTask.FromResult(op!=Denied);
 }
 private sealed class SourceGrant:ICenterActivityReportReadAuthorization
 {public ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal p,string c,CancellationToken ct)=>ValueTask.FromResult(true);}
 [Fact] public async Task Real_source_register_paginates_filters_and_preserves_unverified_lineage()
 {
  if(!Enabled())return;
  var plan=Guid.NewGuid();var campaign=Guid.NewGuid();var association=Guid.NewGuid();
  var note=Guid.NewGuid();var req=Guid.NewGuid();var reply=Guid.NewGuid();
  var source=Guid.NewGuid();var report=Guid.NewGuid();var classId=Guid.NewGuid();var link=Guid.NewGuid();
  Guid issuance=Guid.NewGuid();
  await using(var db=new NpgsqlConnection(Admin))
  {
   await db.OpenAsync();
   await using(var seed=new NpgsqlCommand(
    """
    INSERT INTO programs.center_plan_draft_revisions(center_id,draft_id,revision,program_id,
     national_version,local_title,local_activities,actor_subject)
    VALUES('center-a',@plan,1,'national-1',1,'S27 plan','[]'::jsonb,'actor-a');
    INSERT INTO learning.center_classes(center_id,class_id,title) VALUES('center-a',@class,'S27 class');
    INSERT INTO learning.class_sessions(center_id,session_id,class_id,session_title)
      VALUES('center-a',@source,@class,'S27 planned session');
    INSERT INTO campaigns.center_campaigns(center_id,campaign_id,slug,draft_id,pinned_plan_revision,created_by_subject)
    VALUES('center-a',@campaign,@slug,@plan,1,'actor-a');
    INSERT INTO campaigns.center_campaign_revisions(center_id,campaign_id,revision,title,goal_text,
      audience_description,planned_activities,changed_by_subject)
    VALUES('center-a',@campaign,1,'S27 campaign','Awareness','Neighborhood',
      '[{"activityKey":"workshop","title":"Planned workshop"}]'::jsonb,'actor-a');
    """,db))
   {
    seed.Parameters.AddWithValue("plan",plan);
    seed.Parameters.AddWithValue("class",classId);
    seed.Parameters.AddWithValue("source",source);
    seed.Parameters.AddWithValue("campaign",campaign);
    seed.Parameters.AddWithValue("slug","review-"+Guid.NewGuid().ToString("N"));
    await seed.ExecuteNonQueryAsync();
   }
   await using(var issue=new NpgsqlCommand(
    """
    INSERT INTO programs.national_program_issuances(issuance_id,center_id,program_id,
     national_version,issued_by_subject,issued_at)
    VALUES(@issue,'center-a','national-1',1,'fixture',now())
    ON CONFLICT(center_id,program_id,national_version) DO NOTHING;
    SELECT issuance_id FROM programs.national_program_issuances
    WHERE center_id='center-a' AND program_id='national-1' AND national_version=1
    """,db))
   {
    issue.Parameters.AddWithValue("issue",issuance);
    issuance=Assert.IsType<Guid>(await issue.ExecuteScalarAsync());
   }
   await using(var owner=new NpgsqlCommand(
    """
    SELECT programs.record_center_plan_issuance_link(@link,'center-a',@plan,1,
      @issue,'national-1',1,'fixture',@audit,@event,'s27-plan')
    """,db))
   {
    owner.Parameters.AddWithValue("link",link);owner.Parameters.AddWithValue("plan",plan);
    owner.Parameters.AddWithValue("issue",issuance);owner.Parameters.AddWithValue("audit",Guid.NewGuid());
    owner.Parameters.AddWithValue("event",Guid.NewGuid());
    Assert.Equal(link,await owner.ExecuteScalarAsync());
   }
   await using(var original=new NpgsqlCommand(
    """
    SELECT programs.record_center_sourced_activity_report(@report,'center-a',@link,@plan,
      'CLASS_SESSION',@source,'opaque-evidence','Unverified source only',NULL,'actor-a',
      @audit,@event,'s27-report')
    """,db))
   {
    original.Parameters.AddWithValue("report",report);original.Parameters.AddWithValue("link",link);
    original.Parameters.AddWithValue("plan",plan);original.Parameters.AddWithValue("source",source);
    original.Parameters.AddWithValue("audit",Guid.NewGuid());original.Parameters.AddWithValue("event",Guid.NewGuid());
    Assert.Equal(report,await original.ExecuteScalarAsync());
   }
   await using(var timeline=new NpgsqlCommand(
    """
    INSERT INTO campaigns.campaign_report_links(center_id,association_id,campaign_id,
      campaign_revision,draft_id,pinned_plan_revision,activity_key,report_id,linked_by_subject)
    VALUES('center-a',@association,@campaign,1,@plan,1,'workshop',@report,'actor-a');
    INSERT INTO campaigns.campaign_report_inspection_notes(center_id,note_id,campaign_id,
      association_id,note_text,noted_by_subject,noted_at)
    VALUES('center-a',@note,@campaign,@association,'Note is not approval','actor-a',now()-interval '4 minutes');
    INSERT INTO campaigns.inspection_followup_requests(center_id,request_id,campaign_id,
      association_id,note_id,question_text,requested_by_subject,requested_at)
    VALUES('center-a',@req,@campaign,@association,@note,'Clarify this note','actor-a',
      now()-interval '2 minutes');
    INSERT INTO campaigns.inspection_followup_responses(center_id,response_id,request_id,
      campaign_id,association_id,note_id,response_text,responded_by_subject,responded_at)
    VALUES('center-a',@reply,@req,@campaign,@association,@note,
      'Source does not verify delivery','actor-a',now()-interval '1 minute');
    INSERT INTO campaigns.campaign_report_inspection_notes(center_id,note_id,campaign_id,
     association_id,note_text,noted_by_subject,noted_at)
    SELECT 'center-a',gen_random_uuid(),@campaign,@association,'Additional unverified note',
     'actor-a',now()-(g+4)*interval '1 minute' FROM generate_series(1,19) g;
    """,db))
   {
    timeline.Parameters.AddWithValue("association",association);
    timeline.Parameters.AddWithValue("campaign",campaign);
    timeline.Parameters.AddWithValue("plan",plan);timeline.Parameters.AddWithValue("report",report);
    timeline.Parameters.AddWithValue("note",note);timeline.Parameters.AddWithValue("req",req);
    timeline.Parameters.AddWithValue("reply",reply);
    await timeline.ExecuteNonQueryAsync();
   }
  }

  // Synthetic original-owner commands executed with genuine Npgsql bound parameters.
  // A DO dollar-quoted body cannot bind @parameter placeholders.
  await using(var db=new NpgsqlConnection(Admin)){
   await db.OpenAsync();
   for(var i=1;i<=22;i++){
    var extraSession=Guid.NewGuid();var extraReport=Guid.NewGuid();
    var extraAssociation=Guid.NewGuid();
    await using var q=new NpgsqlCommand(
     """
     INSERT INTO learning.class_sessions(center_id,session_id,class_id,session_title)
     VALUES('center-a',@session,@class,'Additional planned session');
     SELECT programs.record_center_sourced_activity_report(
      @report,'center-a',@link,@plan,'CLASS_SESSION',@session,
      'opaque-extra','Unverified synthetic claim',NULL,'actor-a',
      @audit,@event,'s28-fixture');
     INSERT INTO campaigns.campaign_report_links(center_id,association_id,campaign_id,
      campaign_revision,draft_id,pinned_plan_revision,activity_key,report_id,linked_by_subject)
     VALUES('center-a',@association,@campaign,1,@plan,1,'workshop',@report,'actor-a');
     """,db);
    q.Parameters.AddWithValue("session",extraSession);
    q.Parameters.AddWithValue("class",classId);
    q.Parameters.AddWithValue("report",extraReport);
    q.Parameters.AddWithValue("link",link);
    q.Parameters.AddWithValue("plan",plan);
    q.Parameters.AddWithValue("audit",Guid.NewGuid());
    q.Parameters.AddWithValue("event",Guid.NewGuid());
    q.Parameters.AddWithValue("association",extraAssociation);
    q.Parameters.AddWithValue("campaign",campaign);
    await q.ExecuteNonQueryAsync();
   }
   var originalEvent=Guid.NewGuid();var eventReport=Guid.NewGuid();var eventAssociation=Guid.NewGuid();
   await using var eventCmd=new NpgsqlCommand(
    """
    INSERT INTO events.center_events(center_id,event_id,event_title)
    VALUES('center-a',@source,'Real typed original event');
    SELECT programs.record_center_sourced_activity_report(
     @report,'center-a',@link,@plan,'CENTER_EVENT',@source,
     'opaque-event','Unverified event report',NULL,'actor-a',
     @audit,@event,'s28-event');
    INSERT INTO campaigns.campaign_report_links(center_id,association_id,campaign_id,
     campaign_revision,draft_id,pinned_plan_revision,activity_key,report_id,linked_by_subject)
    VALUES('center-a',@association,@campaign,1,@plan,1,'workshop',@report,'actor-a');
    """,db);
   eventCmd.Parameters.AddWithValue("source",originalEvent);
   eventCmd.Parameters.AddWithValue("report",eventReport);
   eventCmd.Parameters.AddWithValue("link",link);
   eventCmd.Parameters.AddWithValue("plan",plan);
   eventCmd.Parameters.AddWithValue("audit",Guid.NewGuid());
   eventCmd.Parameters.AddWithValue("event",Guid.NewGuid());
   eventCmd.Parameters.AddWithValue("association",eventAssociation);
   eventCmd.Parameters.AddWithValue("campaign",campaign);
   await eventCmd.ExecuteNonQueryAsync();
  }
 Task<IResult> Get(string actor="actor-a",string center="center-a",Guid? cid=null,
  Guid? cursor=null,string? kind=null,string? activity=null,IConfiguration? cfg=null,
  ICenterCampaignReportLinkAuthorization? grant=null,ICenterActivityReportReadAuthorization? original=null)=>
   CenterCampaignEvidenceRegisterEndpoint.Get(Actor(actor),cfg??Config(),grant??new Grant(),
    original??new SourceGrant(),center,cid??campaign,cursor,kind,activity,default);
  Assert.Equal(403,Status(await Get(grant:new Grant{Denied="CAMPAIGN_EVIDENCE_REGISTER_READ"})));
  Assert.Equal(403,Status(await Get(grant:new Grant{Denied="CAMPAIGN_REPORT_INSPECTION_READ"})));
  Assert.Equal(403,Status(await Get(original:new UnapprovedCenterActivityReportReadAuthorization())));
  Assert.Equal(404,Status(await Get(actor:"actor-revoked")));
  Assert.Equal(404,Status(await Get(center:"center-b",actor:"actor-b")));
  Assert.Equal(404,Status(await Get(cid:Guid.NewGuid())));
  Assert.Equal(503,Status(await Get(cfg:Config(Admin))));
  Assert.Equal(503,Status(await Get(cfg:Config(Write))));
  var first=await Get();Assert.Equal(200,Status(first));
  Guid? lastCursor;
  using(var json=Payload(first)){
   var root=json.RootElement;
   Assert.Equal("UNVERIFIED",root.GetProperty("verificationStatus").GetString());
   Assert.False(root.GetProperty("isPageEnd").GetBoolean());
   var records=root.GetProperty("records");
   Assert.Equal(20,records.GetArrayLength());
   for(var i=0;i<20;i++){
    var row=records[i];
    Assert.Equal(plan,row.GetProperty("draftId").GetGuid());
    Assert.Equal(1,row.GetProperty("pinnedPlanRevision").GetInt64());
    Assert.Equal("workshop",row.GetProperty("activityKey").GetString());
    Assert.Equal("UNVERIFIED",row.GetProperty("verificationStatus").GetString());
    Assert.False(row.GetProperty("isApproval").GetBoolean());
    Assert.False(row.GetProperty("isResolved").GetBoolean());
    if(i>0)Assert.True(
      records[i-1].GetProperty("associationId").GetGuid().CompareTo(row.GetProperty("associationId").GetGuid())>0);
   }
   lastCursor=root.GetProperty("nextBeforeAssociationId").GetGuid();
   Assert.Equal(lastCursor,records[19].GetProperty("associationId").GetGuid());
  }
  var second=await Get(cursor:lastCursor);Assert.Equal(200,Status(second));
  using(var json=Payload(second)){
   Assert.True(json.RootElement.GetProperty("isPageEnd").GetBoolean());
   Assert.Equal(4,json.RootElement.GetProperty("records").GetArrayLength());
   Assert.Equal(JsonValueKind.Null,json.RootElement.GetProperty("nextBeforeAssociationId").ValueKind);
  }
  var eventResult=await Get(kind:"CENTER_EVENT");Assert.Equal(200,Status(eventResult));
  using(var json=Payload(eventResult)){
   var rows=json.RootElement.GetProperty("records");
   Assert.Single(rows.EnumerateArray());
   Assert.Equal("CENTER_EVENT",rows[0].GetProperty("sourceKind").GetString());
   Assert.Equal("UNVERIFIED",rows[0].GetProperty("verificationStatus").GetString());
  }
  var filtered=await Get(activity:"workshop");Assert.Equal(200,Status(filtered));
  var empty=await Get(activity:"missing");Assert.Equal(200,Status(empty));
  using(var json=Payload(empty))Assert.Empty(json.RootElement.GetProperty("records").EnumerateArray());
  // The non-result is UNKNOWN, never a zero-execution assertion.
  await using var checkDb=new NpgsqlConnection(Admin);await checkDb.OpenAsync();
  await using var q=new NpgsqlCommand(
   """
   SELECT (SELECT count(*) FROM programs.center_activity_reports WHERE center_id='center-a'
     AND draft_id=@plan),
     (SELECT count(*) FROM campaigns.campaign_report_links WHERE center_id='center-a'
      AND campaign_id=@campaign),
     (SELECT count(*) FROM campaigns.campaign_report_inspection_notes
       WHERE center_id='center-a' AND note_id=@note)
   """,checkDb);
  q.Parameters.AddWithValue("plan",plan);q.Parameters.AddWithValue("campaign",campaign);
  q.Parameters.AddWithValue("note",note);
  await using var reader=await q.ExecuteReaderAsync();Assert.True(await reader.ReadAsync());
  Assert.Equal(24L,reader.GetInt64(0));
  Assert.Equal(24L,reader.GetInt64(1));
  Assert.Equal(1L,reader.GetInt64(2));
 }
}
