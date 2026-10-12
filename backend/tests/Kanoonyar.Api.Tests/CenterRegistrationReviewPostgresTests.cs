using System.Security.Claims;
using System.Text.Json;
using Kanoonyar.Api.Programs;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Xunit;

namespace Kanoonyar.Api.Tests;

/// <summary>Disposable SQL fixture with synthetic references and explicit fixture-only grants.</summary>
public sealed class CenterRegistrationReviewPostgresTests
{
 private static string? Admin=>Environment.GetEnvironmentVariable("CENTER_DRAFT_INTEGRATION_ADMIN_URL");
 private static string? Read=>Environment.GetEnvironmentVariable("CENTER_DRAFT_INTEGRATION_READ_DATABASE_URL");
 private static string? ClassWrite=>Environment.GetEnvironmentVariable("CENTER_CLASS_INTAKE_INTEGRATION_DATABASE_URL");
 private static string? ReviewWrite=>Environment.GetEnvironmentVariable("CENTER_REGISTRATION_REVIEW_INTEGRATION_DATABASE_URL");
 private static bool Enabled()
 {
  if(Environment.GetEnvironmentVariable("CENTER_DRAFT_INTEGRATION_REQUIRED")=="1")
  {
   Assert.False(string.IsNullOrWhiteSpace(Admin));Assert.False(string.IsNullOrWhiteSpace(Read));
   Assert.False(string.IsNullOrWhiteSpace(ClassWrite));Assert.False(string.IsNullOrWhiteSpace(ReviewWrite));
  }
  return !string.IsNullOrWhiteSpace(Admin)&&!string.IsNullOrWhiteSpace(Read)
   &&!string.IsNullOrWhiteSpace(ClassWrite)&&!string.IsNullOrWhiteSpace(ReviewWrite);
 }
 private static IConfiguration Config(string? reviewWriter=null,string? reviewReader=null)=>
  new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{
    ["CENTER_CLASS_INTAKE_WRITE_DATABASE_URL"]=ClassWrite,
    ["CENTER_REGISTRATION_REVIEW_WRITE_DATABASE_URL"]=reviewWriter??ReviewWrite,
    ["CENTER_DRAFT_READ_DATABASE_URL"]=reviewReader??Read
  }).Build();
 private static ClaimsPrincipal User(string sub)=>
  new(new ClaimsIdentity([new Claim("sub",sub)],"synthetic"));
 private static int? Status(IResult value)=>(value as IStatusCodeHttpResult)?.StatusCode;
 private sealed class IntakeGrant:ICenterClassIntakeAuthorization
 {
  public ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal user,string center,Guid cls,
    string op,CancellationToken ct)=>ValueTask.FromResult(true);
 }
 private sealed class ReviewGrant:ICenterRegistrationReviewAuthorization
 {
  public ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal user,string center,Guid cls,
    Guid req,string op,CancellationToken ct)=>ValueTask.FromResult(true);
 }
 [Fact]
 public async Task Exact_request_review_topics_are_audited_without_admission_or_cross_center_visibility()
 {
  if(!Enabled())return;
  var cls=Guid.NewGuid();var registration=Guid.NewGuid();var review=Guid.NewGuid();
  await using(var admin=new NpgsqlConnection(Admin))
  {
   await admin.OpenAsync();
   await using var cmd=new NpgsqlCommand(
    "INSERT INTO learning.center_classes(center_id,class_id,title) VALUES('center-a',@id,'S19 synthetic review class')",admin);
   cmd.Parameters.AddWithValue("id",cls);
   await cmd.ExecuteNonQueryAsync();
  }
  var fixture=new IntakeGrant();
  Assert.Equal(200,Status(await CenterClassIntakeEndpoint.SetCapacity(
    User("actor-a"),Config(),fixture,"center-a",cls,new SetClassCapacityRequest(0,2),default)));
  Assert.Equal(200,Status(await CenterClassIntakeEndpoint.RequestRegistration(
    User("actor-a"),Config(),fixture,"center-a",cls,
    new RequestClassRegistrationIntent(registration,Guid.NewGuid()),default)));

  var grants=new ReviewGrant();
  Task<IResult> Raise(Guid rid,string topic="IDENTITY_AUTHORITY",Guid? classId=null,
    string actor="actor-a",IConfiguration? config=null)=>
    CenterRegistrationReviewEndpoint.Raise(User(actor),config??Config(),grants,
      "center-a",classId??cls,registration,new RaiseRegistrationReviewRequest(rid,topic),default);
  Task<IResult> List(Guid? classId=null,string center="center-a",string actor="actor-a",
    IConfiguration? config=null)=>
    CenterRegistrationReviewEndpoint.List(User(actor),config??Config(),grants,
      center,classId??cls,registration,default);
  var saved=await Raise(review);
  Assert.Equal(200,Status(saved));
  using(var data=JsonDocument.Parse(JsonSerializer.Serialize(
    Assert.IsAssignableFrom<IValueHttpResult>(saved).Value)))
  {
   var root=data.RootElement;
   Assert.Equal(review,root.GetProperty("reviewRequestId").GetGuid());
   Assert.Equal("REVIEW_REQUIRED",root.GetProperty("reviewStatus").GetString());
   Assert.Equal(JsonValueKind.Null,root.GetProperty("enrollmentDecision").ValueKind);
   Assert.False(root.GetProperty("seatReserved").GetBoolean());
  }
  Assert.Equal(409,Status(await Raise(review)));
  Assert.Equal(409,Status(await Raise(Guid.NewGuid())));
  Assert.Equal(409,Status(await Raise(Guid.NewGuid(),"ELIGIBILITY_CRITERIA",Guid.NewGuid())));
  Assert.Equal(403,Status(await Raise(Guid.NewGuid(),"ELIGIBILITY_CRITERIA",actor:"actor-b")));
  Assert.Equal(403,Status(await Raise(Guid.NewGuid(),"ELIGIBILITY_CRITERIA",actor:"actor-revoked")));
  Assert.Equal(400,Status(await Raise(Guid.NewGuid(),"VERIFIED")));
  Assert.Equal(503,Status(await Raise(Guid.NewGuid(),"ELIGIBILITY_CRITERIA",config:Config(reviewWriter:Read))));
  Assert.Equal(503,Status(await Raise(Guid.NewGuid(),"ELIGIBILITY_CRITERIA",config:Config(reviewWriter:Admin))));
  var retrieved=await List();
  Assert.Equal(200,Status(retrieved));
  using(var doc=JsonDocument.Parse(JsonSerializer.Serialize(
    Assert.IsAssignableFrom<IValueHttpResult>(retrieved).Value)))
  {
   var root=doc.RootElement;
   Assert.Equal("REQUESTED",root.GetProperty("registrationState").GetString());
   Assert.Equal(JsonValueKind.Null,root.GetProperty("enrollmentDecision").ValueKind);
   Assert.False(root.GetProperty("seatReserved").GetBoolean());
   var requirements=root.GetProperty("reviewRequests");
   Assert.Equal(1,requirements.GetArrayLength());
   Assert.Equal(review,requirements[0].GetProperty("reviewRequestId").GetGuid());
   Assert.Equal("IDENTITY_AUTHORITY",requirements[0].GetProperty("reviewTopic").GetString());
  }
  Assert.Equal(404,Status(await List(center:"center-b")));
  Assert.Equal(404,Status(await List(actor:"actor-revoked")));
  Assert.Equal(404,Status(await List(classId:Guid.NewGuid())));
  Assert.Equal(503,Status(await List(config:Config(reviewReader:ReviewWrite))));
  Assert.Equal(403,Status(await CenterRegistrationReviewEndpoint.List(
    User("actor-a"),Config(),new UnapprovedCenterRegistrationReviewAuthorization(),
    "center-a",cls,registration,default)));
  await using var verify=new NpgsqlConnection(Admin);
  await verify.OpenAsync();
  await using var counts=new NpgsqlCommand(
   """
   SELECT
   (SELECT count(*) FROM learning.registration_review_requests
     WHERE registration_request_id=@request),
   (SELECT count(*) FROM governance.command_audit
     WHERE action_code='REGISTRATION_HUMAN_REVIEW_REQUESTED' AND target_id=@review),
   (SELECT count(*) FROM governance.outbox_events
     WHERE event_type='RegistrationHumanReviewRequested' AND aggregate_id=@review),
   (SELECT count(*) FROM learning.class_registration_intents
     WHERE request_id=@request AND state='REQUESTED')
   """,verify);
  counts.Parameters.AddWithValue("request",registration);
  counts.Parameters.AddWithValue("review",review.ToString("D"));
  await using var reader=await counts.ExecuteReaderAsync();
  Assert.True(await reader.ReadAsync());
  for(var i=0;i<4;i++)Assert.Equal(1L,reader.GetInt64(i));
 }
}
