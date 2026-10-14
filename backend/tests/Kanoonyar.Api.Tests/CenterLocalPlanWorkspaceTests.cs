using System.Security.Claims;
using Kanoonyar.Api.Programs;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Kanoonyar.Api.Tests;
public sealed class CenterLocalPlanWorkspaceTests
{
 private sealed class Plan(bool yes=true):ILocalPlanDraftReadAuthorization
 {
  public ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal a,string c,CancellationToken ct)
   =>ValueTask.FromResult(yes);
 }
 private sealed class Card(bool yes=true):ICenterWorkCardAuthorization
 {
  public ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal a,string c,string op,Guid id,CancellationToken ct)
   =>ValueTask.FromResult(yes);
 }
 private sealed class Need(bool yes=true):ICenterWorkResourceNeedAuthorization
 {
  public ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal a,string c,Guid id,string op,CancellationToken ct)
   =>ValueTask.FromResult(yes);
 }
 private static ClaimsPrincipal Actor(bool sub=true)=>new(new ClaimsIdentity(
  sub?[new Claim("sub","s40-test"),new Claim("role","superadmin")]:
  [new Claim("role","superadmin")],"signed"));
 private static int? Status(IResult r)=>(r as IStatusCodeHttpResult)?.StatusCode;
 private static IConfiguration Empty=>new ConfigurationBuilder().Build();
 [Fact] public async Task Plan_version_cursor_and_three_independent_permissions_fail_closed()
 {
  var draft=Guid.NewGuid();
  Task<IResult> Get(ClaimsPrincipal? actor=null,Guid? d=null,long ver=1,Guid? before=null,
   bool plan=true,bool cards=true,bool needs=true)=>
   CenterLocalPlanWorkspaceEndpoint.Get(actor??Actor(),Empty,new Plan(plan),
    new Card(cards),new Need(needs),"center-a",d??draft,ver,before,default);
  Assert.Equal(401,Status(await Get(actor:new ClaimsPrincipal())));
  Assert.Equal(403,Status(await Get(actor:Actor(false))));
  Assert.Equal(400,Status(await Get(d:Guid.Empty)));
  Assert.Equal(400,Status(await Get(ver:0)));
  Assert.Equal(400,Status(await Get(ver:-1)));
  Assert.Equal(400,Status(await Get(before:Guid.Empty)));
  Assert.Equal(403,Status(await Get(plan:false)));
  Assert.Equal(403,Status(await Get(cards:false)));
  Assert.Equal(403,Status(await Get(needs:false)));
  Assert.Equal(503,Status(await Get()));
 }
}
