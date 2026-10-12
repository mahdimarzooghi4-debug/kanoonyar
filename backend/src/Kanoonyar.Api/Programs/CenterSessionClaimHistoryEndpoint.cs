using System.Security.Claims;
using Kanoonyar.Api.Security;
using Npgsql;
using NpgsqlTypes;

namespace Kanoonyar.Api.Programs;

/// <summary>Separately granted read operations, distinct from all session-write permissions.</summary>
public interface ICenterSessionClaimHistoryAuthorization
{
    ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal user,string centerId,string operation,
        Guid sessionId,CancellationToken ct);
}
public sealed class UnapprovedCenterSessionClaimHistoryAuthorization :
    ICenterSessionClaimHistoryAuthorization
{
    public ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal user,string centerId,string operation,
        Guid sessionId,CancellationToken ct) => ValueTask.FromResult(false);
}

/// <summary>Always returns UNVERIFIED source assertions, never computed attendance/KPIs.</summary>
public static class CenterSessionClaimHistoryEndpoint
{
    private const int Limit=20;
    public static Task<IResult> Occurrences(ClaimsPrincipal user,IConfiguration config,
        ICenterSessionClaimHistoryAuthorization auth,string centerId,Guid sessionId,
        DateTimeOffset? beforeAt,Guid? beforeId,CancellationToken ct)
        => Read(user,config,auth,centerId,sessionId,null,beforeAt,beforeId,ct);
    public static Task<IResult> Attendance(ClaimsPrincipal user,IConfiguration config,
        ICenterSessionClaimHistoryAuthorization auth,string centerId,Guid sessionId,
        Guid occurrenceReportId,DateTimeOffset? beforeAt,Guid? beforeId,CancellationToken ct)
        => Read(user,config,auth,centerId,sessionId,occurrenceReportId,beforeAt,beforeId,ct);

    private static async Task<IResult> Read(ClaimsPrincipal user,IConfiguration config,
        ICenterSessionClaimHistoryAuthorization auth,string centerId,Guid sessionId,
        Guid? occurrenceId,DateTimeOffset? beforeAt,Guid? beforeId,CancellationToken ct)
    {
        if(user.Identity?.IsAuthenticated!=true)
            return Results.Problem(statusCode:401,title:"Authentication required");
        var subject=user.FindFirst("sub")?.Value;
        if(string.IsNullOrWhiteSpace(subject))
            return Results.Problem(statusCode:403,title:"Verified subject required");
        if(string.IsNullOrWhiteSpace(centerId)||sessionId==Guid.Empty||
           occurrenceId==Guid.Empty||beforeId==Guid.Empty||
           beforeAt.HasValue!=beforeId.HasValue||
           (beforeAt.HasValue&&beforeAt.Value.Offset!=TimeSpan.Zero))
            return Results.Problem(statusCode:400,title:"Invalid claim history cursor or identity");
        var attendance=occurrenceId.HasValue;
        bool allowed;
        try { allowed=await auth.IsAuthorizedAsync(user,centerId,
            attendance?"SESSION_ATTENDANCE_CLAIMS_READ":"SESSION_OCCURRENCE_CLAIMS_READ",sessionId,ct); }
        catch(OperationCanceledException) when(ct.IsCancellationRequested) { throw; }
        catch { allowed=false; }
        if(!allowed) return Results.Problem(statusCode:403,title:"Independent claim history read grant required");
        var url=config["CENTER_DRAFT_READ_DATABASE_URL"];
        if(string.IsNullOrWhiteSpace(url)) return Results.Problem(statusCode:503,title:"Claim history unavailable");
        try
        {
            await using var db=new NpgsqlConnection(url);
            await db.OpenAsync(ct);
            await using var tx=await db.BeginTransactionAsync(ct);
            if(!await RestrictedDraftDatabaseRole.VerifyReadOnlyAsync(db,tx,ct))
                return Results.Problem(statusCode:503,title:"Unsafe claim history SQL role");
            await using(var scope=new NpgsqlCommand("SELECT set_config('kanoonyar.center_id',@center,true)",db,tx))
            {
                scope.Parameters.AddWithValue("center",centerId);
                await scope.ExecuteNonQueryAsync(ct);
            }
            const string occurrencesSql="""
                SELECT o.occurrence_report_id,o.reported_note,o.evidence_ref,
                       o.claimed_started_at,o.claimed_ended_at,o.recorded_at
                FROM learning.session_occurrence_reports o
                WHERE o.center_id=@center AND o.session_id=@session
                  AND (@at::timestamptz IS NULL OR
                       (o.recorded_at,o.occurrence_report_id)<(@at::timestamptz,@id::uuid))
                  AND EXISTS(SELECT 1 FROM centers.memberships m
                    JOIN centers.registry c ON c.center_id=m.center_id
                    WHERE m.center_id=o.center_id AND m.subject_id=@actor
                    AND m.is_active AND c.is_active)
                ORDER BY o.recorded_at DESC,o.occurrence_report_id DESC LIMIT 21
                """;
            const string attendanceSql="""
                SELECT a.claim_id,a.participant_ref,a.reported_presence,a.recorded_at
                FROM learning.session_attendance_claims a
                JOIN learning.session_occurrence_reports o
                  ON o.center_id=a.center_id
                 AND o.occurrence_report_id=a.occurrence_report_id
                 AND o.session_id=a.session_id
                WHERE a.center_id=@center AND a.session_id=@session
                  AND a.occurrence_report_id=@occurrence
                  AND (@at::timestamptz IS NULL OR
                      (a.recorded_at,a.claim_id)<(@at::timestamptz,@id::uuid))
                  AND EXISTS(SELECT 1 FROM centers.memberships m
                    JOIN centers.registry c ON c.center_id=m.center_id
                    WHERE m.center_id=a.center_id AND m.subject_id=@actor
                    AND m.is_active AND c.is_active)
                ORDER BY a.recorded_at DESC,a.claim_id DESC LIMIT 21
                """;
            await using var command=new NpgsqlCommand(attendance?attendanceSql:occurrencesSql,db,tx);
            command.Parameters.AddWithValue("center",centerId);
            command.Parameters.AddWithValue("session",sessionId);
            command.Parameters.AddWithValue("actor",subject);
            command.Parameters.Add("at",NpgsqlDbType.TimestampTz).Value=
                beforeAt.HasValue?(object)beforeAt.Value.UtcDateTime:DBNull.Value;
            command.Parameters.Add("id",NpgsqlDbType.Uuid).Value=beforeId.HasValue?(object)beforeId.Value:DBNull.Value;
            if(attendance)command.Parameters.AddWithValue("occurrence",occurrenceId!.Value);
            var records=new List<object>();
            var count=0; DateTime? nextAt=null;Guid? nextId=null;
            await using(var reader=await command.ExecuteReaderAsync(ct))
            {
                while(await reader.ReadAsync(ct))
                {
                    var key=reader.GetGuid(0);
                    var time=reader.GetDateTime(attendance?3:5);
                    if(count<Limit)
                    {
                        nextAt=time;nextId=key;
                        if(attendance)records.Add(new{
                            claimId=key,participantRef=reader.GetString(1),
                            reportedPresence=reader.GetString(2),recordedAt=time,
                            verificationStatus="UNVERIFIED"});
                        else records.Add(new{
                            occurrenceReportId=key,reportedNote=reader.GetString(1),
                            evidenceRef=reader.IsDBNull(2)?null:reader.GetString(2),
                            claimedStartedAt=reader.IsDBNull(3)?(DateTime?)null:reader.GetDateTime(3),
                            claimedEndedAt=reader.IsDBNull(4)?(DateTime?)null:reader.GetDateTime(4),
                            recordedAt=time,verificationStatus="UNVERIFIED"});
                    }
                    count++;
                }
            }
            await tx.CommitAsync(ct);
            if(count<=Limit){nextAt=null;nextId=null;}
            return attendance
                ?Results.Ok(new{sessionId,occurrenceReportId=occurrenceId,
                    attendanceClaims=records,nextBeforeAt=nextAt,nextBeforeId=nextId})
                :Results.Ok(new{sessionId,occurrenceReports=records,
                    nextBeforeAt=nextAt,nextBeforeId=nextId});
        }
        catch(NpgsqlException){return Results.Problem(statusCode:503,title:"Claim history unavailable");}
        catch(TimeoutException){return Results.Problem(statusCode:503,title:"Claim history unavailable");}
    }
}
