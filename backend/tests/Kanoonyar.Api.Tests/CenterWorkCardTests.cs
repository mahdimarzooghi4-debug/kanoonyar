using System.Security.Claims;
using Kanoonyar.Api.Programs;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Xunit;
namespace Kanoonyar.Api.Tests;
public sealed class CenterWorkCardTests
{
 private sealed class Grant(bool allowed=true):ICenterWorkCardAuthorization
 {
  public ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal a,string c,string op,Guid id,CancellationToken ct)
   =>ValueTask.FromResult(allowed);
 }
 private sealed class Source(bool allowed=true):ILocalPlanDraftReadAuthorization
 {
  public ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal a,string c,CancellationToken ct)
   =>ValueTask.FromResult(allowed);
 }
 private static ClaimsPrincipal Actor(bool subject=true)=>new(new ClaimsIdentity(
  subject?[new Claim("sub","synthetic"),new Claim("role","superadmin")]:
  [new Claim("role","superadmin")],"test"));
 private static IConfiguration Empty=>new ConfigurationBuilder().Build();
 private static int? Status(IResult r)=>(r as IStatusCodeHttpResult)?.StatusCode;
 private static AppendCenterWorkCardRequest Request()=>
  new(Guid.NewGuid(),0,Guid.NewGuid(),1,"Planning title","",null);
 [Fact] public async Task Guard_rejects_invalid_input_role_only_and_missing_independent_grants()
 {
  var request=Request();
  Task<IResult> Write(ClaimsPrincipal? actor=null,AppendCenterWorkCardRequest? body=null,
   bool grant=true,bool source=true)=>
   CenterWorkCardEndpoint.Append(actor??Actor(),Empty,new Grant(grant),new Source(source),
    "center-a",body??request,default);
  Assert.Equal(401,Status(await Write(actor:new ClaimsPrincipal())));
  Assert.Equal(403,Status(await Write(actor:Actor(false))));
  Assert.Equal(400,Status(await Write(body:request with{ExpectedRevision=-1})));
  Assert.Equal(400,Status(await Write(body:request with{DraftId=Guid.Empty})));
  Assert.Equal(400,Status(await Write(body:request with{Title=""})));
  Assert.Equal(400,Status(await Write(body:request with{Title=new string('a',161)})));
  Assert.Equal(400,Status(await Write(body:request with{PlanningNote=new string('x',1201)})));
  Assert.Equal(400,Status(await Write(body:request with{PlannedDueAt=new DateTimeOffset(2026,11,2,10,0,0,TimeSpan.FromHours(4))})));
  Assert.Equal(403,Status(await Write(grant:false)));
  Assert.Equal(403,Status(await Write(source:false)));
  Assert.Equal(503,Status(await Write()));
  Assert.Equal(403,Status(await CenterWorkCardEndpoint.List(
   Actor(),Empty,new Grant(false),new Source(),"center-a",null,default)));
  Assert.Equal(403,Status(await CenterWorkCardEndpoint.List(
   Actor(),Empty,new Grant(),new Source(false),"center-a",null,default)));
  Assert.Equal(503,Status(await CenterWorkCardEndpoint.List(
   Actor(),Empty,new Grant(),new Source(),"center-a",null,default)));
  Assert.Equal(400,Status(await CenterWorkCardEndpoint.List(
   Actor(),Empty,new Grant(),new Source(),"center-a",Guid.Empty,default)));
  Assert.Equal(400,Status(await CenterWorkCardEndpoint.History(
   Actor(),Empty,new Grant(),new Source(),"center-a",Guid.NewGuid(),0,default)));
 }
}
