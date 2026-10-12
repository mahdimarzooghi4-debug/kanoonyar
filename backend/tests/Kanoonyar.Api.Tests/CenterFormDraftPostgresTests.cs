using System.Security.Claims;
using System.Text.Json;
using Kanoonyar.Api.Programs;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Xunit;

namespace Kanoonyar.Api.Tests;

/// <summary>Disposable PostgreSQL, synthetic center actors and separate restricted SQL logins.</summary>
public sealed class CenterFormDraftPostgresTests
{
 private static string? Admin=>Environment.GetEnvironmentVariable("CENTER_DRAFT_INTEGRATION_ADMIN_URL");
 private static string? Read=>Environment.GetEnvironmentVariable("CENTER_DRAFT_INTEGRATION_READ_DATABASE_URL");
 private static string? Write=>Environment.GetEnvironmentVariable("CENTER_FORM_DRAFT_INTEGRATION_DATABASE_URL");
 private static bool Enabled()
 {
  if(Environment.GetEnvironmentVariable("CENTER_DRAFT_INTEGRATION_REQUIRED")=="1")
  {
   Assert.False(string.IsNullOrWhiteSpace(Admin));
   Assert.False(string.IsNullOrWhiteSpace(Read));
   Assert.False(string.IsNullOrWhiteSpace(Write));
  }
  return !string.IsNullOrWhiteSpace(Admin)&&!string.IsNullOrWhiteSpace(Read)
   &&!string.IsNullOrWhiteSpace(Write);
 }
 private static IConfiguration Config(string? writer=null,string? reader=null)=>
  new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{
   ["CENTER_FORM_DRAFT_WRITE_DATABASE_URL"]=writer??Write,
   ["CENTER_DRAFT_READ_DATABASE_URL"]=reader??Read
  }).Build();
 private static ClaimsPrincipal Actor(string sub)=>
  new(new ClaimsIdentity([new Claim("sub",sub)],"synthetic"));
 private static int? Status(IResult x)=>(x as IStatusCodeHttpResult)?.StatusCode;
 private sealed class Grant:ICenterFormDraftAuthorization
 {
  public ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal actor,string center,Guid form,
    string operation,CancellationToken ct)=>ValueTask.FromResult(true);
 }
 private static List<FormDraftField> Definition()=>
  [new("topic","Topic","SHORT_TEXT",true,[]),
   new("selection","Choice","SINGLE_SELECT",false,["First","Second"])];
 [Fact]
 public async Task Full_form_draft_api_is_atomic_versioned_private_and_cross_center_safe()
 {
  if(!Enabled())return;
  var formId=Guid.NewGuid();
  var different=Guid.NewGuid();
  var grant=new Grant();
  Task<IResult> Append(Guid form,long expected,string slug,string title="Synthetic title",
   string actor="actor-a",string center="center-a",IConfiguration? config=null,
   List<FormDraftField>? fields=null)=>
   CenterFormDraftEndpoint.Append(Actor(actor),config??Config(),grant,center,
      new AppendCenterFormDraftRequest(form,expected,slug,title,fields??Definition()),default);
  Task<IResult> List(string center="center-a",string actor="actor-a",IConfiguration? config=null)=>
   CenterFormDraftEndpoint.List(Actor(actor),config??Config(),grant,center,null,default);
  Task<IResult> History(string center="center-a",string actor="actor-a",IConfiguration? config=null)=>
   CenterFormDraftEndpoint.History(Actor(actor),config??Config(),grant,center,formId,null,default);

  var created=await Append(formId,0,"interest-form");
  Assert.Equal(200,Status(created));
  using(var doc=JsonDocument.Parse(JsonSerializer.Serialize(
   Assert.IsAssignableFrom<IValueHttpResult>(created).Value)))
  {
   var x=doc.RootElement;
   Assert.Equal(formId,x.GetProperty("formId").GetGuid());
   Assert.Equal(1L,x.GetProperty("revision").GetInt64());
   Assert.Equal("PRIVATE_DRAFT",x.GetProperty("visibility").GetString());
   Assert.False(x.GetProperty("isPublished").GetBoolean());
   Assert.False(x.GetProperty("acceptsSubmissions").GetBoolean());
  }
  var competitors=await Task.WhenAll(Append(formId,1,"interest-form","v2"),
    Append(formId,1,"interest-form","concurrent"));
  Assert.Single(competitors,x=>Status(x)==200);
  Assert.Single(competitors,x=>Status(x)==409);
  Assert.Equal(409,Status(await Append(formId,1,"interest-form","stale")));
  Assert.Equal(409,Status(await Append(different,0,"interest-form","slug collision")));
  Assert.Equal(409,Status(await Append(formId,2,"changed-slug","immutable")));
  Assert.Equal(403,Status(await Append(Guid.NewGuid(),0,"cross",actor:"actor-b")));
  Assert.Equal(403,Status(await Append(Guid.NewGuid(),0,"revoked",actor:"actor-revoked")));
  Assert.Equal(400,Status(await Append(Guid.NewGuid(),0,"unsafe",fields:[
    new FormDraftField("x","X","SINGLE_SELECT",false,["dup","dup"])
  ])));
  Assert.Equal(403,Status(await CenterFormDraftEndpoint.Append(Actor("actor-a"),Config(),
    new UnapprovedCenterFormDraftAuthorization(),"center-a",
    new AppendCenterFormDraftRequest(Guid.NewGuid(),0,"denied","Denied",Definition()),default)));
  foreach(var unsafeUrl in new[]{Admin,Read})
   Assert.Equal(503,Status(await Append(Guid.NewGuid(),0,"unsafe",config:Config(writer:unsafeUrl))));

  var list=await List();
  Assert.Equal(200,Status(list));
  using(var doc=JsonDocument.Parse(JsonSerializer.Serialize(
   Assert.IsAssignableFrom<IValueHttpResult>(list).Value)))
  {
   var x=doc.RootElement.GetProperty("forms");
   Assert.Single(x.EnumerateArray());
   Assert.Equal(formId,x[0].GetProperty("formId").GetGuid());
   Assert.Equal(2L,x[0].GetProperty("revision").GetInt64());
   Assert.Equal(2,x[0].GetProperty("fields").GetArrayLength());
   Assert.Equal("SINGLE_SELECT",x[0].GetProperty("fields")[1].GetProperty("kind").GetString());
   Assert.False(x[0].GetProperty("acceptsSubmissions").GetBoolean());
  }
  var history=await History();
  Assert.Equal(200,Status(history));
  using(var doc=JsonDocument.Parse(JsonSerializer.Serialize(
   Assert.IsAssignableFrom<IValueHttpResult>(history).Value)))
  {
   var rows=doc.RootElement.GetProperty("revisions");
   Assert.Equal(2,rows.GetArrayLength());
   Assert.Equal(2L,rows[0].GetProperty("revision").GetInt64());
   Assert.Equal(1L,rows[1].GetProperty("revision").GetInt64());
   Assert.False(rows[0].GetProperty("isPublished").GetBoolean());
  }
  Assert.Equal(403,Status(await List(actor:"actor-revoked")));
  Assert.Equal(403,Status(await List(center:"center-b",actor:"actor-a")));
  var other=await List(center:"center-b",actor:"actor-b");
  Assert.Equal(200,Status(other));
  using(var doc=JsonDocument.Parse(JsonSerializer.Serialize(
   Assert.IsAssignableFrom<IValueHttpResult>(other).Value)))
   Assert.Empty(doc.RootElement.GetProperty("forms").EnumerateArray());
  Assert.Equal(404,Status(await History(center:"center-b",actor:"actor-b")));
  Assert.Equal(503,Status(await List(config:Config(reader:Write))));
  Assert.Equal(503,Status(await History(config:Config(reader:Admin))));

  await using var db=new NpgsqlConnection(Admin);await db.OpenAsync();
  await using var count=new NpgsqlCommand(
   """
   SELECT
    (SELECT count(*) FROM web.center_forms WHERE center_id='center-a' AND form_id=@form),
    (SELECT count(*) FROM web.center_form_draft_revisions WHERE center_id='center-a' AND form_id=@form),
    (SELECT count(*) FROM governance.command_audit
      WHERE action_code='CENTER_FORM_DRAFT_REVISION_APPENDED' AND target_id=@id),
    (SELECT count(*) FROM governance.outbox_events
      WHERE event_type='CenterFormDraftRevisionAppended' AND aggregate_id=@id),
    (SELECT count(*) FROM governance.command_audit
      WHERE action_code='CENTER_FORM_DRAFT_REVISION_APPENDED' AND metadata::text LIKE '%Topic%')
   """,db);
  count.Parameters.AddWithValue("form",formId);
  count.Parameters.AddWithValue("id",formId.ToString("D"));
  await using var reader=await count.ExecuteReaderAsync();
  Assert.True(await reader.ReadAsync());
  Assert.Equal(1L,reader.GetInt64(0));
  Assert.Equal(2L,reader.GetInt64(1));
  Assert.Equal(2L,reader.GetInt64(2));
  Assert.Equal(2L,reader.GetInt64(3));
  Assert.Equal(0L,reader.GetInt64(4));
 }
}
