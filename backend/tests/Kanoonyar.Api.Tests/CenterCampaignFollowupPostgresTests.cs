using System.Security.Claims;
using System.Text.Json;
using Kanoonyar.Api.Programs;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Xunit;
namespace Kanoonyar.Api.Tests;
public sealed class CenterCampaignFollowupPostgresTests
{
 private static string? Admin=>Environment.GetEnvironmentVariable("CENTER_DRAFT_INTEGRATION_ADMIN_URL");
 private static string? Read=>Environment.GetEnvironmentVariable("CENTER_DRAFT_INTEGRATION_READ_DATABASE_URL");
 private static string? Write=>Environment.GetEnvironmentVariable("CENTER_CAMPAIGN_FOLLOWUP_INTEGRATION_DATABASE_URL");
 private static bool Enabled(){
  if(Environment.GetEnvironmentVariable("CENTER_DRAFT_INTEGRATION_REQUIRED")=="1"){
   Assert.False(string.IsNullOrWhiteSpace(Admin));Assert.False(string.IsNullOrWhiteSpace(Read));
   Assert.False(string.IsNullOrWhiteSpace(Write));
  }
  return !string.IsNullOrWhiteSpace(Admin)&&!string.IsNullOrWhiteSpace(Read)&&!string.IsNullOrWhiteSpace(Write);
 }
 private static IConfiguration Config(string? w=null,string? r=null)=>
  new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{
   ["CENTER_CAMPAIGN_FOLLOWUP_WRITE_DATABASE_URL"]=w??Write,
   ["CENTER_DRAFT_READ_DATABASE_URL"]=r??Read
  }).Build();
 private static ClaimsPrincipal Actor(string sub)=>new(new ClaimsIdentity([new Claim("sub",sub)],"test"));
 private static int? Status(IResult x)=>(x as IStatusCodeHttpResult)?.StatusCode;
 private static JsonDocument Json(IResult x)=>
  JsonDocument.Parse(JsonSerializer.Serialize(Assert.IsAssignableFrom<IValueHttpResult>(x).Value,
   new JsonSerializerOptions(JsonSerializerDefaults.Web)));
 private sealed class Grant:ICenterCampaignReportLinkAuthorization
 {public ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal a,string c,Guid id,string op,CancellationToken ct)=>ValueTask.FromResult(true);}
 private sealed class SourceGrant:ICenterActivityReportReadAuthorization
 {public ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal a,string c,CancellationToken ct)=>ValueTask.FromResult(true);}
 [Fact] public async Task Clarification_lines_remain_source_bound_and_never_approve_or_close()
 {
  if(!Enabled())return;
  var plan=Guid.NewGuid();var campaign=Guid.NewGuid();var association=Guid.NewGuid();
  var note=Guid.NewGuid();var request=Guid.NewGuid();var response=Guid.NewGuid();
  var classId=Guid.NewGuid();var session=Guid.NewGuid();var link=Guid.NewGuid();var report=Guid.NewGuid();
  Guid issuance=Guid.NewGuid();
  await using(var db=new NpgsqlConnection(Admin)){
   await db.OpenAsync();
   await using(var seed=new NpgsqlCommand(
    """
    INSERT INTO programs.center_plan_draft_revisions(center_id,draft_id,revision,
     program_id,national_version,local_title,local_activities,actor_subject)
    VALUES('center-a',@plan,1,'national-1',1,'Followup plan','[]'::jsonb,'actor-a');
    INSERT INTO learning.center_classes(center_id,class_id,title)
    VALUES('center-a',@class,'Followup class');
    INSERT INTO learning.class_sessions(center_id,session_id,class_id,session_title)
    VALUES('center-a',@session,@class,'Followup planned session');
    INSERT INTO campaigns.center_campaigns(center_id,campaign_id,slug,draft_id,
      pinned_plan_revision,created_by_subject)
    VALUES('center-a',@campaign,@slug,@plan,1,'actor-a');
    INSERT INTO campaigns.center_campaign_revisions(center_id,campaign_id,revision,
     title,goal_text,audience_description,planned_activities,changed_by_subject)
    VALUES('center-a',@campaign,1,'Followup campaign','Goal','Neighborhood',
     '[{"activityKey":"workshop","title":"Planned activity"}]'::jsonb,'actor-a');
    """,db)){
    seed.Parameters.AddWithValue("plan",plan);
    seed.Parameters.AddWithValue("class",classId);
    seed.Parameters.AddWithValue("session",session);
    seed.Parameters.AddWithValue("campaign",campaign);
    seed.Parameters.AddWithValue("slug","followup-"+Guid.NewGuid().ToString("N"));
    await seed.ExecuteNonQueryAsync();
   }
   await using(var issued=new NpgsqlCommand(
    """
    INSERT INTO programs.national_program_issuances(
     issuance_id,center_id,program_id,national_version,issued_by_subject,issued_at)
    VALUES(@issue,'center-a','national-1',1,'fixture',now())
    ON CONFLICT(center_id,program_id,national_version) DO NOTHING;
    SELECT issuance_id FROM programs.national_program_issuances
    WHERE center_id='center-a' AND program_id='national-1' AND national_version=1
    """,db)){
    issued.Parameters.AddWithValue("issue",issuance);
    issuance=Assert.IsType<Guid>(await issued.ExecuteScalarAsync());
   }
   await using(var owner=new NpgsqlCommand(
    """
    SELECT programs.record_center_plan_issuance_link(
      @link,'center-a',@plan,1,@issue,'national-1',1,'fixture',
      @audit,@event,'s26-plan')
    """,db)){
    owner.Parameters.AddWithValue("link",link);owner.Parameters.AddWithValue("plan",plan);
    owner.Parameters.AddWithValue("issue",issuance);
    owner.Parameters.AddWithValue("audit",Guid.NewGuid());owner.Parameters.AddWithValue("event",Guid.NewGuid());
    Assert.Equal(link,await owner.ExecuteScalarAsync());
   }
   await using(var record=new NpgsqlCommand(
    """
    SELECT programs.record_center_sourced_activity_report(
     @report,'center-a',@link,@plan,'CLASS_SESSION',@source,
     'opaque-evidence','Unverified claim',NULL,'actor-a',
     @audit,@event,'s26-source')
    """,db)){
    record.Parameters.AddWithValue("report",report);record.Parameters.AddWithValue("link",link);
    record.Parameters.AddWithValue("plan",plan);record.Parameters.AddWithValue("source",session);
    record.Parameters.AddWithValue("audit",Guid.NewGuid());record.Parameters.AddWithValue("event",Guid.NewGuid());
    Assert.Equal(report,await record.ExecuteScalarAsync());
   }
   await using(var origin=new NpgsqlCommand(
    """
    INSERT INTO campaigns.campaign_report_links(center_id,association_id,campaign_id,
     campaign_revision,draft_id,pinned_plan_revision,activity_key,report_id,linked_by_subject)
    VALUES('center-a',@association,@campaign,1,@plan,1,'workshop',@report,'actor-a');
    INSERT INTO campaigns.campaign_report_inspection_notes(center_id,note_id,campaign_id,
     association_id,note_text,noted_by_subject)
    VALUES('center-a',@note,@campaign,@association,'Unverified observation','actor-a');
    """,db)){
    origin.Parameters.AddWithValue("association",association);
    origin.Parameters.AddWithValue("campaign",campaign);
    origin.Parameters.AddWithValue("plan",plan);
    origin.Parameters.AddWithValue("report",report);
    origin.Parameters.AddWithValue("note",note);
    await origin.ExecuteNonQueryAsync();
   }
  }
  Task<IResult> Add(AppendCampaignFollowupRequest x,string actor="actor-a",
    string center="center-a",Guid? noteId=null,IConfiguration? config=null,
    ICenterActivityReportReadAuthorization? sourceGrant=null)=>
    CenterCampaignFollowupEndpoint.Append(Actor(actor),config??Config(),
      new Grant(),sourceGrant??new SourceGrant(),center,campaign,association,noteId??note,x,default);
  Task<IResult> List(string kind="REQUEST",Guid? requestId=null,string actor="actor-a",
    string center="center-a",IConfiguration? config=null)=>
    CenterCampaignFollowupEndpoint.List(Actor(actor),config??Config(),new Grant(),
      new SourceGrant(),center,campaign,association,note,kind,requestId,null,default);
  var question=new AppendCampaignFollowupRequest(request,"REQUEST",null,"Please clarify report time");
  var answer=new AppendCampaignFollowupRequest(response,"RESPONSE",request,"Source report does not prove completion");
  Assert.Equal(409,Status(await Add(question with{EntryId=Guid.NewGuid()},noteId:Guid.NewGuid())));
  Assert.Equal(409,Status(await Add(answer)));
  Assert.Equal(403,Status(await Add(question,actor:"actor-b")));
  Assert.Equal(403,Status(await Add(question,actor:"actor-revoked")));
  Assert.Equal(403,Status(await Add(question,sourceGrant:new UnapprovedCenterActivityReportReadAuthorization())));
  Assert.Equal(503,Status(await Add(question,config:Config(w:Admin))));
  Assert.Equal(503,Status(await Add(question,config:Config(w:Read))));
  var result=await Add(question);Assert.Equal(200,Status(result));
  using(var value=Json(result)){
   var x=value.RootElement;
   Assert.False(x.GetProperty("isApproval").GetBoolean());
   Assert.False(x.GetProperty("isResolved").GetBoolean());
   Assert.Equal("UNVERIFIED",x.GetProperty("verificationStatus").GetString());
  }
  Assert.Equal(409,Status(await Add(question)));
  Assert.Equal(409,Status(await Add(answer with{RequestId=Guid.NewGuid()})));
  Assert.Equal(200,Status(await Add(answer)));
  Assert.Equal(409,Status(await Add(answer)));
  var requests=await List();Assert.Equal(200,Status(requests));
  using(var value=Json(requests)){
   var rows=value.RootElement.GetProperty("entries");
   Assert.Single(rows.EnumerateArray());
   Assert.Equal(request,rows[0].GetProperty("entryId").GetGuid());
   Assert.False(rows[0].GetProperty("isResolved").GetBoolean());
  }
  var responses=await List("RESPONSE",request);Assert.Equal(200,Status(responses));
  using(var value=Json(responses)){
   var rows=value.RootElement.GetProperty("entries");
   Assert.Single(rows.EnumerateArray());
   Assert.Equal(response,rows[0].GetProperty("entryId").GetGuid());
   Assert.Equal(request,rows[0].GetProperty("requestId").GetGuid());
   Assert.False(rows[0].GetProperty("isApproval").GetBoolean());
  }
  Assert.Equal(404,Status(await List("RESPONSE",Guid.NewGuid())));
  Assert.Equal(404,Status(await List(actor:"actor-revoked")));
  Assert.Equal(404,Status(await List(center:"center-b",actor:"actor-b")));
  Assert.Equal(503,Status(await List(config:Config(r:Admin))));
  Assert.Equal(503,Status(await List(config:Config(r:Write))));
  await using var dbCheck=new NpgsqlConnection(Admin);await dbCheck.OpenAsync();
  var anotherCampaign=Guid.NewGuid();
  await using(var wrong=new NpgsqlCommand(
   """
   INSERT INTO campaigns.inspection_followup_responses(center_id,response_id,
    request_id,campaign_id,association_id,note_id,response_text,responded_by_subject)
   VALUES('center-a',@response,@request,@wrong,@association,@note,'Forged','actor-a')
   """,dbCheck)){
   wrong.Parameters.AddWithValue("response",Guid.NewGuid());
   wrong.Parameters.AddWithValue("request",request);
   wrong.Parameters.AddWithValue("wrong",anotherCampaign);
   wrong.Parameters.AddWithValue("association",association);
   wrong.Parameters.AddWithValue("note",note);
   var ex=await Assert.ThrowsAsync<PostgresException>(async()=>await wrong.ExecuteNonQueryAsync());
   Assert.Equal("23503",ex.SqlState);
  }
  await using var counts=new NpgsqlCommand(
   """
   SELECT
    (SELECT count(*) FROM campaigns.inspection_followup_requests WHERE center_id='center-a' AND request_id=@request),
    (SELECT count(*) FROM campaigns.inspection_followup_responses WHERE center_id='center-a' AND response_id=@response),
    (SELECT count(*) FROM campaigns.campaign_report_inspection_notes WHERE note_id=@note),
    (SELECT count(*) FROM programs.center_activity_reports WHERE report_id=@report),
    (SELECT count(*) FROM governance.command_audit WHERE action_code LIKE 'CAMPAIGN_INSPECTION_FOLLOWUP_%'
      AND (target_id=@req_id OR target_id=@res_id)),
    (SELECT count(*) FROM governance.outbox_events WHERE aggregate_type='campaign_inspection_followup'
      AND (aggregate_id=@req_id OR aggregate_id=@res_id)),
    (SELECT count(*) FROM governance.command_audit WHERE action_code LIKE 'CAMPAIGN_INSPECTION_FOLLOWUP_%'
      AND metadata::text LIKE '%Please clarify report time%')
   """,dbCheck);
  counts.Parameters.AddWithValue("request",request);
  counts.Parameters.AddWithValue("response",response);
  counts.Parameters.AddWithValue("note",note);
  counts.Parameters.AddWithValue("report",report);
  counts.Parameters.AddWithValue("req_id",request.ToString("D"));
  counts.Parameters.AddWithValue("res_id",response.ToString("D"));
  await using var reader=await counts.ExecuteReaderAsync();Assert.True(await reader.ReadAsync());
  for(var i=0;i<4;i++)Assert.Equal(1L,reader.GetInt64(i));
  Assert.Equal(2L,reader.GetInt64(4));Assert.Equal(2L,reader.GetInt64(5));Assert.Equal(0L,reader.GetInt64(6));
 }
}
