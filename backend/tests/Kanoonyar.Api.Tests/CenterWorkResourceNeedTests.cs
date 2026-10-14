using System.Security.Claims;
using Kanoonyar.Api.Programs;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Xunit;
namespace Kanoonyar.Api.Tests;
public sealed class CenterWorkResourceNeedTests
{
 private sealed class NeedGrant(bool ok):ICenterWorkResourceNeedAuthorization
 {
  public ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal a,string c,Guid card,string op,CancellationToken ct)
   =>ValueTask.FromResult(ok);
 }
 private sealed class CardGrant(bool ok):ICenterWorkCardAuthorization
 {
  public ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal a,string c,string op,Guid card,CancellationToken ct)
   =>ValueTask.FromResult(ok);
 }
 private sealed class PlanGrant(bool ok):ILocalPlanDraftReadAuthorization
 {
  public ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal a,string c,CancellationToken ct)
   =>ValueTask.FromResult(ok);
 }
 private static ClaimsPrincipal Actor(bool sub=true)=>new(new ClaimsIdentity(
  sub?[new Claim("sub","s39-test"),new Claim("role","superadmin")]:
   [new Claim("role","superadmin")],"synthetic"));
 private static IConfiguration Empty=>new ConfigurationBuilder().Build();
 private static int? Status(IResult r)=>(r as IStatusCodeHttpResult)?.StatusCode;
 [Fact] public async Task Source_identity_and_three_independent_rights_are_required()
 {
  var card=Guid.NewGuid();
  var req=new AppendWorkResourceNeedRequest(Guid.NewGuid(),0,"Laptop","Planning only",null);
  Task<IResult> Post(ClaimsPrincipal? actor=null,AppendWorkResourceNeedRequest? data=null,
   bool grant=true,bool source=true,bool plan=true)=>
   CenterWorkResourceNeedEndpoint.Append(actor??Actor(),Empty,new NeedGrant(grant),
    new CardGrant(source),new PlanGrant(plan),"center-a",card,data??req,default);
  Assert.Equal(401,Status(await Post(actor:new ClaimsPrincipal())));
  Assert.Equal(403,Status(await Post(actor:Actor(false))));
  Assert.Equal(400,Status(await Post(data:req with{NeedId=Guid.Empty})));
  Assert.Equal(400,Status(await Post(data:req with{ExpectedRevision=-1})));
  Assert.Equal(400,Status(await Post(data:req with{ResourceLabel=" "})));
  Assert.Equal(400,Status(await Post(data:req with{ResourceLabel=new string('x',161)})));
  Assert.Equal(400,Status(await Post(data:req with{PlanningDetail=new string('x',2001)})));
  Assert.Equal(403,Status(await Post(grant:false)));
  Assert.Equal(403,Status(await Post(source:false)));
  Assert.Equal(403,Status(await Post(plan:false)));
  Assert.Equal(503,Status(await Post()));
  Assert.Equal(403,Status(await CenterWorkResourceNeedEndpoint.List(Actor(),Empty,
   new NeedGrant(false),new CardGrant(true),new PlanGrant(true),
   "center-a",card,null,default)));
  Assert.Equal(403,Status(await CenterWorkResourceNeedEndpoint.List(Actor(),Empty,
   new NeedGrant(true),new CardGrant(false),new PlanGrant(true),
   "center-a",card,null,default)));
  Assert.Equal(400,Status(await CenterWorkResourceNeedEndpoint.List(Actor(),Empty,
   new NeedGrant(true),new CardGrant(true),new PlanGrant(true),
   "center-a",card,Guid.Empty,default)));
  Assert.Equal(503,Status(await CenterWorkResourceNeedEndpoint.List(Actor(),Empty,
   new NeedGrant(true),new CardGrant(true),new PlanGrant(true),
   "center-a",card,null,default)));
  Assert.Equal(400,Status(await CenterWorkResourceNeedEndpoint.History(Actor(),Empty,
   new NeedGrant(true),new CardGrant(true),new PlanGrant(true),
   "center-a",card,Guid.Empty,null,default)));
  Assert.Equal(400,Status(await CenterWorkResourceNeedEndpoint.History(Actor(),Empty,
   new NeedGrant(true),new CardGrant(true),new PlanGrant(true),
   "center-a",card,Guid.NewGuid(),0,default)));
 }
}
