using System.Security.Claims;
using Kanoonyar.Api.Programs;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Xunit;
namespace Kanoonyar.Api.Tests;
public sealed class CenterAssembledSitePreviewTests
{
 private sealed class Nav(bool v):ICenterSiteNavigationAuthorization{
  public ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal a,string c,string op,CancellationToken ct)
   =>ValueTask.FromResult(v);
 }
 private sealed class Pages(bool v):ICenterSiteDraftAuthorization{
  public ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal a,string c,Guid id,string op,CancellationToken ct)
   =>ValueTask.FromResult(v);
 }
 private sealed class Design(bool v):ICenterSiteDesignAuthorization{
  public ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal a,string c,string op,CancellationToken ct)
   =>ValueTask.FromResult(v);
 }
 private static ClaimsPrincipal Actor(bool sub=true)=>new(new ClaimsIdentity(sub?
  [new Claim("sub","s37-synthetic"),new Claim("role","superadmin")]:
  [new Claim("role","superadmin")],"test"));
 private static IConfiguration Empty=>new ConfigurationBuilder().Build();
 private static int? Status(IResult r)=>(r as IStatusCodeHttpResult)?.StatusCode;
 private static Task<IResult> Preview(ClaimsPrincipal? actor=null,bool nav=true,bool pages=true,
  bool design=true,long? nr=null,long? dr=null,int? after=null)=>
  CenterAssembledSitePreviewEndpoint.Read(actor??Actor(),Empty,new Nav(nav),new Pages(pages),
   new Design(design),"center-a",nr,dr,after,default);
 [Fact] public async Task Preview_requires_three_distinct_read_rights_and_exact_version_cursors()
 {
  Assert.Equal(401,Status(await Preview(actor:new ClaimsPrincipal())));
  Assert.Equal(403,Status(await Preview(actor:Actor(false))));
  Assert.Equal(400,Status(await Preview(nr:1)));
  Assert.Equal(400,Status(await Preview(dr:1)));
  Assert.Equal(400,Status(await Preview(nr:0,dr:1)));
  Assert.Equal(400,Status(await Preview(nr:1,dr:0)));
  Assert.Equal(400,Status(await Preview(after:1)));
  Assert.Equal(400,Status(await Preview(nr:1,dr:1,after:0)));
  Assert.Equal(403,Status(await Preview(nav:false)));
  Assert.Equal(403,Status(await Preview(pages:false)));
  Assert.Equal(403,Status(await Preview(design:false)));
  Assert.Equal(503,Status(await Preview()));
 }
}
