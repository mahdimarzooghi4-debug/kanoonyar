using System.Security.Claims;
using Kanoonyar.Api.Security;
using Npgsql;
using NpgsqlTypes;

namespace Kanoonyar.Api.Programs;

public sealed record SetClassCapacityRequest(long ExpectedRevision,int Capacity);
public sealed record RequestClassRegistrationIntent(Guid RequestId,Guid ApplicantRef);

/// <summary>Independent, revocable operation-specific grant. No approved runtime implementation.</summary>
public interface ICenterClassIntakeAuthorization
{
    ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal actor,string centerId,
      Guid classId,string operation,CancellationToken ct);
}
public sealed class UnapprovedCenterClassIntakeAuthorization:ICenterClassIntakeAuthorization
{
    public ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal actor,string centerId,
      Guid classId,string operation,CancellationToken ct)=>ValueTask.FromResult(false);
}

public static class CenterClassIntakeEndpoint
{
    private static async Task<bool> Grant(ClaimsPrincipal actor,
      ICenterClassIntakeAuthorization auth,string center,Guid cls,string operation,CancellationToken ct)
    {
        try {return await auth.IsAuthorizedAsync(actor,center,cls,operation,ct);}
        catch(OperationCanceledException) when(ct.IsCancellationRequested){throw;}
        catch{return false;}
    }
    private static string? Subject(ClaimsPrincipal actor)=>actor.FindFirst("sub")?.Value;

    public static async Task<IResult> SetCapacity(ClaimsPrincipal actor,IConfiguration config,
      ICenterClassIntakeAuthorization auth,string centerId,Guid classId,
      SetClassCapacityRequest? request,CancellationToken ct)
    {
        if(actor.Identity?.IsAuthenticated!=true)return Results.Problem(statusCode:401,title:"Authentication required");
        var sub=Subject(actor);
        if(string.IsNullOrWhiteSpace(sub))return Results.Problem(statusCode:403,title:"Verified subject required");
        if(string.IsNullOrWhiteSpace(centerId)||classId==Guid.Empty||request is null||
           request.ExpectedRevision<0||request.Capacity<=0)
           return Results.Problem(statusCode:400,title:"Invalid class capacity command");
        if(!await Grant(actor,auth,centerId,classId,"CLASS_CAPACITY_REVISE",ct))
            return Results.Problem(statusCode:403,title:"Independent capacity revision grant required");
        return await Write(actor,config,centerId,classId,"CAPACITY",request,null,sub,ct);
    }

    public static async Task<IResult> RequestRegistration(ClaimsPrincipal actor,IConfiguration config,
      ICenterClassIntakeAuthorization auth,string centerId,Guid classId,
      RequestClassRegistrationIntent? request,CancellationToken ct)
    {
        if(actor.Identity?.IsAuthenticated!=true)return Results.Problem(statusCode:401,title:"Authentication required");
        var sub=Subject(actor);
        if(string.IsNullOrWhiteSpace(sub))return Results.Problem(statusCode:403,title:"Verified subject required");
        if(string.IsNullOrWhiteSpace(centerId)||classId==Guid.Empty||request is null||
          request.RequestId==Guid.Empty||request.ApplicantRef==Guid.Empty)
          return Results.Problem(statusCode:400,title:"Invalid registration request");
        if(!await Grant(actor,auth,centerId,classId,"CLASS_REGISTRATION_INTENT_SUBMIT",ct))
          return Results.Problem(statusCode:403,title:"Independent registration request grant required");
        return await Write(actor,config,centerId,classId,"REQUEST",null,request,sub,ct);
    }
    private static async Task<IResult> Write(ClaimsPrincipal actor,IConfiguration config,string center,
       Guid cls,string action,SetClassCapacityRequest? capacity,RequestClassRegistrationIntent? request,
       string subject,CancellationToken ct)
    {
        var url=config["CENTER_CLASS_INTAKE_WRITE_DATABASE_URL"];
        if(string.IsNullOrWhiteSpace(url))return Results.Problem(statusCode:503,title:"Class intake command unavailable");
        try
        {
            await using var db=new NpgsqlConnection(url);
            await db.OpenAsync(ct);
            await using var tx=await db.BeginTransactionAsync(ct);
            if(!await RestrictedClassIntakeRole.VerifyAsync(db,tx,ct))
              return Results.Problem(statusCode:503,title:"Unsafe class intake SQL role");
            await using(var scope=new NpgsqlCommand("SELECT set_config('kanoonyar.center_id',@center,true)",db,tx))
            {
                scope.Parameters.AddWithValue("center",center);
                await scope.ExecuteNonQueryAsync(ct);
            }
            await using(var live=new NpgsqlCommand(
              """
              SELECT EXISTS(SELECT 1 FROM centers.memberships m JOIN centers.registry c
                ON c.center_id=m.center_id
                WHERE m.center_id=@center AND m.subject_id=@actor
                  AND m.is_active AND c.is_active)
              """,db,tx))
            {
                live.Parameters.AddWithValue("center",center);
                live.Parameters.AddWithValue("actor",subject);
                if(await live.ExecuteScalarAsync(ct) is not true)
                   return Results.Problem(statusCode:403,title:"Active center membership required");
            }
            var sql=action=="CAPACITY"
              ? "SELECT learning.append_class_capacity_revision(@center,@class,@expected,@capacity,@actor,@audit,@event,@correlation)"
              : "SELECT learning.record_class_registration_intent(@center,@request,@class,@applicant,@actor,@audit,@event,@correlation)";
            await using var command=new NpgsqlCommand(sql,db,tx);
            command.Parameters.AddWithValue("center",center);
            command.Parameters.AddWithValue("class",cls);
            command.Parameters.AddWithValue("actor",subject);
            command.Parameters.AddWithValue("audit",Guid.NewGuid());
            command.Parameters.AddWithValue("event",Guid.NewGuid());
            command.Parameters.AddWithValue("correlation",Guid.NewGuid().ToString("N"));
            if(capacity is not null)
            {
                command.Parameters.AddWithValue("expected",capacity.ExpectedRevision);
                command.Parameters.AddWithValue("capacity",capacity.Capacity);
            }
            else
            {
                command.Parameters.AddWithValue("request",request!.RequestId);
                command.Parameters.AddWithValue("applicant",request.ApplicantRef);
            }
            var revision=(long)(await command.ExecuteScalarAsync(ct)
               ?? throw new InvalidOperationException("Missing persisted intake revision"));
            await tx.CommitAsync(ct);
            return capacity is not null
               ? Results.Ok(new {classId=cls,revision,capacity=capacity.Capacity,
                    confirmedEnrollmentCount=(int?)null})
               : Results.Ok(new {classId=cls,requestId=request!.RequestId,
                    capacityRevision=revision,state="REQUESTED",seatReserved=false});
        }
        catch(PostgresException ex) when(ex.SqlState is "23505" or "23503" or "23514" or "40001")
        {return Results.Problem(statusCode:409,title:"Capacity version, class or registration conflict");}
        catch(PostgresException ex) when(ex.SqlState=="42501"&&
            ex.MessageText=="Center scope or active membership absent")
        {return Results.Problem(statusCode:403,title:"Center membership or scope denied");}
        catch(PostgresException ex) when(ex.SqlState=="42501")
        {return Results.Problem(statusCode:503,title:"Class intake SQL privileges insufficient");}
        catch(PostgresException ex) when(ex.SqlState=="22023")
        {return Results.Problem(statusCode:400,title:"Invalid class intake command");}
        catch(NpgsqlException){return Results.Problem(statusCode:503,title:"Class intake command unavailable");}
        catch(TimeoutException){return Results.Problem(statusCode:503,title:"Class intake command unavailable");}
    }

    public static async Task<IResult> Read(ClaimsPrincipal actor,IConfiguration config,
       ICenterClassIntakeAuthorization auth,string centerId,Guid classId,Guid? beforeRequestId,CancellationToken ct)
    {
        if(actor.Identity?.IsAuthenticated!=true)return Results.Problem(statusCode:401,title:"Authentication required");
        var sub=Subject(actor);
        if(string.IsNullOrWhiteSpace(sub))return Results.Problem(statusCode:403,title:"Verified subject required");
        if(string.IsNullOrWhiteSpace(centerId)||classId==Guid.Empty||
          beforeRequestId==Guid.Empty)return Results.Problem(statusCode:400,title:"Invalid class intake read");
        if(!await Grant(actor,auth,centerId,classId,"CLASS_INTAKE_READ",ct))
          return Results.Problem(statusCode:403,title:"Independent class intake read grant required");
        var url=config["CENTER_DRAFT_READ_DATABASE_URL"];
        if(string.IsNullOrWhiteSpace(url))return Results.Problem(statusCode:503,title:"Class intake read unavailable");
        try
        {
            await using var db=new NpgsqlConnection(url);
            await db.OpenAsync(ct);
            await using var tx=await db.BeginTransactionAsync(ct);
            if(!await RestrictedDraftDatabaseRole.VerifyReadOnlyAsync(db,tx,ct))
              return Results.Problem(statusCode:503,title:"Unsafe class intake reader");
            await using(var scope=new NpgsqlCommand("SELECT set_config('kanoonyar.center_id',@center,true)",db,tx))
            {
                scope.Parameters.AddWithValue("center",centerId);
                await scope.ExecuteNonQueryAsync(ct);
            }
            await using(var check=new NpgsqlCommand(
               """
               SELECT EXISTS(
                 SELECT 1 FROM learning.center_classes c
                 JOIN centers.memberships m ON m.center_id=c.center_id AND m.subject_id=@actor
                 JOIN centers.registry registry ON registry.center_id=c.center_id
                 WHERE c.center_id=@center AND c.class_id=@class AND m.is_active AND registry.is_active)
               """,db,tx))
            {
                check.Parameters.AddWithValue("actor",sub);
                check.Parameters.AddWithValue("center",centerId);
                check.Parameters.AddWithValue("class",classId);
                if(await check.ExecuteScalarAsync(ct) is not true)
                   return Results.Problem(statusCode:404,title:"Class not found in authorized center");
            }
            long? currentRevision=null;int? configuredCapacity=null;
            await using(var capacity=new NpgsqlCommand(
              """
              SELECT revision,capacity FROM learning.class_capacity_revisions
              WHERE center_id=@center AND class_id=@class ORDER BY revision DESC LIMIT 1
              """,db,tx))
            {
                capacity.Parameters.AddWithValue("center",centerId);
                capacity.Parameters.AddWithValue("class",classId);
                await using var reader=await capacity.ExecuteReaderAsync(ct);
                if(await reader.ReadAsync(ct))
                {currentRevision=reader.GetInt64(0);configuredCapacity=reader.GetInt32(1);}
            }
            await using var cmd=new NpgsqlCommand(
              """
              SELECT request_id,applicant_ref,capacity_revision,recorded_at
              FROM learning.class_registration_intents
              WHERE center_id=@center AND class_id=@class
                AND (@before::uuid IS NULL OR request_id<@before::uuid)
              ORDER BY request_id DESC LIMIT 21
              """,db,tx);
            cmd.Parameters.AddWithValue("center",centerId);
            cmd.Parameters.AddWithValue("class",classId);
            cmd.Parameters.Add("before",NpgsqlDbType.Uuid).Value=
                beforeRequestId.HasValue?(object)beforeRequestId.Value:DBNull.Value;
            var records=new List<object>();Guid? cursor=null;int count=0;
            await using(var reader=await cmd.ExecuteReaderAsync(ct))
            {
                while(await reader.ReadAsync(ct))
                {
                    if(count<20)
                    {
                        cursor=reader.GetGuid(0);
                        records.Add(new {
                           requestId=reader.GetGuid(0),applicantRef=reader.GetGuid(1),
                           capacityRevision=reader.GetInt64(2),
                           recordedAt=reader.GetDateTime(3),state="REQUESTED",seatReserved=false
                        });
                    }
                    count++;
                }
            }
            await tx.CommitAsync(ct);
            return Results.Ok(new {classId,currentRevision,configuredCapacity,
                confirmedEnrollmentCount=(int?)null,
                registrationIntents=records,nextBeforeRequestId=count>20?cursor:null});
        }
        catch(NpgsqlException){return Results.Problem(statusCode:503,title:"Class intake read unavailable");}
        catch(TimeoutException){return Results.Problem(statusCode:503,title:"Class intake read unavailable");}
    }
}
