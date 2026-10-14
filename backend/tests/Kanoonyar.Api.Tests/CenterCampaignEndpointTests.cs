using System.Security.Claims;
using Kanoonyar.Api.Programs;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Kanoonyar.Api.Tests;
public sealed class CenterCampaignEndpointTests
{
 private sealed class Yes:ICenterCampaignAuthorization
 {public ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal a,string c,Guid id,string op,CancellationToken ct)=>ValueTask.FromResult(true);}
 private sealed class No:ICenterCampaignAuthorization
 {public ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal a,string c,Guid id,string op,CancellationToken ct)=>ValueTask.FromResult(false);}
 private static ClaimsPrincipal Actor(bool subject=true)=>new(new ClaimsIdentity(
  subject?[new Claim("sub","synthetic"),new Claim("role","admin")]:[new Claim("role","admin")],"synthetic"));
 private static IConfiguration Empty=>new ConfigurationBuilder().Build();
 private static int? Code(IResult r)=>(r as IStatusCodeHttpResult)?.StatusCode;
 private static AppendCenterCampaignRequest Valid()=>new(Guid.NewGuid(),0,"campaign",Guid.NewGuid(),1,
   "Campaign","Goal","Broad audience",[new("activity_one","Planned workshop")]);
 [Fact] public async Task Missing_permission_and_invalid_planned_activity_never_write()
 {
  var a=Actor();var x=Valid();
  Assert.Equal(401,Code(await CenterCampaignEndpoint.Append(new ClaimsPrincipal(),Empty,new Yes(),"a",x,default)));
  Assert.Equal(403,Code(await CenterCampaignEndpoint.Append(Actor(false),Empty,new Yes(),"a",x,default)));
  Assert.Equal(400,Code(await CenterCampaignEndpoint.Append(a,Empty,new Yes(),"a",x with{PinnedPlanRevision=0},default)));
  Assert.Equal(400,Code(await CenterCampaignEndpoint.Append(a,Empty,new Yes(),"a",x with{Slug="../bad"},default)));
  Assert.Equal(400,Code(await CenterCampaignEndpoint.Append(a,Empty,new Yes(),"a",x with{PlannedActivities=[]},default)));
  Assert.Equal(400,Code(await CenterCampaignEndpoint.Append(a,Empty,new Yes(),"a",x with{PlannedActivities=[new("same","X"),new("same","Y")]},default)));
  Assert.Equal(403,Code(await CenterCampaignEndpoint.Append(a,Empty,new No(),"a",x,default)));
  Assert.Equal(503,Code(await CenterCampaignEndpoint.Append(a,Empty,new Yes(),"a",x,default)));
  Assert.Equal(403,Code(await CenterCampaignEndpoint.List(a,Empty,new No(),"a",null,default)));
  Assert.Equal(503,Code(await CenterCampaignEndpoint.List(a,Empty,new Yes(),"a",null,default)));
  Assert.Equal(400,Code(await CenterCampaignEndpoint.History(a,Empty,new Yes(),"a",x.CampaignId,0,default)));
  Assert.Equal(403,Code(await CenterCampaignEndpoint.History(a,Empty,new No(),"a",x.CampaignId,null,default)));
 }
}
