using System.Security.Claims;
using Kanoonyar.Api.Programs;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Kanoonyar.Api.Tests;
public sealed class CenterCampaignReviewWorkspaceTests
{
 private static IConfiguration Empty=>new ConfigurationBuilder().Build();
 private static ClaimsPrincipal Actor(bool sub=true)=>new(new ClaimsIdentity(sub?
  [new Claim("sub","test"),new Claim("role","admin")]:[new Claim("role","admin")],"test"));
 private static int? Code(IResult r)=>(r as IStatusCodeHttpResult)?.StatusCode;
 private sealed class Grant:ICenterCampaignReportLinkAuthorization
 {
  public string? Deny {get;init;}
  public ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal a,string c,Guid id,string op,CancellationToken ct)=>
   ValueTask.FromResult(op!=Deny);
 }
 private sealed class Source:ICenterActivityReportReadAuthorization
 {public ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal a,string c,CancellationToken ct)=>ValueTask.FromResult(true);}
 private static Task<IResult> Get(ClaimsPrincipal actor,ICenterCampaignReportLinkAuthorization g,
   ICenterActivityReportReadAuthorization? source=null,DateTimeOffset? at=null,
   string? kind=null,Guid? id=null)=>
   CenterCampaignReviewWorkspaceEndpoint.Get(actor,Empty,g,source??new Source(),
    "center-a",Guid.NewGuid(),Guid.NewGuid(),at,kind,id,default);
 [Fact] public async Task All_independent_read_grants_and_a_complete_cursor_are_required()
 {
  Assert.Equal(401,Code(await Get(new ClaimsPrincipal(),new Grant())));
  Assert.Equal(403,Code(await Get(Actor(false),new Grant())));
  foreach(var op in new[]{"CAMPAIGN_SOURCE_REPORT_READ","CAMPAIGN_REPORT_INSPECTION_READ",
   "CAMPAIGN_INSPECTION_FOLLOWUP_READ"})
   Assert.Equal(403,Code(await Get(Actor(),new Grant{Deny=op})));
  Assert.Equal(403,Code(await Get(Actor(),new Grant(),
   new UnapprovedCenterActivityReportReadAuthorization())));
  Assert.Equal(400,Code(await Get(Actor(),new Grant(),at:DateTimeOffset.UtcNow)));
  Assert.Equal(400,Code(await Get(Actor(),new Grant(),kind:"NOTE")));
  Assert.Equal(400,Code(await Get(Actor(),new Grant(),at:DateTimeOffset.UtcNow,
    kind:"APPROVED",id:Guid.NewGuid())));
  Assert.Equal(400,Code(await Get(Actor(),new Grant(),at:DateTimeOffset.UtcNow,
    kind:"NOTE",id:Guid.Empty)));
  Assert.Equal(503,Code(await Get(Actor(),new Grant())));
 }
}
