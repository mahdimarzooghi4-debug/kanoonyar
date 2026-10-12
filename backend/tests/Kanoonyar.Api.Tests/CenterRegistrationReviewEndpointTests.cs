using System.Security.Claims;
using Kanoonyar.Api.Programs;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Xunit;
namespace Kanoonyar.Api.Tests;
public sealed class CenterRegistrationReviewEndpointTests
{
 private static IConfiguration Empty=>new ConfigurationBuilder().Build();
 private static ClaimsPrincipal User(params Claim[] claims)=>new(new ClaimsIdentity(claims,"synthetic"));
 private static int? Status(IResult r)=>(r as IStatusCodeHttpResult)?.StatusCode;
 private sealed class Grant:ICenterRegistrationReviewAuthorization
 {
  public ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal p,string c,Guid cls,Guid req,
      string op,CancellationToken ct)=>ValueTask.FromResult(true);
 }
 private sealed class Broken:ICenterRegistrationReviewAuthorization
 {
  public ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal p,string c,Guid cls,Guid req,
      string op,CancellationToken ct)=>throw new InvalidOperationException("authorization offline");
 }
 [Fact] public async Task Missing_identity_bad_topic_and_independent_grants_fail_closed()
 {
  var noGrant=new UnapprovedCenterRegistrationReviewAuthorization();
  var good=User(new Claim("sub","actor-a"),new Claim("role","admin"));
  var cls=Guid.NewGuid();var req=Guid.NewGuid();
  var value=new RaiseRegistrationReviewRequest(Guid.NewGuid(),"CONSENT_AUTHORITY");
  Assert.Equal(401,Status(await CenterRegistrationReviewEndpoint.Raise(new ClaimsPrincipal(),
      Empty,noGrant,"a",cls,req,value,default)));
  Assert.Equal(403,Status(await CenterRegistrationReviewEndpoint.Raise(
      User(new Claim("role","admin")),Empty,noGrant,"a",cls,req,value,default)));
  Assert.Equal(400,Status(await CenterRegistrationReviewEndpoint.Raise(
      good,Empty,new Grant(),"a",cls,req,value with {ReviewTopic="VERIFIED"},default)));
  Assert.Equal(400,Status(await CenterRegistrationReviewEndpoint.Raise(
      good,Empty,new Grant(),"a",cls,req,value with {ReviewRequestId=Guid.Empty},default)));
  Assert.Equal(403,Status(await CenterRegistrationReviewEndpoint.Raise(
      good,Empty,noGrant,"a",cls,req,value,default)));
  Assert.Equal(403,Status(await CenterRegistrationReviewEndpoint.Raise(
      good,Empty,new Broken(),"a",cls,req,value,default)));
  Assert.Equal(503,Status(await CenterRegistrationReviewEndpoint.Raise(
      good,Empty,new Grant(),"a",cls,req,value,default)));
  Assert.Equal(403,Status(await CenterRegistrationReviewEndpoint.List(
      good,Empty,noGrant,"a",cls,req,default)));
  Assert.Equal(403,Status(await CenterRegistrationReviewEndpoint.List(
      good,Empty,new Broken(),"a",cls,req,default)));
  Assert.Equal(503,Status(await CenterRegistrationReviewEndpoint.List(
      good,Empty,new Grant(),"a",cls,req,default)));
 }
}
