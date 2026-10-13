using System.Security.Claims;
using Kanoonyar.Api.Programs;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Xunit;
namespace Kanoonyar.Api.Tests;
public sealed class CenterCampaignFollowupTests
{
 private static IConfiguration Empty=>new ConfigurationBuilder().Build();
 private static ClaimsPrincipal Actor(bool sub=true)=>new(new ClaimsIdentity(sub?
  [new Claim("sub","synthetic"),new Claim("role","admin")]:[new Claim("role","admin")],"synthetic"));
 private static int? Code(IResult r)=>(r as IStatusCodeHttpResult)?.StatusCode;
 private sealed class Allow:ICenterCampaignReportLinkAuthorization
 {public ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal p,string c,Guid id,string op,CancellationToken ct)=>ValueTask.FromResult(true);}
 private sealed class ReportAllow:ICenterActivityReportReadAuthorization
 {public ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal p,string c,CancellationToken ct)=>ValueTask.FromResult(true);}
 [Fact] public async Task Independent_grants_and_typed_followup_bounds_fail_closed()
 {
  var c=Guid.NewGuid();var link=Guid.NewGuid();var note=Guid.NewGuid();
  var question=new AppendCampaignFollowupRequest(Guid.NewGuid(),"REQUEST",null,"Clarify the source");
  Task<IResult> Add(ClaimsPrincipal actor,AppendCampaignFollowupRequest b,
   ICenterCampaignReportLinkAuthorization? grant=null,ICenterActivityReportReadAuthorization? report=null)=>
   CenterCampaignFollowupEndpoint.Append(actor,Empty,grant??new Allow(),report??new ReportAllow(),
    "center-a",c,link,note,b,default);
  Assert.Equal(401,Code(await Add(new ClaimsPrincipal(),question)));
  Assert.Equal(403,Code(await Add(Actor(false),question)));
  Assert.Equal(400,Code(await Add(Actor(),question with{Content=""})));
  Assert.Equal(400,Code(await Add(Actor(),question with{RequestId=Guid.NewGuid()})));
  Assert.Equal(400,Code(await Add(Actor(),question with{EntryKind="APPROVED"})));
  Assert.Equal(400,Code(await Add(Actor(),question with{EntryKind="RESPONSE"})));
  Assert.Equal(400,Code(await Add(Actor(),question with{Content=new string('x',1001)})));
  Assert.Equal(403,Code(await Add(Actor(),question,new UnapprovedCenterCampaignReportLinkAuthorization())));
  Assert.Equal(403,Code(await Add(Actor(),question,null,new UnapprovedCenterActivityReportReadAuthorization())));
  Assert.Equal(503,Code(await Add(Actor(),question)));
  Assert.Equal(400,Code(await CenterCampaignFollowupEndpoint.List(Actor(),Empty,
    new Allow(),new ReportAllow(),"center-a",c,link,note,"VERIFIED",null,null,default)));
  Assert.Equal(400,Code(await CenterCampaignFollowupEndpoint.List(Actor(),Empty,
    new Allow(),new ReportAllow(),"center-a",c,link,note,"RESPONSE",null,null,default)));
  Assert.Equal(403,Code(await CenterCampaignFollowupEndpoint.List(Actor(),Empty,
    new Allow(),new UnapprovedCenterActivityReportReadAuthorization(),
    "center-a",c,link,note,"REQUEST",null,null,default)));
  Assert.Equal(503,Code(await CenterCampaignFollowupEndpoint.List(Actor(),Empty,
    new Allow(),new ReportAllow(),"center-a",c,link,note,"REQUEST",null,null,default)));
 }
}
