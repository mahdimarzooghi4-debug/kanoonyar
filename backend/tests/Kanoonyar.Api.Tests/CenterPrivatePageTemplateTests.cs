using System.Security.Claims;
using Kanoonyar.Api.Programs;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Xunit;
namespace Kanoonyar.Api.Tests;
public sealed class CenterPrivatePageTemplateTests
{
 private sealed class Template(bool yes):ICenterPrivatePageTemplateAuthorization
 {
  public ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal actor,string center,string operation,
   CancellationToken ct)=>ValueTask.FromResult(yes);
 }
 private sealed class Pages(bool yes):ICenterSiteDraftAuthorization
 {
  public ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal actor,string center,Guid page,
   string operation,CancellationToken ct)=>ValueTask.FromResult(yes);
 }
 private static ClaimsPrincipal Actor(bool signed=true)=>new(new ClaimsIdentity(
  signed?[new Claim("sub","s38-actor"),new Claim("role","superadmin")]:
   [new Claim("role","superadmin")],"signed"));
 private static IConfiguration Empty=>new ConfigurationBuilder().Build();
 private static int? Status(IResult response)=>(response as IStatusCodeHttpResult)?.StatusCode;
 private static AppendPrivatePageTemplateRequest Sample()=>
  new(Guid.NewGuid(),Guid.NewGuid(),0,1,"Local template");
 [Fact] public async Task Template_commands_require_identity_exact_source_and_two_separate_grants()
 {
  var request=Sample();
  Task<IResult> Post(ClaimsPrincipal? actor=null,AppendPrivatePageTemplateRequest? input=null,
   bool grant=true,bool pages=true)=>CenterPrivatePageTemplateEndpoint.Append(
   actor??Actor(),Empty,new Template(grant),new Pages(pages),"center-a",input??request,default);
  Assert.Equal(401,Status(await Post(actor:new ClaimsPrincipal())));
  Assert.Equal(403,Status(await Post(actor:Actor(false))));
  Assert.Equal(400,Status(await Post(input:request with{TemplateId=Guid.Empty})));
  Assert.Equal(400,Status(await Post(input:request with{SourcePageId=Guid.Empty})));
  Assert.Equal(400,Status(await Post(input:request with{ExpectedRevision=-1})));
  Assert.Equal(400,Status(await Post(input:request with{PinnedPageRevision=0})));
  Assert.Equal(400,Status(await Post(input:request with{Label=" "})));
  Assert.Equal(400,Status(await Post(input:request with{Label=new string('x',161)})));
  Assert.Equal(403,Status(await Post(grant:false)));
  Assert.Equal(403,Status(await Post(pages:false)));
  Assert.Equal(503,Status(await Post()));
  Assert.Equal(403,Status(await CenterPrivatePageTemplateEndpoint.List(
   Actor(),Empty,new Template(false),new Pages(true),"center-a",null,default)));
  Assert.Equal(403,Status(await CenterPrivatePageTemplateEndpoint.List(
   Actor(),Empty,new Template(true),new Pages(false),"center-a",null,default)));
  Assert.Equal(400,Status(await CenterPrivatePageTemplateEndpoint.List(
   Actor(),Empty,new Template(true),new Pages(true),"center-a",Guid.Empty,default)));
  Assert.Equal(503,Status(await CenterPrivatePageTemplateEndpoint.List(
   Actor(),Empty,new Template(true),new Pages(true),"center-a",null,default)));
  Assert.Equal(400,Status(await CenterPrivatePageTemplateEndpoint.History(
   Actor(),Empty,new Template(true),new Pages(true),"center-a",Guid.Empty,null,default)));
  Assert.Equal(400,Status(await CenterPrivatePageTemplateEndpoint.History(
   Actor(),Empty,new Template(true),new Pages(true),"center-a",Guid.NewGuid(),0,default)));
  Assert.Equal(503,Status(await CenterPrivatePageTemplateEndpoint.History(
   Actor(),Empty,new Template(true),new Pages(true),"center-a",Guid.NewGuid(),null,default)));
 }
}
