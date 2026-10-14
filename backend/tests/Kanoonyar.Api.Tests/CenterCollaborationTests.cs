using System.Security.Claims;
using Kanoonyar.Api.Programs;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Kanoonyar.Api.Tests;
public sealed class CenterCollaborationTests
{
 private sealed class Grant(bool allow):ICenterCollaborationAuthorization
 {
  public ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal actor,string center,
   string operation,CancellationToken ct)=>ValueTask.FromResult(allow);
 }
 private static ClaimsPrincipal Actor(bool sub=true)=>new(new ClaimsIdentity(
  sub?[new Claim("sub","actor-a"),new Claim("role","superadmin")]:
      [new Claim("role","superadmin")],"synthetic"));
 private static IConfiguration Empty=>new ConfigurationBuilder().Build();
 private static int? Status(IResult r)=>(r as IStatusCodeHttpResult)?.StatusCode;
 private static SubmitCenterCollaborationProposalRequest Valid()=>
  new(Guid.NewGuid(),"center-b","Collaboration proposal","Shared summary");
 [Fact] public async Task Create_and_read_require_separate_grants_and_valid_explicit_target()
 {
  var ok=new Grant(true);var deny=new Grant(false);
  var valid=Valid();
  Task<IResult> Post(ClaimsPrincipal? actor=null,SubmitCenterCollaborationProposalRequest? req=null,
   ICenterCollaborationAuthorization? grant=null)=>
   CenterCollaborationEndpoint.Submit(actor??Actor(),Empty,grant??ok,"center-a",req??valid,default);
  Assert.Equal(401,Status(await Post(actor:new ClaimsPrincipal())));
  Assert.Equal(403,Status(await Post(actor:Actor(false))));
  Assert.Equal(400,Status(await Post(req:valid with{ProposalId=Guid.Empty})));
  Assert.Equal(400,Status(await Post(req:valid with{TargetCenterId="center-a"})));
  Assert.Equal(400,Status(await Post(req:valid with{TargetCenterId=""})));
  Assert.Equal(400,Status(await Post(req:valid with{Title="  "})));
  Assert.Equal(400,Status(await Post(req:valid with{Title=new string('a',161)})));
  Assert.Equal(400,Status(await Post(req:valid with{Summary=new string('x',1201)})));
  Assert.Equal(403,Status(await Post(grant:deny)));
  Assert.Equal(503,Status(await Post()));
  Assert.Equal(401,Status(await CenterCollaborationEndpoint.Incoming(
   new ClaimsPrincipal(),Empty,ok,"center-b",null,default)));
  Assert.Equal(403,Status(await CenterCollaborationEndpoint.Incoming(
   Actor(),Empty,deny,"center-b",null,default)));
  Assert.Equal(403,Status(await CenterCollaborationEndpoint.Outgoing(
   Actor(),Empty,deny,"center-a",null,default)));
  Assert.Equal(400,Status(await CenterCollaborationEndpoint.Outgoing(
   Actor(),Empty,ok,"center-a",Guid.Empty,default)));
  Assert.Equal(503,Status(await CenterCollaborationEndpoint.Incoming(
   Actor(),Empty,ok,"center-b",null,default)));
  Assert.Equal(503,Status(await CenterCollaborationEndpoint.Outgoing(
   Actor(),Empty,ok,"center-a",null,default)));
 }
}
