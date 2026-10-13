using System.Security.Claims;
using Kanoonyar.Api.Programs;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Xunit;
namespace Kanoonyar.Api.Tests;
public sealed class CenterSessionPreparationTests
{
 private sealed class Grant(bool allowed):ICenterSessionPreparationAuthorization
 {
  public ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal p,string c,string op,Guid s,CancellationToken ct)
   =>ValueTask.FromResult(allowed);
 }
 private sealed class Source(bool allowed):ICenterSourceCatalogReadAuthorization
 {
  public ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal p,string c,CancellationToken ct)
   =>ValueTask.FromResult(allowed);
 }
 private static ClaimsPrincipal Actor(bool subject=true)=>new(new ClaimsIdentity(
  subject?[new Claim("sub","synthetic"),new Claim("role","superadmin")]:
  [new Claim("role","superadmin")],"signed"));
 private static IConfiguration Empty=>new ConfigurationBuilder().Build();
 private static int? Status(IResult r)=>(r as IStatusCodeHttpResult)?.StatusCode;
 private static AppendSessionPreparationRequest Sample()=>
  new(Guid.NewGuid(),Guid.NewGuid(),0,"Private teaching plan","Outline","Materials");
 [Fact] public async Task Strong_negative_boundaries_and_two_independent_grants_fail_closed()
 {
  var req=Sample();
  Task<IResult> Post(ClaimsPrincipal? p=null,AppendSessionPreparationRequest? body=null,
   bool grant=true,bool source=true)=>
   CenterSessionPreparationEndpoint.Append(p??Actor(),Empty,new Grant(grant),new Source(source),
    "center-a",body??req,default);
  Assert.Equal(401,Status(await Post(p:new ClaimsPrincipal())));
  Assert.Equal(403,Status(await Post(p:Actor(false))));
  Assert.Equal(400,Status(await Post(body:req with{SessionId=Guid.Empty})));
  Assert.Equal(400,Status(await Post(body:req with{ClassId=Guid.Empty})));
  Assert.Equal(400,Status(await Post(body:req with{ExpectedRevision=-1})));
  Assert.Equal(400,Status(await Post(body:req with{Title=" " })));
  Assert.Equal(400,Status(await Post(body:req with{Title=new string('x',161)})));
  Assert.Equal(400,Status(await Post(body:req with{TeachingOutline=new string('x',4001)})));
  Assert.Equal(400,Status(await Post(body:req with{MaterialsNote=new string('x',2001)})));
  Assert.Equal(403,Status(await Post(grant:false)));
  Assert.Equal(403,Status(await Post(source:false)));
  Assert.Equal(503,Status(await Post()));
  Assert.Equal(403,Status(await CenterSessionPreparationEndpoint.List(
   Actor(),Empty,new Grant(false),new Source(true),"center-a",null,default)));
  Assert.Equal(403,Status(await CenterSessionPreparationEndpoint.List(
   Actor(),Empty,new Grant(true),new Source(false),"center-a",null,default)));
  Assert.Equal(400,Status(await CenterSessionPreparationEndpoint.List(
   Actor(),Empty,new Grant(true),new Source(true),"center-a",Guid.Empty,default)));
  Assert.Equal(503,Status(await CenterSessionPreparationEndpoint.List(
   Actor(),Empty,new Grant(true),new Source(true),"center-a",null,default)));
  Assert.Equal(400,Status(await CenterSessionPreparationEndpoint.History(
   Actor(),Empty,new Grant(true),new Source(true),"center-a",Guid.NewGuid(),0,default)));
  Assert.Equal(503,Status(await CenterSessionPreparationEndpoint.History(
   Actor(),Empty,new Grant(true),new Source(true),"center-a",Guid.NewGuid(),null,default)));
 }
}
