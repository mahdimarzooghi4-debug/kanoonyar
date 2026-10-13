using System.Security.Claims;
using Kanoonyar.Api.Programs;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Xunit;
namespace Kanoonyar.Api.Tests;
public sealed class CenterCampaignEvidenceRegisterTests
{
 private static IConfiguration Empty=>new ConfigurationBuilder().Build();
 private static ClaimsPrincipal Actor(bool sub=true)=>new(new ClaimsIdentity(
  sub?[new Claim("sub","synthetic"),new Claim("role","admin")]:[new Claim("role","admin")],"test"));
 private static int? Code(IResult r)=>(r as IStatusCodeHttpResult)?.StatusCode;
 private sealed class Grant:ICenterCampaignReportLinkAuthorization
 {
  public string? Deny {get;init;}
  public ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal a,string c,Guid id,string op,CancellationToken ct)
   =>ValueTask.FromResult(op!=Deny);
 }
 private sealed class Source:ICenterActivityReportReadAuthorization
 {public ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal a,string c,CancellationToken ct)=>ValueTask.FromResult(true);}
 private static Task<IResult> Get(ClaimsPrincipal actor,ICenterCampaignReportLinkAuthorization g,
   ICenterActivityReportReadAuthorization? source=null,Guid? cursor=null,
   string? sourceKind=null,string? activityKey=null)=>
   CenterCampaignEvidenceRegisterEndpoint.Get(actor,Empty,g,source??new Source(),
    "center-a",Guid.NewGuid(),cursor,sourceKind,activityKey,default);
 [Fact] public async Task Register_requires_independent_rights_and_valid_source_filters()
 {
  Assert.Equal(401,Code(await Get(new ClaimsPrincipal(),new Grant())));
  Assert.Equal(403,Code(await Get(Actor(false),new Grant())));
  foreach(var op in new[]{"CAMPAIGN_EVIDENCE_REGISTER_READ","CAMPAIGN_SOURCE_REPORT_READ",
   "CAMPAIGN_REPORT_INSPECTION_READ","CAMPAIGN_INSPECTION_FOLLOWUP_READ"})
   Assert.Equal(403,Code(await Get(Actor(),new Grant{Deny=op})));
  Assert.Equal(403,Code(await Get(Actor(),new Grant(),
   new UnapprovedCenterActivityReportReadAuthorization())));
  Assert.Equal(400,Code(await Get(Actor(),new Grant(),cursor:Guid.Empty)));
  Assert.Equal(400,Code(await Get(Actor(),new Grant(),sourceKind:"VERIFIED")));
  Assert.Equal(400,Code(await Get(Actor(),new Grant(),activityKey:"../other")));
  Assert.Equal(503,Code(await Get(Actor(),new Grant())));
 }
}
