using System.Security.Claims;
using System.Text.Json;
using Kanoonyar.Api.Programs;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Xunit;

namespace Kanoonyar.Api.Tests;

/// <summary>Disposable synthetic 80-center proof for occurrence and attendance claim reads.</summary>
public sealed class NationalEightyCenterLearningEvidenceIsolationTests
{
    private const int CenterCount = 80;
    private static string? AdminUrl => Environment.GetEnvironmentVariable("CENTER_DRAFT_INTEGRATION_ADMIN_URL");
    private static string? ReadUrl => Environment.GetEnvironmentVariable("CENTER_DRAFT_INTEGRATION_READ_DATABASE_URL");
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private sealed record Fixture(string CenterId,string Subject,Guid ClassId,Guid SessionId,
        Guid OccurrenceId,Guid ClaimId,string ParticipantRef);

    private sealed class SyntheticGrant : ICenterSessionClaimHistoryAuthorization
    {
        public ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal actor,string centerId,
            string operation,Guid sessionId,CancellationToken ct) =>
            ValueTask.FromResult(operation is "SESSION_OCCURRENCE_CLAIMS_READ" or "SESSION_ATTENDANCE_CLAIMS_READ");
    }
    private static ClaimsPrincipal Actor(string subject) =>
        new(new ClaimsIdentity([new Claim("sub",subject)],"synthetic-80-center-test"));
    private static IConfiguration Config(string readUrl) =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> {
            ["CENTER_DRAFT_READ_DATABASE_URL"]=readUrl
        }).Build();
    private static int? Status(IResult r)=>(r as IStatusCodeHttpResult)?.StatusCode;
    private static JsonElement Body(IResult r) {
        Assert.Equal(200,Status(r));
        using var json=JsonDocument.Parse(JsonSerializer.Serialize(
            Assert.IsAssignableFrom<IValueHttpResult>(r).Value,JsonOptions));
        return json.RootElement.Clone();
    }
    private static Task<IResult> Occurrences(Fixture f,IConfiguration c,string? actor=null,string? scope=null) =>
        CenterSessionClaimHistoryEndpoint.Occurrences(Actor(actor??f.Subject),c,new SyntheticGrant(),
            scope??f.CenterId,f.SessionId,null,null,CancellationToken.None);
    private static Task<IResult> Attendance(Fixture f,IConfiguration c,string? actor=null,string? scope=null) =>
        CenterSessionClaimHistoryEndpoint.Attendance(Actor(actor??f.Subject),c,new SyntheticGrant(),
            scope??f.CenterId,f.SessionId,f.OccurrenceId,null,null,CancellationToken.None);

    private static async Task<Fixture[]> Seed(string adminUrl) {
        var batch=Guid.NewGuid().ToString("N");
        var centers=Enumerable.Range(0,CenterCount).Select(i=>new Fixture(
            $"s86-center-{batch}-{i:D2}",$"s86-subject-{batch}-{i:D2}",
            Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),
            $"synthetic-participant-{batch}-{i:D2}")).ToArray();
        await using var db=new NpgsqlConnection(adminUrl);await db.OpenAsync();
        await using var tx=await db.BeginTransactionAsync();
        foreach(var f in centers) {
            await using(var source=new NpgsqlCommand("""
                INSERT INTO centers.registry(center_id,display_name,is_active,registered_by_subject,registered_at)
                VALUES(@center,'Synthetic S86 fixture',true,'s86-fixture',now());
                INSERT INTO centers.memberships(center_id,subject_id,role_code,is_active,recorded_by_subject,recorded_at)
                VALUES(@center,@actor,'synthetic-s86-fixture',true,'s86-fixture',now());
                INSERT INTO learning.center_classes(center_id,class_id,title)
                VALUES(@center,@class,'Synthetic S86 source class');
                INSERT INTO learning.class_sessions(center_id,session_id,class_id,session_title,scheduled_at)
                VALUES(@center,@session,@class,'Synthetic S86 source session',NULL);
                """,db,tx)) {
                source.Parameters.AddWithValue("center",f.CenterId);source.Parameters.AddWithValue("actor",f.Subject);
                source.Parameters.AddWithValue("class",f.ClassId);source.Parameters.AddWithValue("session",f.SessionId);
                await source.ExecuteNonQueryAsync();
            }
            await using(var scope=new NpgsqlCommand("SELECT set_config('kanoonyar.center_id',@center,true)",db,tx)) {
                scope.Parameters.AddWithValue("center",f.CenterId);await scope.ExecuteNonQueryAsync();
            }
            await using(var occurrence=new NpgsqlCommand("""
                SELECT learning.record_session_occurrence_claim(
                  @center,@occurrence,@session,'Synthetic unverified occurrence claim',
                  'synthetic:s86-evidence',NULL,NULL,@actor,@audit,@event,@correlation)
                """,db,tx)) {
                occurrence.Parameters.AddWithValue("center",f.CenterId);occurrence.Parameters.AddWithValue("occurrence",f.OccurrenceId);
                occurrence.Parameters.AddWithValue("session",f.SessionId);occurrence.Parameters.AddWithValue("actor",f.Subject);
                occurrence.Parameters.AddWithValue("audit",Guid.NewGuid());occurrence.Parameters.AddWithValue("event",Guid.NewGuid());
                occurrence.Parameters.AddWithValue("correlation",Guid.NewGuid().ToString("N"));
                await occurrence.ExecuteNonQueryAsync();
            }
            await using(var attendance=new NpgsqlCommand("""
                SELECT learning.record_session_attendance_claim(
                  @center,@claim,@occurrence,@session,@participant,'REPORTED_PRESENT',
                  @actor,@audit,@event,@correlation)
                """,db,tx)) {
                attendance.Parameters.AddWithValue("center",f.CenterId);attendance.Parameters.AddWithValue("claim",f.ClaimId);
                attendance.Parameters.AddWithValue("occurrence",f.OccurrenceId);attendance.Parameters.AddWithValue("session",f.SessionId);
                attendance.Parameters.AddWithValue("participant",f.ParticipantRef);attendance.Parameters.AddWithValue("actor",f.Subject);
                attendance.Parameters.AddWithValue("audit",Guid.NewGuid());attendance.Parameters.AddWithValue("event",Guid.NewGuid());
                attendance.Parameters.AddWithValue("correlation",Guid.NewGuid().ToString("N"));
                await attendance.ExecuteNonQueryAsync();
            }
        }
        var dualSubject=$"s86-dual-subject-{batch}";
        await using(var dual=new NpgsqlCommand("""
            INSERT INTO centers.memberships(center_id,subject_id,role_code,is_active,recorded_by_subject,recorded_at)
            VALUES(@first,@actor,'synthetic-s86-fixture',true,'s86-fixture',now()),
                  (@second,@actor,'synthetic-s86-fixture',true,'s86-fixture',now())
            """,db,tx)) {
            dual.Parameters.AddWithValue("first",centers[0].CenterId);dual.Parameters.AddWithValue("second",centers[1].CenterId);
            dual.Parameters.AddWithValue("actor",dualSubject);await dual.ExecuteNonQueryAsync();
        }
        await tx.CommitAsync();return centers;
    }

    [Fact]
    public async Task Eighty_synthetic_centers_keep_occurrence_and_attendance_history_isolated() {
        var required=Environment.GetEnvironmentVariable("CENTER_DRAFT_INTEGRATION_REQUIRED")=="1";
        if(required){Assert.False(string.IsNullOrWhiteSpace(AdminUrl));Assert.False(string.IsNullOrWhiteSpace(ReadUrl));}
        if(string.IsNullOrWhiteSpace(AdminUrl)||string.IsNullOrWhiteSpace(ReadUrl))return;
        var centers=await Seed(AdminUrl);var config=Config(ReadUrl);
        var occurrenceIds=new HashSet<Guid>();var claimIds=new HashSet<Guid>();
        for(var i=0;i<centers.Length;i++) {
            var f=centers[i];
            var occurrence=Body(await Occurrences(f,config)).GetProperty("occurrenceReports").EnumerateArray().ToArray();
            Assert.Single(occurrence);Assert.Equal(f.OccurrenceId,occurrence[0].GetProperty("occurrenceReportId").GetGuid());
            Assert.Equal("Synthetic unverified occurrence claim",occurrence[0].GetProperty("reportedNote").GetString());
            Assert.Equal("UNVERIFIED",occurrence[0].GetProperty("verificationStatus").GetString());
            Assert.True(occurrenceIds.Add(f.OccurrenceId));
            var attendance=Body(await Attendance(f,config)).GetProperty("attendanceClaims").EnumerateArray().ToArray();
            Assert.Single(attendance);Assert.Equal(f.ClaimId,attendance[0].GetProperty("claimId").GetGuid());
            Assert.Equal(f.ParticipantRef,attendance[0].GetProperty("participantRef").GetString());
            Assert.Equal("REPORTED_PRESENT",attendance[0].GetProperty("reportedPresence").GetString());
            Assert.Equal("UNVERIFIED",attendance[0].GetProperty("verificationStatus").GetString());
            Assert.True(claimIds.Add(f.ClaimId));
            var neighbor=centers[(i+1)%centers.Length];
            Assert.Empty(Body(await Occurrences(f,config,scope:neighbor.CenterId)).GetProperty("occurrenceReports").EnumerateArray());
            Assert.Empty(Body(await Attendance(f,config,scope:neighbor.CenterId)).GetProperty("attendanceClaims").EnumerateArray());
        }
        Assert.Equal(CenterCount,occurrenceIds.Count);Assert.Equal(CenterCount,claimIds.Count);
        var dualSubject=$"s86-dual-subject-{centers[0].CenterId.Split('-')[2]}";
        foreach(var f in centers.Take(2)) {
            var occurrence=Body(await Occurrences(f,config,dualSubject)).GetProperty("occurrenceReports").EnumerateArray().ToArray();
            Assert.Single(occurrence);Assert.Equal(f.OccurrenceId,occurrence[0].GetProperty("occurrenceReportId").GetGuid());
            var attendance=Body(await Attendance(f,config,dualSubject)).GetProperty("attendanceClaims").EnumerateArray().ToArray();
            Assert.Single(attendance);Assert.Equal(f.ClaimId,attendance[0].GetProperty("claimId").GetGuid());
        }
        Assert.Empty(Body(await Occurrences(centers[2],config,dualSubject)).GetProperty("occurrenceReports").EnumerateArray());
        Assert.Empty(Body(await Attendance(centers[2],config,dualSubject)).GetProperty("attendanceClaims").EnumerateArray());

        await using(var db=new NpgsqlConnection(AdminUrl)) {
            await db.OpenAsync();await using var command=new NpgsqlCommand("""
                UPDATE centers.memberships SET is_active=false
                WHERE center_id=@revokedCenter AND subject_id=@actor;
                UPDATE centers.registry SET is_active=false WHERE center_id=@inactiveCenter;
                """,db);
            command.Parameters.AddWithValue("revokedCenter",centers[10].CenterId);
            command.Parameters.AddWithValue("actor",centers[10].Subject);
            command.Parameters.AddWithValue("inactiveCenter",centers[11].CenterId);
            await command.ExecuteNonQueryAsync();
        }
        foreach(var f in centers.Skip(10).Take(2)) {
            Assert.Empty(Body(await Occurrences(f,config)).GetProperty("occurrenceReports").EnumerateArray());
            Assert.Empty(Body(await Attendance(f,config)).GetProperty("attendanceClaims").EnumerateArray());
        }
        Assert.Single(Body(await Occurrences(centers[12],config)).GetProperty("occurrenceReports").EnumerateArray());
        Assert.Single(Body(await Attendance(centers[12],config)).GetProperty("attendanceClaims").EnumerateArray());
    }
}
