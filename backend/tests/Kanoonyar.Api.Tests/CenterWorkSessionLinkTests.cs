using System.Security.Claims;
using Kanoonyar.Api.Programs;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Kanoonyar.Api.Tests;
public sealed class CenterWorkSessionLinkTests
{
 private sealed class Link(bool allowed):IWorkCardSessionLinkAuthorization
 {
  public ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal a,string c,Guid w,string op,
   CancellationToken ct)=>ValueTask.FromResult(allowed);
 }
 private sealed class Work(bool allowed):ICenterWorkCardAuthorization
 {
  public ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal a,string c,string op,Guid w,
   CancellationToken ct)=>ValueTask.FromResult(allowed);
 }
 private sealed class Plan(bool allowed):ILocalPlanDraftReadAuthorization
 {
  public ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal a,string c,CancellationToken ct)
   =>ValueTask.FromResult(allowed);
 }
 private sealed class Session(bool allowed):ICenterSourceCatalogReadAuthorization
 {
  public ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal a,string c,CancellationToken ct)
   =>ValueTask.FromResult(allowed);
 }
 private static IConfiguration Empty=>new ConfigurationBuilder().Build();
 private static ClaimsPrincipal Actor(bool withSub=true)=>new(new ClaimsIdentity(
  withSub?[new Claim("sub","s41-test"),new Claim("role","superadmin")]:
   [new Claim("role","superadmin")],"synthetic"));
 private static int? Status(IResult r)=>(r as IStatusCodeHttpResult)?.StatusCode;
 [Fact] public async Task Link_requires_exact_canonical_sources_and_four_separate_rights()
 {
  var card=Guid.NewGuid();var session=Guid.NewGuid();
  Task<IResult> Post(ClaimsPrincipal? actor=null,Guid? id=null,bool l=true,bool w=true,
   bool p=true,bool s=true)=>CenterWorkSessionLinkEndpoint.Append(actor??Actor(),
    Empty,new Link(l),new Work(w),new Plan(p),new Session(s),"center-a",card,
    new AppendWorkSessionLinkRequest(id??session),default);
  Task<IResult> Read(ClaimsPrincipal? actor=null,Guid? cursor=null,bool l=true,
   bool w=true,bool p=true,bool s=true)=>CenterWorkSessionLinkEndpoint.List(
    actor??Actor(),Empty,new Link(l),new Work(w),new Plan(p),new Session(s),
    "center-a",card,cursor,default);
  Assert.Equal(401,Status(await Post(actor:new ClaimsPrincipal())));
  Assert.Equal(403,Status(await Post(actor:Actor(false))));
  Assert.Equal(400,Status(await Post(id:Guid.Empty)));
  Assert.Equal(403,Status(await Post(l:false)));
  Assert.Equal(403,Status(await Post(w:false)));
  Assert.Equal(403,Status(await Post(p:false)));
  Assert.Equal(403,Status(await Post(s:false)));
  Assert.Equal(503,Status(await Post()));
  Assert.Equal(401,Status(await Read(actor:new ClaimsPrincipal())));
  Assert.Equal(403,Status(await Read(actor:Actor(false))));
  Assert.Equal(400,Status(await Read(cursor:Guid.Empty)));
  Assert.Equal(403,Status(await Read(l:false)));
  Assert.Equal(403,Status(await Read(w:false)));
  Assert.Equal(403,Status(await Read(p:false)));
  Assert.Equal(403,Status(await Read(s:false)));
  Assert.Equal(503,Status(await Read()));
 }
}
