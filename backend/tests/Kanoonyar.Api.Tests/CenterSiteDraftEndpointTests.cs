using System.Security.Claims;
using Kanoonyar.Api.Programs;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Xunit;
namespace Kanoonyar.Api.Tests;
public sealed class CenterSiteDraftEndpointTests
{
 private static IConfiguration Empty=>new ConfigurationBuilder().Build();
 private static ClaimsPrincipal Actor(params Claim[] claims)=>new(new ClaimsIdentity(claims,"fixture"));
 private static int? Code(IResult r)=>(r as IStatusCodeHttpResult)?.StatusCode;
 private sealed class Permit:ICenterSiteDraftAuthorization
 {
  public ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal user,string center,Guid id,string op,CancellationToken ct)=>ValueTask.FromResult(true);
 }
 private sealed class Broken:ICenterSiteDraftAuthorization
 {
  public ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal user,string center,Guid id,string op,CancellationToken ct)=>throw new InvalidOperationException("grant source unavailable");
 }
 [Fact] public async Task Drafts_require_independent_scope_and_safe_content()
 {
  var admin=Actor(new Claim("sub","actor-a"),new Claim("role","admin"));
  var id=Guid.NewGuid();
  var data=new AppendCenterSiteDraftRequest(id,0,"center-home","Home","Some plain text");
  Assert.Equal(401,Code(await CenterSiteDraftEndpoint.Append(new ClaimsPrincipal(),Empty,new Permit(),"a",data,default)));
  Assert.Equal(403,Code(await CenterSiteDraftEndpoint.Append(Actor(new Claim("role","admin")),Empty,new Permit(),"a",data,default)));
  Assert.Equal(400,Code(await CenterSiteDraftEndpoint.Append(admin,Empty,new Permit(),"a",data with{Slug="../admin"},default)));
  Assert.Equal(400,Code(await CenterSiteDraftEndpoint.Append(admin,Empty,new Permit(),"a",data with{ExpectedRevision=-1},default)));
  Assert.Equal(400,Code(await CenterSiteDraftEndpoint.Append(admin,Empty,new Permit(),"a",data with{Title=""},default)));
  Assert.Equal(400,Code(await CenterSiteDraftEndpoint.Append(admin,Empty,new Permit(),"a",data with{BodyText=new string('a',20001)},default)));
  Assert.Equal(403,Code(await CenterSiteDraftEndpoint.Append(admin,Empty,new UnapprovedCenterSiteDraftAuthorization(),"a",data,default)));
  Assert.Equal(403,Code(await CenterSiteDraftEndpoint.Append(admin,Empty,new Broken(),"a",data,default)));
  Assert.Equal(503,Code(await CenterSiteDraftEndpoint.Append(admin,Empty,new Permit(),"a",data,default)));
  Assert.Equal(403,Code(await CenterSiteDraftEndpoint.List(admin,Empty,new UnapprovedCenterSiteDraftAuthorization(),"a",null,default)));
  Assert.Equal(503,Code(await CenterSiteDraftEndpoint.List(admin,Empty,new Permit(),"a",null,default)));
  Assert.Equal(400,Code(await CenterSiteDraftEndpoint.History(admin,Empty,new Permit(),"a",id,0,default)));
  Assert.Equal(403,Code(await CenterSiteDraftEndpoint.History(admin,Empty,new Broken(),"a",id,null,default)));
  Assert.Equal(503,Code(await CenterSiteDraftEndpoint.History(admin,Empty,new Permit(),"a",id,null,default)));
 }
}
