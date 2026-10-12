using System.Security.Claims;
using System.Text.Json;
using Kanoonyar.Api.Programs;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Xunit;

namespace Kanoonyar.Api.Tests;

/// <summary>Disposable PostgreSQL evidence that links are source pointers, not delivered activities.</summary>
public sealed class CenterCampaignReportLinkPostgresTests
{
 private static string? Admin=>Environment.GetEnvironmentVariable("CENTER_DRAFT_INTEGRATION_ADMIN_URL");
 private static string? Read=>Environment.GetEnvironmentVariable("CENTER_DRAFT_INTEGRATION_READ_DATABASE_URL");
 private static string? Write=>Environment.GetEnvironmentVariable("CENTER_CAMPAIGN_LINK_INTEGRATION_DATABASE_URL");
 private static string? CampaignWriter=>Environment.GetEnvironmentVariable("CENTER_CAMPAIGN_INTEGRATION_DATABASE_URL");
 private static bool Enabled()
 {
  if(Environment.GetEnvironmentVariable("CENTER_DRAFT_INTEGRATION_REQUIRED")=="1")
  {
   Assert.False(string.IsNullOrWhiteSpace(Admin));Assert.False(string.IsNullOrWhiteSpace(Read));
   Assert.False(string.IsNullOrWhiteSpace(Write));Assert.False(string.IsNullOrWhiteSpace(CampaignWriter));
  }
  return !string.IsNullOrWhiteSpace(Admin)&&!string.IsNullOrWhiteSpace(Read)&&
    !string.IsNullOrWhiteSpace(Write)&&!string.IsNullOrWhiteSpace(CampaignWriter);
 }
 private static IConfiguration Config(string? write=null,string? read=null)=>
  new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{
   ["CENTER_CAMPAIGN_REPORT_LINK_DATABASE_URL"]=write??Write,
   ["CENTER_DRAFT_READ_DATABASE_URL"]=read??Read,
   ["CENTER_CAMPAIGN_WRITE_DATABASE_URL"]=CampaignWriter
  }).Build();
 private static ClaimsPrincipal Actor(string sub)=>new(new ClaimsIdentity([new Claim("sub",sub)],"synthetic"));
 private static int? Status(IResult result)=>(result as IStatusCodeHttpResult)?.StatusCode;
 private static JsonDocument Json(IResult result)=>JsonDocument.Parse(JsonSerializer.Serialize(
  Assert.IsAssignableFrom<IValueHttpResult>(result).Value,new JsonSerializerOptions(JsonSerializerDefaults.Web)));
 private sealed class LinkGrant:ICenterCampaignReportLinkAuthorization
 {public ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal p,string c,Guid id,string op,CancellationToken ct)=>ValueTask.FromResult(true);}
 private sealed class CampaignGrant:ICenterCampaignAuthorization
 {public ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal p,string c,Guid id,string op,CancellationToken ct)=>ValueTask.FromResult(true);}
 private sealed class ReportGrant:ICenterActivityReportReadAuthorization
 {public ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal p,string c,CancellationToken ct)=>ValueTask.FromResult(true);}

 [Fact] public async Task Exact_source_and_plan_link_is_audited_never_recounted_and_never_verified()
 {
  if(!Enabled())return;
  var plan=Guid.NewGuid();var campaign=Guid.NewGuid();var report=Guid.NewGuid();
  var session=Guid.NewGuid();var classId=Guid.NewGuid();var issuance=Guid.NewGuid();var link=Guid.NewGuid();
  await using(var admin=new NpgsqlConnection(Admin))
  {
   await admin.OpenAsync();
   await using(var setup=new NpgsqlCommand(
    """
    INSERT INTO programs.center_plan_draft_revisions(center_id,draft_id,revision,
      program_id,national_version,local_title,local_activities,actor_subject)
    VALUES('center-a',@draft,1,'national-1',1,'Source plan','[]'::jsonb,'actor-a');
    INSERT INTO learning.center_classes(center_id,class_id,title)
    VALUES('center-a',@class,'Synthetic class');
    INSERT INTO learning.class_sessions(center_id,session_id,class_id,session_title)
    VALUES('center-a',@session,@class,'Synthetic session');
    """,admin))
   {
    setup.Parameters.AddWithValue("draft",plan);
    setup.Parameters.AddWithValue("class",classId);
    setup.Parameters.AddWithValue("session",session);
    await setup.ExecuteNonQueryAsync();
   }
   await using(var issue=new NpgsqlCommand(
    """
    INSERT INTO programs.national_program_issuances
      (issuance_id,center_id,program_id,national_version,issued_by_subject,issued_at)
    VALUES(@issue,'center-a','national-1',1,'fixture',now())
    ON CONFLICT(center_id,program_id,national_version) DO NOTHING;
    SELECT issuance_id FROM programs.national_program_issuances
    WHERE center_id='center-a' AND program_id='national-1' AND national_version=1;
    """,admin))
   {
    issue.Parameters.AddWithValue("issue",issuance);
    issuance=Assert.IsType<Guid>(await issue.ExecuteScalarAsync());
   }
   await using(var owner=new NpgsqlCommand(
    """
    SELECT programs.record_center_plan_issuance_link(
      @link,'center-a',@draft,1,@issue,'national-1',1,'actor-a',
      @audit,@event,'s24-source-plan')
    """,admin))
   {
    owner.Parameters.AddWithValue("link",link);owner.Parameters.AddWithValue("draft",plan);
    owner.Parameters.AddWithValue("issue",issuance);
    owner.Parameters.AddWithValue("audit",Guid.NewGuid());
    owner.Parameters.AddWithValue("event",Guid.NewGuid());
    Assert.Equal(link,await owner.ExecuteScalarAsync());
   }
   await using(var seedReport=new NpgsqlCommand(
    """
    SELECT programs.record_center_sourced_activity_report(
      @report,'center-a',@link,@draft,'CLASS_SESSION',@session,
      'synthetic-evidence','Claimed workshop report',NULL,'actor-a',
      @audit,@event,'s24-sourced-report')
    """,admin))
   {
    seedReport.Parameters.AddWithValue("report",report);
    seedReport.Parameters.AddWithValue("link",link);
    seedReport.Parameters.AddWithValue("draft",plan);
    seedReport.Parameters.AddWithValue("session",session);
    seedReport.Parameters.AddWithValue("audit",Guid.NewGuid());
    seedReport.Parameters.AddWithValue("event",Guid.NewGuid());
    Assert.Equal(report,await seedReport.ExecuteScalarAsync());
   }
  }
  var campaignRequest=new AppendCenterCampaignRequest(campaign,0,"campaign-source",
    plan,1,"Private campaign","Planned community action","General residents",
    [new PlannedCampaignActivity("workshop","Planned workshop")]);
  Assert.Equal(200,Status(await CenterCampaignEndpoint.Append(Actor("actor-a"),Config(),
    new CampaignGrant(),"center-a",campaignRequest,default)));
  var body=new LinkCampaignSourceReportRequest(Guid.NewGuid(),1,"workshop",report);
  Task<IResult> Add(LinkCampaignSourceReportRequest b,string actor="actor-a",
   string center="center-a",IConfiguration? config=null,
   ICenterCampaignReportLinkAuthorization? g=null,
   ICenterActivityReportReadAuthorization? rg=null)=>
   CenterCampaignReportLinkEndpoint.Link(Actor(actor),config??Config(),
     g??new LinkGrant(),rg??new ReportGrant(),center,campaign,b,default);
  Task<IResult> List(string actor="actor-a",string center="center-a",IConfiguration? config=null)=>
   CenterCampaignReportLinkEndpoint.List(Actor(actor),config??Config(),
     new LinkGrant(),new ReportGrant(),center,campaign,null,default);
  Assert.Equal(409,Status(await Add(body with{ReportId=Guid.NewGuid()})));
  Assert.Equal(409,Status(await Add(body with{ActivityKey="unknown"})));
  Assert.Equal(409,Status(await Add(body with{CampaignRevision=2})));
  Assert.Equal(403,Status(await Add(body,actor:"actor-b")));
  Assert.Equal(403,Status(await Add(body,actor:"actor-revoked")));
  Assert.Equal(403,Status(await Add(body,
    rg:new UnapprovedCenterActivityReportReadAuthorization())));
  Assert.Equal(403,Status(await Add(body,
    g:new UnapprovedCenterCampaignReportLinkAuthorization())));
  Assert.Equal(503,Status(await Add(body,config:Config(write:Admin))));
  Assert.Equal(503,Status(await Add(body,config:Config(write:Read))));
  Assert.Equal(200,Status(await Add(body)));
  Assert.Equal(409,Status(await Add(body)));
  Assert.Equal(409,Status(await Add(body with{AssociationId=Guid.NewGuid()})));
  var list=await List();Assert.Equal(200,Status(list));
  using(var json=Json(list))
  {
   var root=json.RootElement;
   Assert.Equal("UNVERIFIED",root.GetProperty("verificationStatus").GetString());
   Assert.False(root.GetProperty("isExecuted").GetBoolean());
   var rows=root.GetProperty("associations");
   Assert.Single(rows.EnumerateArray());
   Assert.Equal(report,rows[0].GetProperty("reportId").GetGuid());
   Assert.Equal("CLASS_SESSION",rows[0].GetProperty("sourceKind").GetString());
   Assert.Equal(session.ToString("D"),rows[0].GetProperty("sourceRecordRef").GetString());
   Assert.False(rows[0].GetProperty("isExecuted").GetBoolean());
  }
  Assert.Equal(404,Status(await List(center:"center-b",actor:"actor-b")));
  Assert.Equal(404,Status(await List(center:"center-b")));
  Assert.Equal(404,Status(await List(actor:"actor-revoked")));
  Assert.Equal(503,Status(await List(config:Config(read:Admin))));
  Assert.Equal(503,Status(await List(config:Config(read:Write))));
  await using var db=new NpgsqlConnection(Admin);await db.OpenAsync();
  await using var q=new NpgsqlCommand(
   """
   SELECT (SELECT count(*) FROM campaigns.campaign_report_links
      WHERE center_id='center-a' AND campaign_id=@campaign),
    (SELECT count(*) FROM programs.center_activity_reports WHERE report_id=@report),
    (SELECT count(*) FROM programs.activity_report_source_bindings WHERE report_id=@report),
    (SELECT count(*) FROM governance.command_audit
      WHERE action_code='UNVERIFIED_CAMPAIGN_REPORT_ASSOCIATION_RECORDED' AND target_id=@id),
    (SELECT count(*) FROM governance.outbox_events
      WHERE event_type='UnverifiedCampaignReportLinked' AND aggregate_id=@id)
   """,db);
  q.Parameters.AddWithValue("campaign",campaign);
  q.Parameters.AddWithValue("report",report);
  q.Parameters.AddWithValue("id",body.AssociationId.ToString("D"));
  await using var reader=await q.ExecuteReaderAsync();Assert.True(await reader.ReadAsync());
  for(var i=0;i<5;i++)Assert.Equal(1L,reader.GetInt64(i));
 }
}
