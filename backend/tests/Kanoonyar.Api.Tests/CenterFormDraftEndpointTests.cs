using System.Security.Claims;
using Kanoonyar.Api.Programs;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Xunit;
namespace Kanoonyar.Api.Tests;
public sealed class CenterFormDraftEndpointTests
{
 private static IConfiguration Config=>new ConfigurationBuilder().Build();
 private static ClaimsPrincipal Actor(params Claim[] c)=>new(new ClaimsIdentity(c,"fixture"));
 private static int? Status(IResult r)=>(r as IStatusCodeHttpResult)?.StatusCode;
 private sealed class Permit:ICenterFormDraftAuthorization
 {public ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal a,string c,Guid f,string op,CancellationToken ct)=>ValueTask.FromResult(true);}
 private sealed class Broken:ICenterFormDraftAuthorization
 {public ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal a,string c,Guid f,string op,CancellationToken ct)=>throw new Exception("permission offline");}
 private static AppendCenterFormDraftRequest Valid()=>new(Guid.NewGuid(),0,"form-one","Safe form",
  [new FormDraftField("topic","Topic","SHORT_TEXT",true,[])]);
 [Fact] public async Task Default_denial_and_malformed_schema_fail_closed()
 {
  var actor=Actor(new Claim("sub","fixture"),new Claim("role","admin"));
  var denied=new UnapprovedCenterFormDraftAuthorization();
  var req=Valid();
  Assert.Equal(401,Status(await CenterFormDraftEndpoint.Append(new ClaimsPrincipal(),Config,denied,"a",req,default)));
  Assert.Equal(403,Status(await CenterFormDraftEndpoint.Append(Actor(new Claim("role","admin")),Config,denied,"a",req,default)));
  Assert.Equal(400,Status(await CenterFormDraftEndpoint.Append(actor,Config,new Permit(),"a",req with{Slug="../bad"},default)));
  Assert.Equal(400,Status(await CenterFormDraftEndpoint.Append(actor,Config,new Permit(),"a",req with{ExpectedRevision=-1},default)));
  Assert.Equal(400,Status(await CenterFormDraftEndpoint.Append(actor,Config,new Permit(),"a",req with{Fields=[]},default)));
  Assert.Equal(400,Status(await CenterFormDraftEndpoint.Append(actor,Config,new Permit(),"a",
    req with{Fields=[new("x","X","EXEC_SCRIPT",false,[])]},default)));
  Assert.Equal(400,Status(await CenterFormDraftEndpoint.Append(actor,Config,new Permit(),"a",
    req with{Fields=[new("x","X","SHORT_TEXT",true,[]),new("x","Dup","LONG_TEXT",false,[])]},default)));
  Assert.Equal(400,Status(await CenterFormDraftEndpoint.Append(actor,Config,new Permit(),"a",
    req with{Fields=[new("choice","Choose","SINGLE_SELECT",true,["same","same"])]},default)));
  Assert.Equal(403,Status(await CenterFormDraftEndpoint.Append(actor,Config,denied,"a",req,default)));
  Assert.Equal(403,Status(await CenterFormDraftEndpoint.Append(actor,Config,new Broken(),"a",req,default)));
  Assert.Equal(503,Status(await CenterFormDraftEndpoint.Append(actor,Config,new Permit(),"a",req,default)));
  Assert.Equal(403,Status(await CenterFormDraftEndpoint.List(actor,Config,denied,"a",null,default)));
  Assert.Equal(503,Status(await CenterFormDraftEndpoint.List(actor,Config,new Permit(),"a",null,default)));
  Assert.Equal(400,Status(await CenterFormDraftEndpoint.History(actor,Config,new Permit(),"a",req.FormId,0,default)));
  Assert.Equal(403,Status(await CenterFormDraftEndpoint.History(actor,Config,new Broken(),"a",req.FormId,null,default)));
  Assert.Equal(503,Status(await CenterFormDraftEndpoint.History(actor,Config,new Permit(),"a",req.FormId,null,default)));
 }
}
