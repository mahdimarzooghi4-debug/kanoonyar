using System.Security.Claims;
using Kanoonyar.Api.Security;
using Npgsql;
using NpgsqlTypes;

namespace Kanoonyar.Api.Programs;

/// <summary>Claims of occurrence/attendance, never verified delivery or enrollment.</summary>
public sealed record CreateSessionOccurrenceClaimRequest(
    Guid OccurrenceReportId, string ReportedNote, string? EvidenceRef,
    DateTimeOffset? ClaimedStartedAt, DateTimeOffset? ClaimedEndedAt);
public sealed record CreateSessionAttendanceClaimRequest(
    Guid ClaimId, Guid OccurrenceReportId,
    string ParticipantRef, string ReportedPresence);

public interface ICenterSessionClaimWriteAuthorization
{
    ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal principal, string centerId,
        string operation, Guid sessionId, CancellationToken cancellationToken);
}
public sealed class UnapprovedCenterSessionClaimWriteAuthorization :
    ICenterSessionClaimWriteAuthorization
{
    public ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal principal, string centerId,
        string operation, Guid sessionId, CancellationToken cancellationToken)
        => ValueTask.FromResult(false);
}

public static class CenterSessionClaimWriteEndpoint
{
    public static Task<IResult> RecordOccurrence(
        ClaimsPrincipal principal, IConfiguration config, ICenterSessionClaimWriteAuthorization grant,
        string centerId, Guid sessionId, CreateSessionOccurrenceClaimRequest? request,
        CancellationToken ct)
    {
        if (request is null || request.OccurrenceReportId == Guid.Empty ||
            string.IsNullOrWhiteSpace(request.ReportedNote) || request.ReportedNote.Length > 2048 ||
            (request.EvidenceRef is not null &&
             (string.IsNullOrWhiteSpace(request.EvidenceRef) || request.EvidenceRef.Length > 512)) ||
            (request.ClaimedStartedAt.HasValue && request.ClaimedStartedAt.Value.Offset != TimeSpan.Zero) ||
            (request.ClaimedEndedAt.HasValue && request.ClaimedEndedAt.Value.Offset != TimeSpan.Zero) ||
            (request.ClaimedStartedAt.HasValue && request.ClaimedEndedAt.HasValue &&
             request.ClaimedEndedAt < request.ClaimedStartedAt))
            return Task.FromResult<IResult>(Results.Problem(statusCode: 400,
                title: "Invalid unverified occurrence claim"));
        return Write(principal, config, grant, centerId, sessionId, "SESSION_OCCURRENCE_REPORT",
            request.OccurrenceReportId, Guid.Empty, request.ReportedNote.Trim(),
            request.EvidenceRef?.Trim(), request.ClaimedStartedAt,
            request.ClaimedEndedAt, null, null, ct);
    }

    public static Task<IResult> RecordAttendance(
        ClaimsPrincipal principal, IConfiguration config, ICenterSessionClaimWriteAuthorization grant,
        string centerId, Guid sessionId, CreateSessionAttendanceClaimRequest? request,
        CancellationToken ct)
    {
        if (request is null || request.ClaimId == Guid.Empty ||
            request.OccurrenceReportId == Guid.Empty ||
            string.IsNullOrWhiteSpace(request.ParticipantRef) ||
            request.ParticipantRef.Length > 256 ||
            request.ReportedPresence is not ("REPORTED_PRESENT" or "REPORTED_ABSENT"))
            return Task.FromResult<IResult>(Results.Problem(statusCode: 400,
                title: "Invalid unverified attendance claim"));
        return Write(principal, config, grant, centerId, sessionId, "SESSION_ATTENDANCE_REPORT",
            request.ClaimId, request.OccurrenceReportId, null, null, null, null,
            request.ParticipantRef.Trim(), request.ReportedPresence, ct);
    }

    private static async Task<IResult> Write(
        ClaimsPrincipal principal, IConfiguration config, ICenterSessionClaimWriteAuthorization grant,
        string centerId, Guid sessionId, string operation, Guid recordId, Guid occurrenceId,
        string? note, string? evidence, DateTimeOffset? started, DateTimeOffset? ended,
        string? participant, string? presence, CancellationToken ct)
    {
        if (principal.Identity?.IsAuthenticated != true)
            return Results.Problem(statusCode: 401, title: "Authentication required");
        var subject = principal.FindFirst("sub")?.Value;
        if (string.IsNullOrWhiteSpace(subject))
            return Results.Problem(statusCode: 403, title: "Verified subject required");
        if (string.IsNullOrWhiteSpace(centerId) || sessionId == Guid.Empty)
            return Results.Problem(statusCode: 400, title: "Invalid center or session");
        bool granted;
        try { granted = await grant.IsAuthorizedAsync(principal,centerId,operation,sessionId,ct); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch { granted = false; }
        if (!granted)
            return Results.Problem(statusCode: 403, title: "Independent session claim grant required");

        var url = config["CENTER_LEARNING_WRITE_DATABASE_URL"];
        if (string.IsNullOrWhiteSpace(url))
            return Results.Problem(statusCode: 503, title: "Session claim storage unavailable");
        try
        {
            await using var db = new NpgsqlConnection(url);
            await db.OpenAsync(ct);
            await using var tx = await db.BeginTransactionAsync(ct);
            if (!await RestrictedSessionClaimCommandRole.VerifyAsync(db,tx,ct))
                return Results.Problem(statusCode: 503, title: "Unsafe session-claim SQL role");
            await using(var scope = new NpgsqlCommand(
                "SELECT set_config('kanoonyar.center_id',@center,true)",db,tx))
            {
                scope.Parameters.AddWithValue("center",centerId);
                await scope.ExecuteNonQueryAsync(ct);
            }
            await using(var membership = new NpgsqlCommand(
                """
                SELECT EXISTS (SELECT 1 FROM centers.memberships m
                JOIN centers.registry c ON c.center_id=m.center_id
                WHERE m.center_id=@center AND m.subject_id=@actor
                AND m.is_active AND c.is_active)
                """,db,tx))
            {
                membership.Parameters.AddWithValue("center",centerId);
                membership.Parameters.AddWithValue("actor",subject);
                if(await membership.ExecuteScalarAsync(ct) is not true)
                    return Results.Problem(statusCode: 403, title: "Active center membership required");
            }
            var occurrence = operation == "SESSION_OCCURRENCE_REPORT";
            await using var command = new NpgsqlCommand(
                occurrence ?
                """
                SELECT learning.record_session_occurrence_claim(
                  @center,@record,@session,@note,@evidence,@started,@ended,
                  @actor,@audit,@event,@correlation)
                """ :
                """
                SELECT learning.record_session_attendance_claim(
                  @center,@record,@occurrence,@session,@participant,@presence,
                  @actor,@audit,@event,@correlation)
                """,db,tx);
            command.Parameters.AddWithValue("center",centerId);
            command.Parameters.AddWithValue("record",recordId);
            command.Parameters.AddWithValue("session",sessionId);
            command.Parameters.AddWithValue("actor",subject);
            command.Parameters.AddWithValue("audit",Guid.NewGuid());
            command.Parameters.AddWithValue("event",Guid.NewGuid());
            command.Parameters.AddWithValue("correlation",Guid.NewGuid().ToString("N"));
            if (occurrence)
            {
                command.Parameters.AddWithValue("note",note!);
                command.Parameters.Add("evidence",NpgsqlDbType.Text).Value =
                    evidence is null ? DBNull.Value : evidence;
                command.Parameters.Add("started",NpgsqlDbType.TimestampTz).Value =
                    started.HasValue ? (object)started.Value : DBNull.Value;
                command.Parameters.Add("ended",NpgsqlDbType.TimestampTz).Value =
                    ended.HasValue ? (object)ended.Value : DBNull.Value;
            }
            else
            {
                command.Parameters.AddWithValue("occurrence",occurrenceId);
                command.Parameters.AddWithValue("participant",participant!);
                command.Parameters.AddWithValue("presence",presence!);
            }
            if(await command.ExecuteScalarAsync(ct) is not Guid stored || stored != recordId)
                throw new InvalidOperationException("Unconfirmed stored claim identity");
            await tx.CommitAsync(ct);
            // A successful persistence acknowledgement does NOT confirm delivery or presence.
            return occurrence ?
                Results.Ok(new { occurrenceReportId=stored,sessionId,
                    verificationStatus="UNVERIFIED" }) :
                Results.Ok(new { claimId=stored,occurrenceReportId=occurrenceId,sessionId,
                    verificationStatus="UNVERIFIED" });
        }
        catch(PostgresException ex) when(ex.SqlState is "23505" or "23503" or "23514" or "40001")
        { return Results.Problem(statusCode:409,title:"Session claim identity or source conflict"); }
        catch(PostgresException ex) when(ex.SqlState=="42501" &&
            ex.MessageText=="Center scope or active membership absent")
        { return Results.Problem(statusCode:403,title:"Center membership or scope denied"); }
        catch(PostgresException ex) when(ex.SqlState=="42501")
        { return Results.Problem(statusCode:503,title:"Session claim database privileges insufficient"); }
        catch(PostgresException ex) when(ex.SqlState=="22023")
        { return Results.Problem(statusCode:400,title:"Invalid session claim request"); }
        catch(NpgsqlException)
        { return Results.Problem(statusCode:503,title:"Session claim storage unavailable"); }
        catch(TimeoutException)
        { return Results.Problem(statusCode:503,title:"Session claim storage unavailable"); }
    }
}
