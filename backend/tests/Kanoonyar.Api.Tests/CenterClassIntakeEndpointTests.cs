using System.Security.Claims;
using Kanoonyar.Api.Programs;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Xunit;
namespace Kanoonyar.Api.Tests;
public sealed class CenterClassIntakeEndpointTests
{
 private static IConfiguration Empty=>new ConfigurationBuilder().Build();
 private static ClaimsPrincipal User(params Claim[] claims)=>new(new ClaimsIdentity(claims,"fixture"));
 private static int? Status(IResult r)=>(r as IStatusCodeHttpResult)?.StatusCode;
 private sealed class Grant:ICenterClassIntakeAuthorization
 {
  public ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal a,string c,Guid cls,string op,CancellationToken ct)=>ValueTask.FromResult(true);
 }
 private sealed class Broken:ICenterClassIntakeAuthorization
 {
  public ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal a,string c,Guid cls,string op,CancellationToken ct)=>throw new InvalidOperationException("grant offline");
 }
 private static Guid Id=>Guid.NewGuid();
 [Fact] public async Task Authentication_validation_and_operation_grants_fail_closed()
 {
  var admin=User(new Claim("sub","fixture"),new Claim("role","superadmin"));
  var unapproved=new UnapprovedCenterClassIntakeAuthorization();
  Assert.Equal(401,Status(await CenterClassIntakeEndpoint.SetCapacity(new ClaimsPrincipal(),Empty,unapproved,"a",Id,new(0,3),default)));
  Assert.Equal(403,Status(await CenterClassIntakeEndpoint.SetCapacity(User(new Claim("role","admin")),Empty,unapproved,"a",Id,new(0,3),default)));
  Assert.Equal(400,Status(await CenterClassIntakeEndpoint.SetCapacity(admin,Empty,new Grant(),"a",Id,new(-1,3),default)));
  Assert.Equal(400,Status(await CenterClassIntakeEndpoint.SetCapacity(admin,Empty,new Grant(),"a",Id,new(0,0),default)));
  Assert.Equal(400,Status(await CenterClassIntakeEndpoint.SetCapacity(admin,Empty,new Grant(),"a",Guid.Empty,new(0,3),default)));
  Assert.Equal(403,Status(await CenterClassIntakeEndpoint.SetCapacity(admin,Empty,unapproved,"a",Id,new(0,3),default)));
  Assert.Equal(403,Status(await CenterClassIntakeEndpoint.SetCapacity(admin,Empty,new Broken(),"a",Id,new(0,3),default)));
  Assert.Equal(503,Status(await CenterClassIntakeEndpoint.SetCapacity(admin,Empty,new Grant(),"a",Id,new(0,3),default)));
  Assert.Equal(400,Status(await CenterClassIntakeEndpoint.RequestRegistration(admin,Empty,new Grant(),"a",Id,new(Guid.Empty,Id),default)));
  Assert.Equal(400,Status(await CenterClassIntakeEndpoint.RequestRegistration(admin,Empty,new Grant(),"a",Id,new(Id,Guid.Empty),default)));
  Assert.Equal(403,Status(await CenterClassIntakeEndpoint.RequestRegistration(admin,Empty,unapproved,"a",Id,new(Id,Id),default)));
  Assert.Equal(503,Status(await CenterClassIntakeEndpoint.RequestRegistration(admin,Empty,new Grant(),"a",Id,new(Id,Id),default)));
  Assert.Equal(400,Status(await CenterClassIntakeEndpoint.Read(admin,Empty,new Grant(),"a",Id,Guid.Empty,default)));
  Assert.Equal(403,Status(await CenterClassIntakeEndpoint.Read(admin,Empty,unapproved,"a",Id,null,default)));
  Assert.Equal(503,Status(await CenterClassIntakeEndpoint.Read(admin,Empty,new Grant(),"a",Id,null,default)));
 }
}
