using System.Security.Claims;
using Kanoonyar.Api.Programs;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Xunit;
namespace Kanoonyar.Api.Tests;
public sealed class CenterSiteDesignEndpointTests
{
 private static IConfiguration Missing=>new ConfigurationBuilder().Build();
 private static ClaimsPrincipal Actor(params Claim[] c)=>new(new ClaimsIdentity(c,"test"));
 private static int? Status(IResult r)=>(r as IStatusCodeHttpResult)?.StatusCode;
 private sealed class Permit:ICenterSiteDesignAuthorization
 {public ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal a,string c,string op,CancellationToken ct)=>ValueTask.FromResult(true);}
 private sealed class Broken:ICenterSiteDesignAuthorization
 {public ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal a,string c,string op,CancellationToken ct)=>throw new Exception("offline");}
 [Fact] public async Task No_implicit_identity_design_or_operation_grants()
 {
  var user=Actor(new Claim("sub","synthetic"),new Claim("role","admin"));
  var command=new AppendCenterSiteDesignRequest(0,"HERO","OCEAN","SYSTEM","Center","",true,false);
  var denied=new UnapprovedCenterSiteDesignAuthorization();
  Assert.Equal(401,Status(await CenterSiteDesignEndpoint.Append(new ClaimsPrincipal(),Missing,denied,"a",command,default)));
  Assert.Equal(403,Status(await CenterSiteDesignEndpoint.Append(Actor(new Claim("role","admin")),Missing,denied,"a",command,default)));
  Assert.Equal(400,Status(await CenterSiteDesignEndpoint.Append(user,Missing,new Permit(),"a",command with{LayoutKey="SCRIPT"},default)));
  Assert.Equal(400,Status(await CenterSiteDesignEndpoint.Append(user,Missing,new Permit(),"a",command with{PaletteKey="#fff;url(evil)"},default)));
  Assert.Equal(400,Status(await CenterSiteDesignEndpoint.Append(user,Missing,new Permit(),"a",command with{ExpectedRevision=-1},default)));
  Assert.Equal(400,Status(await CenterSiteDesignEndpoint.Append(user,Missing,new Permit(),"a",command with{HeaderText=""},default)));
  Assert.Equal(403,Status(await CenterSiteDesignEndpoint.Append(user,Missing,denied,"a",command,default)));
  Assert.Equal(403,Status(await CenterSiteDesignEndpoint.Append(user,Missing,new Broken(),"a",command,default)));
  Assert.Equal(503,Status(await CenterSiteDesignEndpoint.Append(user,Missing,new Permit(),"a",command,default)));
  Assert.Equal(403,Status(await CenterSiteDesignEndpoint.Latest(user,Missing,denied,"a",default)));
  Assert.Equal(503,Status(await CenterSiteDesignEndpoint.Latest(user,Missing,new Permit(),"a",default)));
  Assert.Equal(400,Status(await CenterSiteDesignEndpoint.History(user,Missing,new Permit(),"a",0,default)));
  Assert.Equal(403,Status(await CenterSiteDesignEndpoint.History(user,Missing,new Broken(),"a",null,default)));
  Assert.Equal(503,Status(await CenterSiteDesignEndpoint.History(user,Missing,new Permit(),"a",null,default)));
 }
}
