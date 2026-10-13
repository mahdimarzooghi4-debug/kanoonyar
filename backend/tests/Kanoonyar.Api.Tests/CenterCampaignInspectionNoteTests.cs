using System.Security.Claims;
using Kanoonyar.Api.Programs;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Kanoonyar.Api.Tests;
public sealed class CenterCampaignInspectionNoteTests
{
 private static IConfiguration Config=>new ConfigurationBuilder().Build();
 private static ClaimsPrincipal Actor(bool subject=true)=>new(new ClaimsIdentity(
  subject?[new Claim("sub","synthetic"),new Claim("role","admin")]:[new Claim("role","admin")],"test"));
 private static int? Status(IResult x)=>(x as IStatusCodeHttpResult)?.StatusCode;
 private sealed class Yes:ICenterCampaignReportLinkAuthorization
 {public ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal a,string c,Guid id,string op,CancellationToken ct)=>ValueTask.FromResult(true);}
 private sealed class ReportYes:ICenterActivityReportReadAuthorization
 {public ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal a,string c,CancellationToken ct)=>ValueTask.FromResult(true);}
 [Fact] public async Task Notes_are_never_approvals_and_require_both_independent_grants()
 {
  var a=Actor();var c=Guid.NewGuid();var link=Guid.NewGuid();
  var valid=new AppendCampaignInspectionNoteRequest(Guid.NewGuid(),"Observation only");
  Assert.Equal(401,Status(await CenterCampaignInspectionNotesEndpoint.Append(new ClaimsPrincipal(),Config,
    new Yes(),new ReportYes(),"a",c,link,valid,default)));
  Assert.Equal(403,Status(await CenterCampaignInspectionNotesEndpoint.Append(Actor(false),Config,
    new Yes(),new ReportYes(),"a",c,link,valid,default)));
  Assert.Equal(400,Status(await CenterCampaignInspectionNotesEndpoint.Append(a,Config,
    new Yes(),new ReportYes(),"a",c,link,valid with{NoteText=""},default)));
  Assert.Equal(400,Status(await CenterCampaignInspectionNotesEndpoint.Append(a,Config,
    new Yes(),new ReportYes(),"a",c,link,valid with{NoteText=new string('a',1001)},default)));
  Assert.Equal(403,Status(await CenterCampaignInspectionNotesEndpoint.Append(a,Config,
    new UnapprovedCenterCampaignReportLinkAuthorization(),new ReportYes(),"a",c,link,valid,default)));
  Assert.Equal(403,Status(await CenterCampaignInspectionNotesEndpoint.Append(a,Config,
    new Yes(),new UnapprovedCenterActivityReportReadAuthorization(),"a",c,link,valid,default)));
  Assert.Equal(503,Status(await CenterCampaignInspectionNotesEndpoint.Append(a,Config,
    new Yes(),new ReportYes(),"a",c,link,valid,default)));
  Assert.Equal(403,Status(await CenterCampaignInspectionNotesEndpoint.List(a,Config,
    new Yes(),new UnapprovedCenterActivityReportReadAuthorization(),"a",c,link,null,default)));
  Assert.Equal(400,Status(await CenterCampaignInspectionNotesEndpoint.List(a,Config,
    new Yes(),new ReportYes(),"a",c,link,Guid.Empty,default)));
  Assert.Equal(503,Status(await CenterCampaignInspectionNotesEndpoint.List(a,Config,
    new Yes(),new ReportYes(),"a",c,link,null,default)));
 }
}
