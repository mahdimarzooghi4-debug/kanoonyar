using System.Security.Claims;
using Kanoonyar.Api.Programs;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Xunit;
namespace Kanoonyar.Api.Tests;
public sealed class CenterRecipientNotesTests
{
 private sealed class Grants(bool incoming,bool note):ICenterCollaborationAuthorization
 {
  public ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal actor,string center,string op,CancellationToken ct)=>
   ValueTask.FromResult(op=="COLLABORATION_INCOMING_READ"?incoming:note);
 }
 private static ClaimsPrincipal Actor(bool withSub=true)=>new(new ClaimsIdentity(
  withSub?[new Claim("sub","s36-synthetic"),new Claim("role","superadmin")]:
  [new Claim("role","superadmin")],"test"));
 private static IConfiguration Empty=>new ConfigurationBuilder().Build();
 private static int? Status(IResult r)=>(r as IStatusCodeHttpResult)?.StatusCode;
 [Fact] public async Task No_implicit_recipient_or_admin_authority_and_invalid_notes_fail_closed()
 {
  var p=Guid.NewGuid();var request=new AppendRecipientPrivateNoteRequest(Guid.NewGuid(),"Internal only");
  Task<IResult> Write(ClaimsPrincipal? actor=null,AppendRecipientPrivateNoteRequest? body=null,
    bool incoming=true,bool note=true)=>
   CenterRecipientPrivateNotesEndpoint.Append(actor??Actor(),Empty,new Grants(incoming,note),
    "center-b",p,body??request,default);
  Assert.Equal(401,Status(await Write(actor:new ClaimsPrincipal())));
  Assert.Equal(403,Status(await Write(actor:Actor(false))));
  Assert.Equal(400,Status(await Write(body:request with{NoteId=Guid.Empty})));
  Assert.Equal(400,Status(await Write(body:request with{NoteText="  "})));
  Assert.Equal(400,Status(await Write(body:request with{NoteText=new string('x',1201)})));
  Assert.Equal(403,Status(await Write(incoming:false)));
  Assert.Equal(403,Status(await Write(note:false)));
  Assert.Equal(503,Status(await Write()));
  Assert.Equal(400,Status(await CenterRecipientPrivateNotesEndpoint.List(
   Actor(),Empty,new Grants(true,true),"center-b",Guid.Empty,null,default)));
  Assert.Equal(400,Status(await CenterRecipientPrivateNotesEndpoint.List(
   Actor(),Empty,new Grants(true,true),"center-b",p,Guid.Empty,default)));
  Assert.Equal(403,Status(await CenterRecipientPrivateNotesEndpoint.List(
   Actor(),Empty,new Grants(false,true),"center-b",p,null,default)));
  Assert.Equal(403,Status(await CenterRecipientPrivateNotesEndpoint.List(
   Actor(),Empty,new Grants(true,false),"center-b",p,null,default)));
  Assert.Equal(503,Status(await CenterRecipientPrivateNotesEndpoint.List(
   Actor(),Empty,new Grants(true,true),"center-b",p,null,default)));
 }
}
