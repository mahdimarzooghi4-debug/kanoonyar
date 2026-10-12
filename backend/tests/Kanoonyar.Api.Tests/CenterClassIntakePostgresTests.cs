using System.Security.Claims;
using System.Text.Json;
using Kanoonyar.Api.Programs;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Xunit;

namespace Kanoonyar.Api.Tests;

/// <summary>Restricted PostgreSQL integration, synthetic actors/refs only; no real enrollment.</summary>
public sealed class CenterClassIntakePostgresTests
{
 private static string? Admin=>Environment.GetEnvironmentVariable("CENTER_DRAFT_INTEGRATION_ADMIN_URL");
 private static string? Read=>Environment.GetEnvironmentVariable("CENTER_DRAFT_INTEGRATION_READ_DATABASE_URL");
 private static string? Write=>Environment.GetEnvironmentVariable("CENTER_CLASS_INTAKE_INTEGRATION_DATABASE_URL");
 private static bool Enabled()
 {
  if(Environment.GetEnvironmentVariable("CENTER_DRAFT_INTEGRATION_REQUIRED")=="1")
  {
   Assert.False(string.IsNullOrWhiteSpace(Admin));Assert.False(string.IsNullOrWhiteSpace(Read));
   Assert.False(string.IsNullOrWhiteSpace(Write));
  }
  return !string.IsNullOrWhiteSpace(Admin)&&!string.IsNullOrWhiteSpace(Read)&&!string.IsNullOrWhiteSpace(Write);
 }
 private static IConfiguration Config(string? writer=null,string? reader=null)=>
  new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{
    ["CENTER_CLASS_INTAKE_WRITE_DATABASE_URL"]=writer??Write,
    ["CENTER_DRAFT_READ_DATABASE_URL"]=reader??Read
  }).Build();
 private static ClaimsPrincipal User(string subject)=>
    new(new ClaimsIdentity([new Claim("sub",subject)],"fixture"));
 private static int? Status(IResult r)=>(r as IStatusCodeHttpResult)?.StatusCode;
 private sealed class Permit:ICenterClassIntakeAuthorization
 {
  public ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal p,string c,Guid cls,
    string op,CancellationToken ct)=>ValueTask.FromResult(true);
 }
 [Fact]
 public async Task Capacity_is_versioned_and_registration_requests_are_unconfirmed_atomic_and_center_scoped()
 {
  if(!Enabled())return;
  var cls=Guid.NewGuid();var applicant=Guid.NewGuid();var request=Guid.NewGuid();
  await using(var admin=new NpgsqlConnection(Admin))
  {
   await admin.OpenAsync();
   await using var cmd=new NpgsqlCommand(
     "INSERT INTO learning.center_classes(center_id,class_id,title) VALUES('center-a',@id,'S18 synthetic course')",admin);
   cmd.Parameters.AddWithValue("id",cls);
   await cmd.ExecuteNonQueryAsync();
  }
  var grant=new Permit();
  Task<IResult> Capacity(long expected,int seats,string subject="actor-a",IConfiguration? config=null)=>
   CenterClassIntakeEndpoint.SetCapacity(User(subject),config??Config(),
     grant,"center-a",cls,new(expected,seats),default);
  Task<IResult> Request(Guid id,Guid who,string subject="actor-a",IConfiguration? config=null)=>
   CenterClassIntakeEndpoint.RequestRegistration(User(subject),config??Config(),grant,
     "center-a",cls,new(id,who),default);
  Task<IResult> ReadClass(string center="center-a",string subject="actor-a",
      IConfiguration? config=null,Guid? cursor=null)=>
   CenterClassIntakeEndpoint.Read(User(subject),config??Config(),grant,center,cls,cursor,default);

  var initial=await ReadClass();
  Assert.Equal(200,Status(initial));
  using(var data=JsonDocument.Parse(JsonSerializer.Serialize(
    Assert.IsAssignableFrom<IValueHttpResult>(initial).Value)))
  {
   Assert.Equal(JsonValueKind.Null,data.RootElement.GetProperty("currentRevision").ValueKind);
   Assert.Equal(JsonValueKind.Null,data.RootElement.GetProperty("configuredCapacity").ValueKind);
   Assert.Equal(JsonValueKind.Null,data.RootElement.GetProperty("confirmedEnrollmentCount").ValueKind);
   Assert.Empty(data.RootElement.GetProperty("registrationIntents").EnumerateArray());
  }
  Assert.Equal(409,Status(await Request(request,applicant))); // capacity not configured
  Assert.Equal(200,Status(await Capacity(0,1)));
  var competitors=await Task.WhenAll(Capacity(1,2),Capacity(1,3));
  Assert.Single(competitors.Where(x=>Status(x)==200));
  Assert.Single(competitors.Where(x=>Status(x)==409));
  Assert.Equal(409,Status(await Capacity(1,4)));
  var result=await Request(request,applicant);
  Assert.Equal(200,Status(result));
  using(var doc=JsonDocument.Parse(JsonSerializer.Serialize(
    Assert.IsAssignableFrom<IValueHttpResult>(result).Value)))
  {
   Assert.Equal(request,doc.RootElement.GetProperty("requestId").GetGuid());
   Assert.Equal(2L,doc.RootElement.GetProperty("capacityRevision").GetInt64());
   Assert.Equal("REQUESTED",doc.RootElement.GetProperty("state").GetString());
   Assert.False(doc.RootElement.GetProperty("seatReserved").GetBoolean());
  }
  var replay=await Task.WhenAll(Request(Guid.NewGuid(),applicant),Request(Guid.NewGuid(),applicant));
  Assert.All(replay,x=>Assert.Equal(409,Status(x)));
  Assert.Equal(409,Status(await Request(request,Guid.NewGuid())));
  Assert.Equal(403,Status(await Request(Guid.NewGuid(),Guid.NewGuid(),"actor-b")));
  Assert.Equal(403,Status(await Capacity(2,3,"actor-revoked")));
  Assert.Equal(404,Status(await ReadClass("center-b")));
  Assert.Equal(404,Status(await ReadClass(subject:"actor-revoked")));
  Assert.Equal(403,Status(await CenterClassIntakeEndpoint.Read(User("actor-a"),Config(),
     new UnapprovedCenterClassIntakeAuthorization(),"center-a",cls,null,default)));
  Assert.Equal(503,Status(await ReadClass(config:Config(reader:Write))));
  foreach(var unsafeUrl in new[]{Admin,Read})
    Assert.Equal(503,Status(await Capacity(2,4,config:Config(writer:unsafeUrl))));

  var state=await ReadClass();
  Assert.Equal(200,Status(state));
  using(var doc=JsonDocument.Parse(JsonSerializer.Serialize(
    Assert.IsAssignableFrom<IValueHttpResult>(state).Value)))
  {
   var root=doc.RootElement;
   Assert.Equal(2L,root.GetProperty("currentRevision").GetInt64());
   Assert.Null(root.GetProperty("confirmedEnrollmentCount").GetString());
   Assert.Single(root.GetProperty("registrationIntents").EnumerateArray());
   var row=root.GetProperty("registrationIntents")[0];
   Assert.Equal("REQUESTED",row.GetProperty("state").GetString());
   Assert.False(row.GetProperty("seatReserved").GetBoolean());
   Assert.Equal(2L,row.GetProperty("capacityRevision").GetInt64());
  }
  await using var verify=new NpgsqlConnection(Admin);
  await verify.OpenAsync();
  await using var count=new NpgsqlCommand(
    """
    SELECT
     (SELECT count(*) FROM learning.class_capacity_revisions WHERE class_id=@class),
     (SELECT count(*) FROM learning.class_registration_intents WHERE class_id=@class),
     (SELECT count(*) FROM governance.command_audit WHERE center_id='center-a'
        AND action_code='CLASS_CAPACITY_REVISION_APPENDED' AND target_id=@target),
     (SELECT count(*) FROM governance.outbox_events WHERE event_type='ClassRegistrationIntentRequested'
        AND aggregate_id=@request)
    """,verify);
  count.Parameters.AddWithValue("class",cls);
  count.Parameters.AddWithValue("target",cls.ToString("D"));
  count.Parameters.AddWithValue("request",request.ToString("D"));
  await using var reader=await count.ExecuteReaderAsync();
  Assert.True(await reader.ReadAsync());
  Assert.Equal(2L,reader.GetInt64(0));Assert.Equal(1L,reader.GetInt64(1));
  Assert.Equal(2L,reader.GetInt64(2));Assert.Equal(1L,reader.GetInt64(3));
 }
}
