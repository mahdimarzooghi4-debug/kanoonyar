using System.Security.Claims;
using System.Text.Json;
using Kanoonyar.Api.Programs;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Xunit;

namespace Kanoonyar.Api.Tests;
public sealed class CenterCollaborationPostgresTests
{
 private static string? Admin=>Environment.GetEnvironmentVariable("CENTER_DRAFT_INTEGRATION_ADMIN_URL");
 private static string? Reader=>Environment.GetEnvironmentVariable("CENTER_DRAFT_INTEGRATION_READ_DATABASE_URL");
 private static string? Writer=>Environment.GetEnvironmentVariable("CENTER_COLLABORATION_INTEGRATION_DATABASE_URL");
 private static bool Enabled()
 {
  if(Environment.GetEnvironmentVariable("CENTER_DRAFT_INTEGRATION_REQUIRED")=="1")
  {
   Assert.False(string.IsNullOrWhiteSpace(Admin));
   Assert.False(string.IsNullOrWhiteSpace(Reader));
   Assert.False(string.IsNullOrWhiteSpace(Writer));
  }
  return !string.IsNullOrWhiteSpace(Admin)&&!string.IsNullOrWhiteSpace(Reader)&&
   !string.IsNullOrWhiteSpace(Writer);
 }
 private sealed class Grant(bool allowed=true):ICenterCollaborationAuthorization
 {
  public ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal p,string c,string op,CancellationToken ct)=>
   ValueTask.FromResult(allowed);
 }
 private static ClaimsPrincipal Actor(string id)=>new(new ClaimsIdentity([new Claim("sub",id)],"signed-synthetic"));
 private static IConfiguration Config(string? writer=null,string? reader=null)=>
  new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{
   ["CENTER_COLLABORATION_WRITE_DATABASE_URL"]=writer??Writer,
   ["CENTER_DRAFT_READ_DATABASE_URL"]=reader??Reader
  }).Build();
 private static int? Status(IResult r)=>(r as IStatusCodeHttpResult)?.StatusCode;
 private static JsonDocument Json(IResult r)=>JsonDocument.Parse(JsonSerializer.Serialize(
  Assert.IsAssignableFrom<IValueHttpResult>(r).Value,new JsonSerializerOptions(JsonSerializerDefaults.Web)));
 [Fact] public async Task Explicit_directed_proposals_are_minimal_isolated_audited_and_not_accepted()
 {
  if(!Enabled())return;
  const string a="collaboration-s34-a",b="collaboration-s34-b",
   c="collaboration-s34-c",inactive="collaboration-s34-inactive";
  await using(var db=new NpgsqlConnection(Admin))
  {
   await db.OpenAsync();
   await using var q=new NpgsqlCommand("""
    INSERT INTO centers.registry(center_id,display_name,is_active,registered_by_subject,registered_at)
    VALUES('collaboration-s34-a','A',true,'fixture',now()),
          ('collaboration-s34-b','B',true,'fixture',now()),
          ('collaboration-s34-c','C',true,'fixture',now()),
          ('collaboration-s34-inactive','Inactive',false,'fixture',now());
    INSERT INTO centers.memberships(center_id,subject_id,role_code,is_active,recorded_by_subject,recorded_at)
    VALUES('collaboration-s34-a','s34-sender','fixture',true,'fixture',now()),
          ('collaboration-s34-b','s34-recipient','fixture',true,'fixture',now()),
          ('collaboration-s34-c','s34-third','fixture',true,'fixture',now()),
          ('collaboration-s34-a','s34-revoked','fixture',false,'fixture',now());
    """,db);
   await q.ExecuteNonQueryAsync();
  }
  Task<IResult> Create(Guid id,string target=b,string sender="s34-sender",string center=a,
   IConfiguration? config=null,bool allowed=true)=>
    CenterCollaborationEndpoint.Submit(Actor(sender),config??Config(),
     new Grant(allowed),center,new SubmitCenterCollaborationProposalRequest(
      id,target,"Shared proposal","Only authorized plain-text summary"),default);
  Task<IResult> Read(string center,string subject,bool incoming,
   Guid? before=null,IConfiguration? config=null,bool allowed=true)=>
   incoming?CenterCollaborationEndpoint.Incoming(Actor(subject),config??Config(),
     new Grant(allowed),center,before,default):
   CenterCollaborationEndpoint.Outgoing(Actor(subject),config??Config(),
     new Grant(allowed),center,before,default);
  var first=Guid.NewGuid();
  Assert.Equal(403,Status(await Create(first,sender:"s34-revoked")));
  Assert.Equal(403,Status(await Create(first,sender:"s34-recipient")));
  Assert.Equal(403,Status(await Create(first,allowed:false)));
  Assert.Equal(503,Status(await Create(first,config:Config(writer:Admin))));
  Assert.Equal(503,Status(await Create(first,config:Config(writer:Reader))));
  Assert.Equal(409,Status(await Create(first,target:inactive)));
  Assert.Equal(409,Status(await Create(first,target:"no-such-center")));
  Assert.Equal(200,Status(await Create(first)));
  Assert.Equal(409,Status(await Create(first)));
  var ids=new List<Guid>{first};
  for(int i=0;i<20;i++)
  {
   var id=Guid.NewGuid();ids.Add(id);
   Assert.Equal(200,Status(await Create(id)));
  }
  var forThird=Guid.NewGuid();
  Assert.Equal(200,Status(await Create(forThird,target:c)));
  Assert.Equal(403,Status(await Read(b,"s34-third",true)));
  Assert.Equal(403,Status(await Read(a,"s34-revoked",false)));
  Assert.Equal(403,Status(await Read(b,"s34-recipient",true,allowed:false)));
  Assert.Equal(503,Status(await Read(b,"s34-recipient",true,config:Config(reader:Admin))));
  Assert.Equal(503,Status(await Read(b,"s34-recipient",true,config:Config(reader:Writer))));
  Guid? cursor;
  using(var j=Json(await Read(b,"s34-recipient",true)))
  {
   var root=j.RootElement;
   Assert.Equal("INCOMING",root.GetProperty("direction").GetString());
   var entries=root.GetProperty("proposals");
   Assert.Equal(20,entries.GetArrayLength());
   cursor=root.GetProperty("nextBeforeProposalId").GetGuid();
   foreach(var p in entries.EnumerateArray())
   {
    Assert.Equal(a,p.GetProperty("sourceCenterId").GetString());
    Assert.Equal(b,p.GetProperty("targetCenterId").GetString());
    Assert.False(p.GetProperty("isAccepted").GetBoolean());
    Assert.False(p.GetProperty("isExecuted").GetBoolean());
    Assert.Equal("PROPOSED_UNREVIEWED",p.GetProperty("reviewStatus").GetString());
    Assert.False(p.TryGetProperty("createdBySubject",out _));
   }
   Assert.DoesNotContain(forThird.ToString("D"),root.GetRawText());
  }
  using(var j=Json(await Read(b,"s34-recipient",true,before:cursor)))
  {
   Assert.Single(j.RootElement.GetProperty("proposals").EnumerateArray());
   Assert.Equal(JsonValueKind.Null,j.RootElement.GetProperty("nextBeforeProposalId").ValueKind);
  }
  using(var j=Json(await Read(c,"s34-third",true)))
  {
   var entries=j.RootElement.GetProperty("proposals");
   Assert.Single(entries.EnumerateArray());
   Assert.Equal(forThird,entries[0].GetProperty("proposalId").GetGuid());
  }
  using(var j=Json(await Read(a,"s34-sender",false)))
  {
   Assert.Equal(20,j.RootElement.GetProperty("proposals").GetArrayLength());
  }
  using(var j=Json(await Read(b,"s34-recipient",false)))
  {
   Assert.Empty(j.RootElement.GetProperty("proposals").EnumerateArray());
  }
  using(var j=Json(await Read(a,"s34-sender",true)))
  {
   Assert.Empty(j.RootElement.GetProperty("proposals").EnumerateArray());
  }
  await using var conn=new NpgsqlConnection(Admin);await conn.OpenAsync();
  await using(var counter=new NpgsqlCommand("""
    SELECT
    (SELECT count(*) FROM collaboration.center_proposals WHERE source_center_id=@a),
    (SELECT count(*) FROM governance.command_audit
      WHERE center_id=@a AND action_code='CENTER_COLLABORATION_PROPOSAL_RECORDED'),
    (SELECT count(*) FROM governance.outbox_events
      WHERE event_type='CenterCollaborationProposalRecorded'
      AND (payload->>'source_center_id')=@a)
    """,conn))
  {
   counter.Parameters.AddWithValue("a",a);
   await using var rd=await counter.ExecuteReaderAsync();
   Assert.True(await rd.ReadAsync());
   Assert.Equal(22L,rd.GetInt64(0));
   Assert.Equal(22L,rd.GetInt64(1));
   Assert.Equal(22L,rd.GetInt64(2));
  }
  await using(var tamper=new NpgsqlCommand("""
   UPDATE collaboration.center_proposals SET summary='forged' WHERE proposal_id=@id
   """,conn))
  {
   tamper.Parameters.AddWithValue("id",first);
   await Assert.ThrowsAsync<PostgresException>(()=>tamper.ExecuteNonQueryAsync());
  }
  await using(var readOnly=new NpgsqlConnection(Reader))
  {
   await readOnly.OpenAsync();
   await using var q=new NpgsqlCommand(
    "SELECT created_by_subject FROM collaboration.center_proposals LIMIT 1",readOnly);
   var failure=await Assert.ThrowsAsync<PostgresException>(()=>q.ExecuteScalarAsync());
   Assert.Equal("42501",failure.SqlState);
  }
 }
}
