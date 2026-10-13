using System.Security.Claims;
using Kanoonyar.Api.Programs;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Kanoonyar.Api.Tests;
public sealed class CenterEventPlanEndpointTests
{
 private sealed class Grant(bool allowed):ICenterEventPlanAuthorization
 {
  public ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal actor,string centerId,
   Guid eventId,string operation,CancellationToken ct)=>ValueTask.FromResult(allowed);
 }
 private static ClaimsPrincipal Actor(bool sub=true)=>new(new ClaimsIdentity(
  sub?[new Claim("sub","actor-a"),new Claim("role","admin")]:[new Claim("role","admin")],"test"));
 private static IConfiguration Empty=>new ConfigurationBuilder().Build();
 private static int? Status(IResult r)=>(r as IStatusCodeHttpResult)?.StatusCode;
 private static AppendCenterEventPlanRequest Valid()=>new(Guid.NewGuid(),0,Guid.NewGuid(),1,
  "Community event","Culture","Community hall",DateTimeOffset.Parse("2026-11-10T10:00:00Z"));
 [Fact] public async Task Independent_permissions_validation_and_safe_storage_fail_closed()
 {
  var yes=new Grant(true);var no=new Grant(false);var e=Valid();
  Assert.Equal(401,Status(await CenterEventPlanEndpoint.Append(new ClaimsPrincipal(),Empty,yes,"a",e,default)));
  Assert.Equal(403,Status(await CenterEventPlanEndpoint.Append(Actor(false),Empty,yes,"a",e,default)));
  Assert.Equal(400,Status(await CenterEventPlanEndpoint.Append(Actor(),Empty,yes,"a",e with{PinnedPlanRevision=0},default)));
  Assert.Equal(400,Status(await CenterEventPlanEndpoint.Append(Actor(),Empty,yes,"a",e with{ScheduledAt=default},default)));
  Assert.Equal(400,Status(await CenterEventPlanEndpoint.Append(Actor(),Empty,yes,"a",e with{LocationDescription=""},default)));
  Assert.Equal(400,Status(await CenterEventPlanEndpoint.Append(Actor(),Empty,yes,"a",e with{ScheduledAt=DateTimeOffset.Parse("2026-11-10T12:00:00+02:00")},default)));
  Assert.Equal(403,Status(await CenterEventPlanEndpoint.Append(Actor(),Empty,no,"a",e,default)));
  Assert.Equal(503,Status(await CenterEventPlanEndpoint.Append(Actor(),Empty,yes,"a",e,default)));
  Assert.Equal(403,Status(await CenterEventPlanEndpoint.List(Actor(),Empty,no,"a",null,default)));
  Assert.Equal(503,Status(await CenterEventPlanEndpoint.List(Actor(),Empty,yes,"a",null,default)));
  Assert.Equal(400,Status(await CenterEventPlanEndpoint.List(Actor(),Empty,yes,"a",Guid.Empty,default)));
  Assert.Equal(400,Status(await CenterEventPlanEndpoint.History(Actor(),Empty,yes,"a",e.EventId,0,default)));
  Assert.Equal(403,Status(await CenterEventPlanEndpoint.History(Actor(),Empty,no,"a",e.EventId,null,default)));
 }
}
