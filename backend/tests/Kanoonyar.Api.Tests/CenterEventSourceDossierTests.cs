using System.Security.Claims;
using Kanoonyar.Api.Programs;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Kanoonyar.Api.Tests;
public sealed class CenterEventSourceDossierTests
{
 private sealed class EventGrant(bool allowed):ICenterEventPlanAuthorization
 {
  public ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal a,string c,Guid id,string operation,CancellationToken ct)=>
   ValueTask.FromResult(allowed && operation=="EVENT_SOURCE_DOSSIER_READ");
 }
 private sealed class ReportGrant(bool allowed):ICenterActivityReportReadAuthorization
 {
  public ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal a,string c,CancellationToken ct)=>
   ValueTask.FromResult(allowed);
 }
 private static ClaimsPrincipal Actor(bool subject=true)=>new(new ClaimsIdentity(
  subject?[new Claim("sub","actor-a"),new Claim("role","admin")]:[new Claim("role","admin")],"test"));
 private static int? Status(IResult r)=>(r as IStatusCodeHttpResult)?.StatusCode;
 private static IConfiguration Empty=>new ConfigurationBuilder().Build();
 [Fact] public async Task Authentication_cursor_and_each_independent_grant_are_fail_closed()
 {
  var id=Guid.NewGuid();var eventYes=new EventGrant(true);var reportYes=new ReportGrant(true);
  Task<IResult> Get(ClaimsPrincipal a,ICenterEventPlanAuthorization e,
   ICenterActivityReportReadAuthorization r,long? before=null,Guid? eventId=null)=>
   CenterEventSourceDossierEndpoint.Get(a,Empty,e,r,"center-a",eventId??id,before,default);
  Assert.Equal(401,Status(await Get(new ClaimsPrincipal(),eventYes,reportYes)));
  Assert.Equal(403,Status(await Get(Actor(false),eventYes,reportYes)));
  Assert.Equal(400,Status(await Get(Actor(),eventYes,reportYes,0)));
  Assert.Equal(400,Status(await Get(Actor(),eventYes,reportYes,eventId:Guid.Empty)));
  Assert.Equal(403,Status(await Get(Actor(),new EventGrant(false),reportYes)));
  Assert.Equal(403,Status(await Get(Actor(),eventYes,new ReportGrant(false))));
  Assert.Equal(503,Status(await Get(Actor(),eventYes,reportYes)));
 }
}
