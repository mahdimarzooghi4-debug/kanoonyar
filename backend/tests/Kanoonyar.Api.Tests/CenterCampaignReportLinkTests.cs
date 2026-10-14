using System.Security.Claims;
using Kanoonyar.Api.Programs;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Kanoonyar.Api.Tests;
public sealed class CenterCampaignReportLinkTests
{
 private static IConfiguration Config=>new ConfigurationBuilder().Build();
 private static ClaimsPrincipal Actor(params Claim[] claims)=>new(new ClaimsIdentity(claims,"test"));
 private static int? Status(IResult x)=>(x as IStatusCodeHttpResult)?.StatusCode;
 private sealed class Yes:ICenterCampaignReportLinkAuthorization
 {public ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal a,string c,Guid id,string op,CancellationToken ct)=>ValueTask.FromResult(true);}
 private sealed class No:ICenterCampaignReportLinkAuthorization
 {public ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal a,string c,Guid id,string op,CancellationToken ct)=>ValueTask.FromResult(false);}
 private sealed class ReportYes:ICenterActivityReportReadAuthorization
 {public ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal a,string c,CancellationToken ct)=>ValueTask.FromResult(true);}
 private sealed class ReportNo:ICenterActivityReportReadAuthorization
 {public ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal a,string c,CancellationToken ct)=>ValueTask.FromResult(false);}
 [Fact] public async Task Both_grants_are_mandatory_and_unsafe_ids_fail_closed()
 {
  var actor=Actor(new Claim("sub","fixture"),new Claim("role","admin"));
  var campaign=Guid.NewGuid();
  var body=new LinkCampaignSourceReportRequest(Guid.NewGuid(),1,"workshop",Guid.NewGuid());
  Assert.Equal(401,Status(await CenterCampaignReportLinkEndpoint.Link(new ClaimsPrincipal(),Config,new Yes(),new ReportYes(),"a",campaign,body,default)));
  Assert.Equal(403,Status(await CenterCampaignReportLinkEndpoint.Link(Actor(new Claim("role","admin")),Config,new Yes(),new ReportYes(),"a",campaign,body,default)));
  Assert.Equal(400,Status(await CenterCampaignReportLinkEndpoint.Link(actor,Config,new Yes(),new ReportYes(),"a",campaign,body with{ActivityKey="../bad"},default)));
  Assert.Equal(400,Status(await CenterCampaignReportLinkEndpoint.Link(actor,Config,new Yes(),new ReportYes(),"a",campaign,body with{CampaignRevision=0},default)));
  Assert.Equal(403,Status(await CenterCampaignReportLinkEndpoint.Link(actor,Config,new Yes(),new ReportNo(),"a",campaign,body,default)));
  Assert.Equal(403,Status(await CenterCampaignReportLinkEndpoint.Link(actor,Config,new No(),new ReportYes(),"a",campaign,body,default)));
  Assert.Equal(503,Status(await CenterCampaignReportLinkEndpoint.Link(actor,Config,new Yes(),new ReportYes(),"a",campaign,body,default)));
  Assert.Equal(403,Status(await CenterCampaignReportLinkEndpoint.List(actor,Config,new Yes(),new ReportNo(),"a",campaign,null,default)));
  Assert.Equal(403,Status(await CenterCampaignReportLinkEndpoint.List(actor,Config,new No(),new ReportYes(),"a",campaign,null,default)));
  Assert.Equal(400,Status(await CenterCampaignReportLinkEndpoint.List(actor,Config,new Yes(),new ReportYes(),"a",campaign,Guid.Empty,default)));
  Assert.Equal(503,Status(await CenterCampaignReportLinkEndpoint.List(actor,Config,new Yes(),new ReportYes(),"a",campaign,null,default)));
 }
}
