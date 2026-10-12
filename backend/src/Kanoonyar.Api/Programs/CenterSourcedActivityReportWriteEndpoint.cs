using System.Security.Claims;
using Kanoonyar.Api.Security;
using Npgsql;
using NpgsqlTypes;

namespace Kanoonyar.Api.Programs;

public sealed record CreateSourcedActivityReportRequest(
    Guid ReportId, Guid DraftId, Guid LinkId, string SourceKind, Guid SourceId,
    string EvidenceRef, string ReportedSummary, DateTimeOffset? ClaimedOccurredAt);

/// <summary>Independently authenticated center/resource/operation grant; not JWT role.</summary>
public interface ICenterSourcedActivityReportWriteAuthorization
{
    ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal user, string centerId,
        string sourceKind, Guid sourceId, Guid draftId, CancellationToken ct);
}
public sealed class UnapprovedCenterSourcedActivityReportWriteAuthorization :
    ICenterSourcedActivityReportWriteAuthorization
{
    public ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal user, string centerId,
        string sourceKind, Guid sourceId, Guid draftId, CancellationToken ct) =>
        ValueTask.FromResult(false);
}

public static class CenterSourcedActivityReportWriteEndpoint
{
    public static async Task<IResult> Record(ClaimsPrincipal user,IConfiguration config,
        ICenterSourcedActivityReportWriteAuthorization permission,
        string centerId,CreateSourcedActivityReportRequest? request,CancellationToken ct)
    {
        if(user.Identity?.IsAuthenticated!=true)
            return Results.Problem(statusCode:401,title:"Authentication required");
        var subject=user.FindFirst("sub")?.Value;
        if(string.IsNullOrWhiteSpace(subject))
            return Results.Problem(statusCode:403,title:"Verified subject required");
        if(string.IsNullOrWhiteSpace(centerId)||request is null||
           request.ReportId==Guid.Empty||request.DraftId==Guid.Empty||
           request.LinkId==Guid.Empty||request.SourceId==Guid.Empty||
           request.SourceKind is not ("CLASS_SESSION" or "CENTER_EVENT")||
           string.IsNullOrWhiteSpace(request.EvidenceRef)||request.EvidenceRef.Length>512||
           string.IsNullOrWhiteSpace(request.ReportedSummary)||request.ReportedSummary.Length>2048||
           (request.ClaimedOccurredAt.HasValue && request.ClaimedOccurredAt.Value.Offset!=TimeSpan.Zero))
            return Results.Problem(statusCode:400,title:"Invalid unverified source activity report");
        bool allowed;
        try
        {
            allowed=await permission.IsAuthorizedAsync(user,centerId,
                request.SourceKind,request.SourceId,request.DraftId,ct);
        }
        catch(OperationCanceledException) when(ct.IsCancellationRequested){throw;}
        catch{allowed=false;}
        if(!allowed)
            return Results.Problem(statusCode:403,title:"Independent sourced report write grant required");

        var url=config["CENTER_ACTIVITY_REPORT_WRITE_DATABASE_URL"];
        if(string.IsNullOrWhiteSpace(url))
            return Results.Problem(statusCode:503,title:"Activity report command unavailable");
        try
        {
            await using var db=new NpgsqlConnection(url);
            await db.OpenAsync(ct);
            await using var tx=await db.BeginTransactionAsync(ct);
            if(!await RestrictedSourcedActivityReportRole.VerifyAsync(db,tx,ct))
                return Results.Problem(statusCode:503,title:"Unsafe sourced report SQL role");
            await using(var scope=new NpgsqlCommand(
                "SELECT set_config('kanoonyar.center_id',@center,true)",db,tx))
            {
                scope.Parameters.AddWithValue("center",centerId);
                await scope.ExecuteNonQueryAsync(ct);
            }
            await using(var live=new NpgsqlCommand(
                """
                SELECT EXISTS (
                  SELECT 1 FROM centers.memberships m
                  JOIN centers.registry c ON c.center_id=m.center_id
                  WHERE m.center_id=@center AND m.subject_id=@actor
                    AND m.is_active AND c.is_active)
                """,db,tx))
            {
                live.Parameters.AddWithValue("center",centerId);
                live.Parameters.AddWithValue("actor",subject);
                if(await live.ExecuteScalarAsync(ct) is not true)
                    return Results.Problem(statusCode:403,title:"Active center membership required");
            }
            await using var cmd=new NpgsqlCommand(
                """
                SELECT programs.record_center_sourced_activity_command(
                  @center,@report,@link,@draft,@kind,@source,@evidence,@summary,
                  @claimed,@actor,@audit,@event,@correlation)
                """,db,tx);
            cmd.Parameters.AddWithValue("center",centerId);
            cmd.Parameters.AddWithValue("report",request.ReportId);
            cmd.Parameters.AddWithValue("link",request.LinkId);
            cmd.Parameters.AddWithValue("draft",request.DraftId);
            cmd.Parameters.AddWithValue("kind",request.SourceKind);
            cmd.Parameters.AddWithValue("source",request.SourceId);
            cmd.Parameters.AddWithValue("evidence",request.EvidenceRef.Trim());
            cmd.Parameters.AddWithValue("summary",request.ReportedSummary.Trim());
            cmd.Parameters.Add("claimed",NpgsqlDbType.TimestampTz).Value=
                request.ClaimedOccurredAt.HasValue?
                  (object)request.ClaimedOccurredAt.Value:DBNull.Value;
            cmd.Parameters.AddWithValue("actor",subject);
            cmd.Parameters.AddWithValue("audit",Guid.NewGuid());
            cmd.Parameters.AddWithValue("event",Guid.NewGuid());
            cmd.Parameters.AddWithValue("correlation",Guid.NewGuid().ToString("N"));
            if(await cmd.ExecuteScalarAsync(ct) is not Guid saved||saved!=request.ReportId)
                throw new InvalidOperationException("Unconfirmed source report identity");
            await tx.CommitAsync(ct);
            return Results.Ok(new{
                reportId=saved,request.DraftId,request.LinkId,
                request.SourceKind,request.SourceId,verificationStatus="UNVERIFIED"
            });
        }
        catch(PostgresException ex) when(ex.SqlState is "23505" or "23503" or "23514" or "40001")
        {return Results.Problem(statusCode:409,title:"Duplicate report or mismatched source/plan binding");}
        catch(PostgresException ex) when(ex.SqlState=="42501"&&
            ex.MessageText=="Center scope or active membership absent")
        {return Results.Problem(statusCode:403,title:"Center membership or scope denied");}
        catch(PostgresException ex) when(ex.SqlState=="42501")
        {return Results.Problem(statusCode:503,title:"Report SQL privileges insufficient");}
        catch(PostgresException ex) when(ex.SqlState=="22023")
        {return Results.Problem(statusCode:400,title:"Invalid activity report command");}
        catch(NpgsqlException)
        {return Results.Problem(statusCode:503,title:"Activity report command unavailable");}
        catch(TimeoutException)
        {return Results.Problem(statusCode:503,title:"Activity report command unavailable");}
    }
}
