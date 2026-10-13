using System.Security.Claims;
using System.Text.Json;
using Kanoonyar.Api.Programs;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Xunit;
namespace Kanoonyar.Api.Tests;
public sealed class CenterRecipientNotesPostgresTests
{
 private static string? Admin=>Environment.GetEnvironmentVariable("CENTER_DRAFT_INTEGRATION_ADMIN_URL");
 private static string? Reader=>Environment.GetEnvironmentVariable("CENTER_DRAFT_INTEGRATION_READ_DATABASE_URL");
 private static string? Writer=>Environment.GetEnvironmentVariable("CENTER_RECIPIENT_NOTE_INTEGRATION_DATABASE_URL");
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
 private sealed class Grant(bool incoming=true,bool note=true):ICenterCollaborationAuthorization
 {
  public ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal p,string c,string op,CancellationToken ct)=>
   ValueTask.FromResult(op=="COLLABORATION_INCOMING_READ"?incoming:note);
 }
 private static ClaimsPrincipal Actor(string subject)=>new(new ClaimsIdentity(
  [new Claim("sub",subject)],"synthetic"));
 private static IConfiguration Config(string? write=null,string? read=null)=>new ConfigurationBuilder()
  .AddInMemoryCollection(new Dictionary<string,string?>{
   ["CENTER_RECIPIENT_NOTE_WRITE_DATABASE_URL"]=write??Writer,
   ["CENTER_DRAFT_READ_DATABASE_URL"]=read??Reader}).Build();
 private static int? Status(IResult r)=>(r as IStatusCodeHttpResult)?.StatusCode;
 private static JsonDocument Json(IResult r)=>JsonDocument.Parse(JsonSerializer.Serialize(
  Assert.IsAssignableFrom<IValueHttpResult>(r).Value,
  new JsonSerializerOptions(JsonSerializerDefaults.Web)));
 [Fact] public async Task Destination_only_private_notes_are_immutable_and_never_accept_proposal()
 {
  if(!Enabled())return;
  const string a="s36-source",b="s36-recipient",c="s36-third";
  var id=Guid.NewGuid();var another=Guid.NewGuid();
  await using(var db=new NpgsqlConnection(Admin))
  {
   await db.OpenAsync();
   await using var q=new NpgsqlCommand("""
    INSERT INTO centers.registry(center_id,display_name,is_active,registered_by_subject,registered_at)
    VALUES('s36-source','Sender',true,'fixture',now()),
          ('s36-recipient','Destination',true,'fixture',now()),
          ('s36-third','Third',true,'fixture',now());
    INSERT INTO centers.memberships(center_id,subject_id,role_code,is_active,recorded_by_subject,recorded_at)
    VALUES('s36-source','s36-sender','fixture',true,'fixture',now()),
          ('s36-recipient','s36-reader','fixture',true,'fixture',now()),
          ('s36-recipient','s36-revoked','fixture',false,'fixture',now()),
          ('s36-third','s36-third-actor','fixture',true,'fixture',now());
    INSERT INTO collaboration.center_proposals(proposal_id,source_center_id,target_center_id,
       title,summary,created_by_subject) VALUES
      (@id,'s36-source','s36-recipient','Original proposal','Exact minimal share','s36-sender'),
      (@another,'s36-source','s36-third','Different target','Different summary','s36-sender');
    """,db);
   q.Parameters.AddWithValue("id",id);
   q.Parameters.AddWithValue("another",another);
   await q.ExecuteNonQueryAsync();
  }
  Task<IResult> Post(Guid note,Guid proposal,string content="Private target observation",
    string center=b,string subject="s36-reader",IConfiguration? config=null,
    bool incoming=true,bool granted=true)=>
   CenterRecipientPrivateNotesEndpoint.Append(Actor(subject),config??Config(),
    new Grant(incoming,granted),center,proposal,new AppendRecipientPrivateNoteRequest(note,content),default);
  Task<IResult> List(Guid proposal,Guid? before=null,string center=b,
    string subject="s36-reader",IConfiguration? cfg=null,
    bool incoming=true,bool granted=true)=>
   CenterRecipientPrivateNotesEndpoint.List(Actor(subject),cfg??Config(),
    new Grant(incoming,granted),center,proposal,before,default);
  var first=Guid.NewGuid();
  Assert.Equal(403,Status(await Post(first,id,subject:"s36-revoked")));
  Assert.Equal(403,Status(await Post(first,id,subject:"s36-sender",center:a)));
  Assert.Equal(403,Status(await Post(first,id,subject:"s36-third-actor",center:b)));
  Assert.Equal(403,Status(await Post(first,id,incoming:false)));
  Assert.Equal(403,Status(await Post(first,id,granted:false)));
  Assert.Equal(503,Status(await Post(first,id,config:Config(write:Admin))));
  Assert.Equal(503,Status(await Post(first,id,config:Config(write:Reader))));
  Assert.Equal(403,Status(await Post(first,another)));
  Assert.Equal(200,Status(await Post(first,id)));
  Assert.Equal(409,Status(await Post(first,id)));
  var all=new List<Guid>{first};
  for(var n=0;n<20;n++)
  {
   var note=Guid.NewGuid();all.Add(note);
   Assert.Equal(200,Status(await Post(note,id,content:"Private recipient note "+n)));
  }
  Assert.Equal(404,Status(await List(id,subject:"s36-sender",center:a)));
  Assert.Equal(403,Status(await List(id,subject:"s36-revoked")));
  Assert.Equal(403,Status(await List(id,subject:"s36-third-actor",center:b)));
  Assert.Equal(403,Status(await List(id,incoming:false)));
  Assert.Equal(403,Status(await List(id,granted:false)));
  Assert.Equal(404,Status(await List(another)));
  Assert.Equal(503,Status(await List(id,cfg:Config(read:Admin))));
  Assert.Equal(503,Status(await List(id,cfg:Config(read:Writer))));
  Guid? cursor;
  using(var data=Json(await List(id)))
  {
   var root=data.RootElement;
   Assert.Equal(id,root.GetProperty("proposalId").GetGuid());
   Assert.Equal(b,root.GetProperty("targetCenterId").GetString());
   Assert.False(root.GetProperty("isAccepted").GetBoolean());
   Assert.False(root.GetProperty("isExecuted").GetBoolean());
   Assert.Equal("PROPOSED_UNREVIEWED",root.GetProperty("reviewStatus").GetString());
   var notes=root.GetProperty("notes");Assert.Equal(20,notes.GetArrayLength());
   cursor=root.GetProperty("nextBeforeNoteId").GetGuid();
   foreach(var note in notes.EnumerateArray())
   {
    Assert.Equal("RECIPIENT_INTERNAL",note.GetProperty("visibility").GetString());
    Assert.False(note.TryGetProperty("recordedBySubject",out _));
    Assert.False(note.GetProperty("isAccepted").GetBoolean());
   }
  }
  using(var data=Json(await List(id,before:cursor)))
  {
   Assert.Single(data.RootElement.GetProperty("notes").EnumerateArray());
   Assert.Equal(JsonValueKind.Null,data.RootElement.GetProperty("nextBeforeNoteId").ValueKind);
  }
  using(var data=Json(await List(another,center:c,subject:"s36-third-actor")))
  {
   Assert.Empty(data.RootElement.GetProperty("notes").EnumerateArray());
  }
  await using var admin=new NpgsqlConnection(Admin);await admin.OpenAsync();
  await using(var q=new NpgsqlCommand("""
   SELECT (SELECT count(*) FROM collaboration.recipient_private_notes WHERE proposal_id=@id),
    (SELECT count(*) FROM governance.command_audit WHERE center_id=@center
     AND action_code='RECIPIENT_PRIVATE_NOTE_RECORDED'),
    (SELECT count(*) FROM governance.outbox_events
     WHERE event_type='RecipientPrivateNoteRecorded'
      AND (payload->>'target_center_id')=@center)
   """,admin))
  {
   q.Parameters.AddWithValue("id",id);
   q.Parameters.AddWithValue("center",b);
   await using var rd=await q.ExecuteReaderAsync();
   Assert.True(await rd.ReadAsync());
   Assert.Equal(21L,rd.GetInt64(0));Assert.Equal(21L,rd.GetInt64(1));
   Assert.Equal(21L,rd.GetInt64(2));
  }
  await using(var tamper=new NpgsqlCommand("""
   UPDATE collaboration.recipient_private_notes SET note_text='forged' WHERE note_id=@note
   """,admin))
  {
   tamper.Parameters.AddWithValue("note",first);
   await Assert.ThrowsAsync<PostgresException>(()=>tamper.ExecuteNonQueryAsync());
  }
  await using(var reader=new NpgsqlConnection(Reader))
  {
   await reader.OpenAsync();
   await using var q=new NpgsqlCommand(
    "SELECT recorded_by_subject FROM collaboration.recipient_private_notes LIMIT 1",reader);
   var exception=await Assert.ThrowsAsync<PostgresException>(()=>q.ExecuteScalarAsync());
   Assert.Equal("42501",exception.SqlState);
  }
 }
}
