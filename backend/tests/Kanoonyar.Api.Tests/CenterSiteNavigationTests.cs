using System.Security.Claims;
using Kanoonyar.Api.Programs;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Kanoonyar.Api.Tests;
public sealed class CenterSiteNavigationTests
{
 private sealed class Nav(bool allow):ICenterSiteNavigationAuthorization
 {
  public ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal actor,string center,
   string operation,CancellationToken ct)=>ValueTask.FromResult(allow);
 }
 private sealed class Pages(bool allow):ICenterSiteDraftAuthorization
 {
  public ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal actor,string center,Guid page,
   string operation,CancellationToken ct)=>ValueTask.FromResult(allow&&
    operation=="SITE_DRAFT_LIST_READ"&&page==Guid.Empty);
 }
 private static ClaimsPrincipal Actor(bool sub=true)=>new(new ClaimsIdentity(
  sub?[new Claim("sub","actor-a"),new Claim("role","superadmin")]:
      [new Claim("role","superadmin")],"test"));
 private static IConfiguration Empty=>new ConfigurationBuilder().Build();
 private static int? Status(IResult r)=>(r as IStatusCodeHttpResult)?.StatusCode;
 private static AppendCenterSiteNavigationRequest Valid()=>
  new(0,[new SiteNavigationPageRef(Guid.NewGuid(),1)]);
 [Fact] public async Task Signed_actor_is_insufficient_without_two_separate_permissions()
 {
  var yes=new Nav(true);var pageYes=new Pages(true);var req=Valid();
  Assert.Equal(401,Status(await CenterSiteNavigationEndpoint.Append(
   new ClaimsPrincipal(),Empty,yes,pageYes,"center-a",req,default)));
  Assert.Equal(403,Status(await CenterSiteNavigationEndpoint.Append(
   Actor(false),Empty,yes,pageYes,"center-a",req,default)));
  Assert.Equal(400,Status(await CenterSiteNavigationEndpoint.Append(
   Actor(),Empty,yes,pageYes,"center-a",null,default)));
  Assert.Equal(400,Status(await CenterSiteNavigationEndpoint.Append(
   Actor(),Empty,yes,pageYes,"center-a",req with{ExpectedRevision=-1},default)));
  Assert.Equal(400,Status(await CenterSiteNavigationEndpoint.Append(
   Actor(),Empty,yes,pageYes,"center-a",req with{Items=[
    new SiteNavigationPageRef(Guid.Empty,1)]},default)));
  var duplicate=Guid.NewGuid();
  Assert.Equal(400,Status(await CenterSiteNavigationEndpoint.Append(
   Actor(),Empty,yes,pageYes,"center-a",req with{Items=[
    new SiteNavigationPageRef(duplicate,1),
    new SiteNavigationPageRef(duplicate,2)]},default)));
  Assert.Equal(403,Status(await CenterSiteNavigationEndpoint.Append(
   Actor(),Empty,new Nav(false),pageYes,"center-a",req,default)));
  Assert.Equal(403,Status(await CenterSiteNavigationEndpoint.Append(
   Actor(),Empty,yes,new Pages(false),"center-a",req,default)));
  Assert.Equal(503,Status(await CenterSiteNavigationEndpoint.Append(
   Actor(),Empty,yes,pageYes,"center-a",req,default)));
  Assert.Equal(403,Status(await CenterSiteNavigationEndpoint.Latest(
   Actor(),Empty,new Nav(false),pageYes,"center-a",default)));
  Assert.Equal(403,Status(await CenterSiteNavigationEndpoint.Latest(
   Actor(),Empty,yes,new Pages(false),"center-a",default)));
  Assert.Equal(503,Status(await CenterSiteNavigationEndpoint.Latest(
   Actor(),Empty,yes,pageYes,"center-a",default)));
  Assert.Equal(400,Status(await CenterSiteNavigationEndpoint.History(
   Actor(),Empty,yes,pageYes,"center-a",0,default)));
  Assert.Equal(403,Status(await CenterSiteNavigationEndpoint.History(
   Actor(),Empty,yes,new Pages(false),"center-a",null,default)));
  Assert.Equal(503,Status(await CenterSiteNavigationEndpoint.History(
   Actor(),Empty,yes,pageYes,"center-a",null,default)));
 }
}
