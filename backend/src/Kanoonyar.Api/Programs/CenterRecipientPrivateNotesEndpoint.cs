using System.Data;
using System.Security.Claims;
using Kanoonyar.Api.Security;
using Npgsql;
using NpgsqlTypes;

namespace Kanoonyar.Api.Programs;

public sealed record AppendRecipientPrivateNoteRequest(Guid NoteId,string NoteText);
public static class CenterRecipientPrivateNotesEndpoint
{
 private static IResult? Check(ClaimsPrincipal actor,string center,Guid proposal,Guid? cursor=null)
 {
  if(actor.Identity?.IsAuthenticated!=true)
   return Results.Problem(statusCode:401,title:"Authentication required");
  if(string.IsNullOrWhiteSpace(actor.FindFirst("sub")?.Value))
   return Results.Problem(statusCode:403,title:"Verified OIDC subject required");
  if(string.IsNullOrWhiteSpace(center)||proposal==Guid.Empty||cursor==Guid.Empty)
   return Results.Problem(statusCode:400,title:"Invalid recipient note identity or cursor");
  return null;
 }
 private static async Task<bool> Authorized(ICenterCollaborationAuthorization grants,
  ClaimsPrincipal actor,string center,string op,CancellationToken ct)
 {
  try
  {
   return await grants.IsAuthorizedAsync(actor,center,"COLLABORATION_INCOMING_READ",ct)&&
    await grants.IsAuthorizedAsync(actor,center,op,ct);
  }
  catch(OperationCanceledException) when(ct.IsCancellationRequested){throw;}
  catch{return false;}
 }
 public static async Task<IResult> Append(ClaimsPrincipal actor,IConfiguration config,
  ICenterCollaborationAuthorization grants,string centerId,Guid proposalId,
  AppendRecipientPrivateNoteRequest? request,CancellationToken ct)
 {
  var invalid=Check(actor,centerId,proposalId);if(invalid is not null)return invalid;
  if(request is null||request.NoteId==Guid.Empty||request.NoteText is null||
   string.IsNullOrWhiteSpace(request.NoteText)||request.NoteText.Length>1200)
   return Results.Problem(statusCode:400,title:"Invalid recipient-private note");
  if(!await Authorized(grants,actor,centerId,"COLLABORATION_RECIPIENT_NOTE_APPEND",ct))
   return Results.Problem(statusCode:403,title:"Recipient read and independent note grant required");
  var url=config["CENTER_RECIPIENT_NOTE_WRITE_DATABASE_URL"];
  if(string.IsNullOrWhiteSpace(url))
   return Results.Problem(statusCode:503,title:"Recipient note writer unavailable");
  try
  {
   await using var db=new NpgsqlConnection(url);await db.OpenAsync(ct);
   await using var tx=await db.BeginTransactionAsync(ct);
   if(!await RestrictedRecipientNoteRole.WriterAsync(db,tx,ct))
    return Results.Problem(statusCode:503,title:"Unsafe recipient note writer");
   await using(var scope=new NpgsqlCommand("SELECT set_config('kanoonyar.center_id',@center,true)",db,tx))
   {scope.Parameters.AddWithValue("center",centerId);await scope.ExecuteNonQueryAsync(ct);}
   await using var q=new NpgsqlCommand("""
    SELECT collaboration.append_recipient_private_note(@note,@proposal,@center,@content,
     @actor,@audit,@outbox,@correlation)
    """,db,tx);
   q.Parameters.AddWithValue("note",request.NoteId);
   q.Parameters.AddWithValue("proposal",proposalId);
   q.Parameters.AddWithValue("center",centerId);
   q.Parameters.AddWithValue("content",request.NoteText.Trim());
   q.Parameters.AddWithValue("actor",actor.FindFirst("sub")!.Value);
   q.Parameters.AddWithValue("audit",Guid.NewGuid());
   q.Parameters.AddWithValue("outbox",Guid.NewGuid());
   q.Parameters.AddWithValue("correlation",Guid.NewGuid().ToString("N"));
   if(await q.ExecuteScalarAsync(ct) is not Guid id||id!=request.NoteId)
    throw new InvalidOperationException("Unexpected recipient note acknowledgement");
   await tx.CommitAsync(ct);
   return Results.Ok(new{noteId=id,proposalId,targetCenterId=centerId,
     visibility="RECIPIENT_INTERNAL",reviewStatus="PROPOSED_UNREVIEWED",
     isAccepted=false,isExecuted=false});
  }
  catch(PostgresException e) when(e.SqlState is "23503" or "23505" or "23514" or "40001")
   {return Results.Problem(statusCode:409,title:"Duplicate note or proposal not addressed to recipient");}
  catch(PostgresException e) when(e.SqlState=="42501"&&
    e.MessageText=="Recipient center membership or scope absent")
   {return Results.Problem(statusCode:403,title:"Live recipient membership required");}
  catch(PostgresException e) when(e.SqlState=="42501"&&
    e.MessageText=="Recipient proposal scope absent")
   {return Results.Problem(statusCode:403,title:"Proposal is not addressed to requesting recipient");}
  catch(PostgresException e) when(e.SqlState=="42501")
   {return Results.Problem(statusCode:503,title:"Recipient note SQL access unavailable");}
  catch(PostgresException e) when(e.SqlState=="22023")
   {return Results.Problem(statusCode:400,title:"Invalid note command");}
  catch(NpgsqlException){return Results.Problem(statusCode:503,title:"Recipient note storage unavailable");}
  catch(TimeoutException){return Results.Problem(statusCode:503,title:"Recipient note storage unavailable");}
 }
 public static async Task<IResult> List(ClaimsPrincipal actor,IConfiguration config,
  ICenterCollaborationAuthorization grants,string centerId,Guid proposalId,
  Guid? beforeNoteId,CancellationToken ct)
 {
  var invalid=Check(actor,centerId,proposalId,beforeNoteId);if(invalid is not null)return invalid;
  if(!await Authorized(grants,actor,centerId,"COLLABORATION_RECIPIENT_NOTE_READ",ct))
   return Results.Problem(statusCode:403,title:"Recipient read and independent private note read grants required");
  var url=config["CENTER_DRAFT_READ_DATABASE_URL"];
  if(string.IsNullOrWhiteSpace(url))
   return Results.Problem(statusCode:503,title:"Recipient notes read unavailable");
  try
  {
   await using var db=new NpgsqlConnection(url);await db.OpenAsync(ct);
   await using var tx=await db.BeginTransactionAsync(IsolationLevel.RepeatableRead,ct);
   if(!await RestrictedRecipientNoteRole.ReaderAsync(db,tx,ct)||
      !await RestrictedCollaborationRole.ReaderAsync(db,tx,ct))
    return Results.Problem(statusCode:503,title:"Unsafe recipient notes reader role");
   await using(var scope=new NpgsqlCommand("SELECT set_config('kanoonyar.center_id',@center,true)",db,tx))
   {scope.Parameters.AddWithValue("center",centerId);await scope.ExecuteNonQueryAsync(ct);}
   await using(var member=new NpgsqlCommand("""
    SELECT EXISTS(SELECT 1 FROM centers.memberships m JOIN centers.registry c
     ON c.center_id=m.center_id WHERE m.center_id=@center AND m.subject_id=@actor
      AND m.is_active AND c.is_active)
    """,db,tx))
   {
    member.Parameters.AddWithValue("center",centerId);
    member.Parameters.AddWithValue("actor",actor.FindFirst("sub")!.Value);
    if(await member.ExecuteScalarAsync(ct) is not true)
     return Results.Problem(statusCode:403,title:"Live recipient center membership required");
   }
   await using(var existence=new NpgsqlCommand("""
    SELECT EXISTS(SELECT 1 FROM collaboration.center_proposals p
     WHERE p.proposal_id=@proposal AND p.target_center_id=@center)
    """,db,tx))
   {
    existence.Parameters.AddWithValue("proposal",proposalId);
    existence.Parameters.AddWithValue("center",centerId);
    if(await existence.ExecuteScalarAsync(ct) is not true)
     return Results.Problem(statusCode:404,title:"No authorized directed proposal");
   }
   await using var q=new NpgsqlCommand("""
    SELECT note_id,note_text,recorded_at FROM collaboration.recipient_private_notes
    WHERE target_center_id=@center AND proposal_id=@proposal
     AND (@before::uuid IS NULL OR note_id<@before::uuid)
    ORDER BY note_id DESC LIMIT 21
    """,db,tx);
   q.Parameters.AddWithValue("center",centerId);
   q.Parameters.AddWithValue("proposal",proposalId);
   q.Parameters.Add("before",NpgsqlDbType.Uuid).Value=beforeNoteId.HasValue?
    (object)beforeNoteId.Value:DBNull.Value;
   var notes=new List<object>();Guid? cursor=null;int count=0;
   await using(var reader=await q.ExecuteReaderAsync(ct))
   {
    while(await reader.ReadAsync(ct))
    {
     if(count<20)
     {
      cursor=reader.GetGuid(0);
      notes.Add(new{noteId=cursor,noteText=reader.GetString(1),
       recordedAt=reader.GetDateTime(2),visibility="RECIPIENT_INTERNAL",
       reviewStatus="PROPOSED_UNREVIEWED",isAccepted=false,isExecuted=false});
     }
     count++;
    }
   }
   await tx.CommitAsync(ct);
   return Results.Ok(new{proposalId,targetCenterId=centerId,notes,
    nextBeforeNoteId=count>20?cursor:null,visibility="RECIPIENT_INTERNAL",
    reviewStatus="PROPOSED_UNREVIEWED",isAccepted=false,isExecuted=false});
  }
  catch(NpgsqlException){return Results.Problem(statusCode:503,title:"Recipient notes source unavailable");}
  catch(TimeoutException){return Results.Problem(statusCode:503,title:"Recipient notes source unavailable");}
 }
}
