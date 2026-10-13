using System.Security.Claims;
using Kanoonyar.Api.Programs;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Xunit;
namespace Kanoonyar.Api.Tests;
public sealed class CenterPrivateCalendarTests
{
 private sealed class EventGrant(bool allow):ICenterEventPlanAuthorization
 {
  public ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal actor,string center,Guid id,
   string op,CancellationToken ct)=>ValueTask.FromResult(allow&&
    op=="CENTER_PRIVATE_CALENDAR_READ"&&id==Guid.Empty);
 }
 private sealed class ClassGrant(bool allow):ICenterSourceCatalogReadAuthorization
 {
  public ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal actor,string center,CancellationToken ct)=>
   ValueTask.FromResult(allow);
 }
 private static ClaimsPrincipal Actor(bool sub=true)=>new(new ClaimsIdentity(
  sub?[new Claim("sub","actor-a")]:[new Claim("role","admin")],"test"));
 private static int? Status(IResult r)=>(r as IStatusCodeHttpResult)?.StatusCode;
 private static readonly DateTimeOffset Start=DateTimeOffset.Parse("2026-11-01T00:00:00Z");
 private static readonly DateTimeOffset End=DateTimeOffset.Parse("2026-12-01T00:00:00Z");
 private static Task<IResult> Get(ClaimsPrincipal actor,bool eventGrant=true,bool classGrant=true,
   DateTimeOffset? from=null,DateTimeOffset? until=null,DateTimeOffset? before=null,
   string? kind=null,Guid? beforeId=null)=>
  CenterPrivateCalendarEndpoint.Get(actor,new ConfigurationBuilder().Build(),
   new EventGrant(eventGrant),new ClassGrant(classGrant),"center-a",
   from??Start,until??End,before,kind,beforeId,default);
 [Fact] public async Task Input_authorization_and_cursor_require_all_components_and_strict_utc()
 {
  Assert.Equal(401,Status(await Get(new ClaimsPrincipal())));
  Assert.Equal(403,Status(await Get(Actor(false))));
  Assert.Equal(400,Status(await Get(Actor(),until:Start)));
  Assert.Equal(400,Status(await Get(Actor(),from:End)));
  Assert.Equal(400,Status(await Get(Actor(),before:Start)));
  Assert.Equal(400,Status(await Get(Actor(),before:Start,kind:"CENTER_EVENT")));
  Assert.Equal(400,Status(await Get(Actor(),before:Start,kind:"APPROVED",
   beforeId:Guid.NewGuid())));
  Assert.Equal(400,Status(await Get(Actor(),before:End,kind:"CENTER_EVENT",
   beforeId:Guid.NewGuid())));
  Assert.Equal(403,Status(await Get(Actor(),eventGrant:false)));
  Assert.Equal(403,Status(await Get(Actor(),classGrant:false)));
  Assert.Equal(503,Status(await Get(Actor())));
 }
}
