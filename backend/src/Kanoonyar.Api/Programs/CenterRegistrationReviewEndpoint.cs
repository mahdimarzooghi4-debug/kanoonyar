using System.Security.Claims;
using Kanoonyar.Api.Security;
using Npgsql;

namespace Kanoonyar.Api.Programs;

public sealed record RaiseRegistrationReviewRequest(Guid ReviewRequestId,string ReviewTopic);

/// <summary>Separate scope/operation permission for questions, never approval.</summary>
public interface ICenterRegistrationReviewAuthorization
{
 ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal user,string centerId,Guid classId,
    Guid requestId,string operation,CancellationToken ct);
}
public sealed class UnapprovedCenterRegistrationReviewAuthorization:
    ICenterRegistrationReviewAuthorization
{
 public ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal user,string centerId,Guid classId,
    Guid requestId,string operation,CancellationToken ct)=>ValueTask.FromResult(false);
}

public static class CenterRegistrationReviewEndpoint
{
 private static readonly HashSet<string> Topics=new(StringComparer.Ordinal){
   "IDENTITY_AUTHORITY","ELIGIBILITY_CRITERIA","CONSENT_AUTHORITY","ADMISSION_POLICY"};
 private static async Task<bool> Permit(ICenterRegistrationReviewAuthorization auth,
   ClaimsPrincipal user,string center,Guid cls,Guid request,string operation,CancellationToken ct)
 {
  try {return await auth.IsAuthorizedAsync(user,center,cls,request,operation,ct);}
  catch(OperationCanceledException) when(ct.IsCancellationRequested){throw;}
  catch{return false;}
 }
 private static IResult? ValidateActorAndPath(ClaimsPrincipal user,string center,Guid cls,Guid request)
 {
  if(user.Identity?.IsAuthenticated!=true)
    return Results.Problem(statusCode:401,title:"Authentication required");
  if(string.IsNullOrWhiteSpace(user.FindFirst("sub")?.Value))
    return Results.Problem(statusCode:403,title:"Verified subject required");
  if(string.IsNullOrWhiteSpace(center)||cls==Guid.Empty||request==Guid.Empty)
    return Results.Problem(statusCode:400,title:"Invalid registration review identity");
  return null;
 }
 public static async Task<IResult> Raise(ClaimsPrincipal user,IConfiguration config,
   ICenterRegistrationReviewAuthorization auth,string centerId,Guid classId,Guid requestId,
   RaiseRegistrationReviewRequest? body,CancellationToken ct)
 {
  var invalid=ValidateActorAndPath(user,centerId,classId,requestId);
  if(invalid is not null)return invalid;
  if(body is null||body.ReviewRequestId==Guid.Empty||body.ReviewTopic is null
     ||!Topics.Contains(body.ReviewTopic))
    return Results.Problem(statusCode:400,title:"Invalid human-review topic or request ID");
  if(!await Permit(auth,user,centerId,classId,requestId,"REGISTRATION_REVIEW_REQUIREMENT_RAISE",ct))
    return Results.Problem(statusCode:403,title:"Independent registration review write grant required");
  var url=config["CENTER_REGISTRATION_REVIEW_WRITE_DATABASE_URL"];
  if(string.IsNullOrWhiteSpace(url))
    return Results.Problem(statusCode:503,title:"Human-review recording unavailable");
  try
  {
   await using var db=new NpgsqlConnection(url);
   await db.OpenAsync(ct);
   await using var tx=await db.BeginTransactionAsync(ct);
   if(!await RestrictedRegistrationReviewRole.VerifyWriterAsync(db,tx,ct))
     return Results.Problem(statusCode:503,title:"Unsafe review SQL writer");
   await using(var scope=new NpgsqlCommand(
     "SELECT set_config('kanoonyar.center_id',@center,true)",db,tx))
   {
    scope.Parameters.AddWithValue("center",centerId);
    await scope.ExecuteNonQueryAsync(ct);
   }
   // Membership is checked again by the SQL command; this remains a non-authority signal.
   await using var cmd=new NpgsqlCommand(
     """
     SELECT learning.request_registration_human_review(
       @center,@class,@request,@review,@topic,@actor,@audit,@event,@correlation)
     """,db,tx);
   cmd.Parameters.AddWithValue("center",centerId);
   cmd.Parameters.AddWithValue("class",classId);
   cmd.Parameters.AddWithValue("request",requestId);
   cmd.Parameters.AddWithValue("review",body.ReviewRequestId);
   cmd.Parameters.AddWithValue("topic",body.ReviewTopic);
   cmd.Parameters.AddWithValue("actor",user.FindFirst("sub")!.Value);
   cmd.Parameters.AddWithValue("audit",Guid.NewGuid());
   cmd.Parameters.AddWithValue("event",Guid.NewGuid());
   cmd.Parameters.AddWithValue("correlation",Guid.NewGuid().ToString("N"));
   if(await cmd.ExecuteScalarAsync(ct) is not Guid stored||stored!=body.ReviewRequestId)
     throw new InvalidOperationException("Review request identity not persisted");
   await tx.CommitAsync(ct);
   return Results.Ok(new{classId,requestId,reviewRequestId=stored,
       reviewTopic=body.ReviewTopic,reviewStatus="REVIEW_REQUIRED",
       enrollmentDecision=(string?)null,seatReserved=false});
  }
  catch(PostgresException ex) when(ex.SqlState is "23503" or "23505" or "23514" or "40001")
  {return Results.Problem(statusCode:409,title:"Review already requested or registration mismatch");}
  catch(PostgresException ex) when(ex.SqlState=="42501"&&
    ex.MessageText=="Center scope or active membership absent")
  {return Results.Problem(statusCode:403,title:"Live center membership required");}
  catch(PostgresException ex) when(ex.SqlState=="42501")
  {return Results.Problem(statusCode:503,title:"Review SQL privilege unavailable");}
  catch(PostgresException ex) when(ex.SqlState=="22023")
  {return Results.Problem(statusCode:400,title:"Invalid review request");}
  catch(NpgsqlException){return Results.Problem(statusCode:503,title:"Review recording unavailable");}
  catch(TimeoutException){return Results.Problem(statusCode:503,title:"Review recording unavailable");}
 }
 public static async Task<IResult> List(ClaimsPrincipal user,IConfiguration config,
   ICenterRegistrationReviewAuthorization auth,string centerId,Guid classId,Guid requestId,
   CancellationToken ct)
 {
  var invalid=ValidateActorAndPath(user,centerId,classId,requestId);
  if(invalid is not null)return invalid;
  if(!await Permit(auth,user,centerId,classId,requestId,"REGISTRATION_REVIEW_REQUIREMENT_READ",ct))
    return Results.Problem(statusCode:403,title:"Independent registration review read grant required");
  var url=config["CENTER_DRAFT_READ_DATABASE_URL"];
  if(string.IsNullOrWhiteSpace(url))
    return Results.Problem(statusCode:503,title:"Registration review read unavailable");
  try
  {
   await using var db=new NpgsqlConnection(url);
   await db.OpenAsync(ct);
   await using var tx=await db.BeginTransactionAsync(ct);
   if(!await RestrictedRegistrationReviewRole.VerifyReaderAsync(db,tx,ct))
     return Results.Problem(statusCode:503,title:"Unsafe registration review reader");
   await using(var scope=new NpgsqlCommand(
       "SELECT set_config('kanoonyar.center_id',@center,true)",db,tx))
   {
    scope.Parameters.AddWithValue("center",centerId);
    await scope.ExecuteNonQueryAsync(ct);
   }
   await using(var exists=new NpgsqlCommand(
     """
     SELECT EXISTS(
       SELECT 1 FROM learning.class_registration_intents i
       JOIN centers.memberships m ON m.center_id=i.center_id AND m.subject_id=@actor
       JOIN centers.registry c ON c.center_id=i.center_id
       WHERE i.center_id=@center AND i.class_id=@class AND i.request_id=@request
         AND i.state='REQUESTED' AND m.is_active AND c.is_active)
     """,db,tx))
   {
    exists.Parameters.AddWithValue("center",centerId);
    exists.Parameters.AddWithValue("class",classId);
    exists.Parameters.AddWithValue("request",requestId);
    exists.Parameters.AddWithValue("actor",user.FindFirst("sub")!.Value);
    if(await exists.ExecuteScalarAsync(ct) is not true)
      return Results.Problem(statusCode:404,title:"Registration request not found in authorized center");
   }
   await using var cmd=new NpgsqlCommand(
     """
     SELECT review_request_id,review_topic,recorded_at
     FROM learning.registration_review_requests
     WHERE center_id=@center AND registration_request_id=@request
     ORDER BY review_topic
     """,db,tx);
   cmd.Parameters.AddWithValue("center",centerId);
   cmd.Parameters.AddWithValue("request",requestId);
   var items=new List<object>();
   await using(var reader=await cmd.ExecuteReaderAsync(ct))
   {
    while(await reader.ReadAsync(ct))
    {
     if(items.Count==4)throw new InvalidOperationException("Unexpected review requirement count");
     items.Add(new{reviewRequestId=reader.GetGuid(0),reviewTopic=reader.GetString(1),
       recordedAt=reader.GetDateTime(2),reviewStatus="REVIEW_REQUIRED"});
    }
   }
   await tx.CommitAsync(ct);
   return Results.Ok(new{classId,requestId,registrationState="REQUESTED",
       enrollmentDecision=(string?)null,seatReserved=false,reviewRequests=items});
  }
  catch(NpgsqlException){return Results.Problem(statusCode:503,title:"Review read unavailable");}
  catch(TimeoutException){return Results.Problem(statusCode:503,title:"Review read unavailable");}
 }
}
