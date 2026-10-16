using System.Security.Claims;
using System.Text.Json;
using Kanoonyar.Api.Programs;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Xunit;

namespace Kanoonyar.Api.Tests;

/// <summary>Disposable synthetic 80-center proof for isolated, cursor-paginated attendance claim reads.</summary>
public sealed class NationalEightyCenterLearningEvidenceIsolationTests
{
    private const int CenterCount = 80;
    private const int ClaimsPerCenter = 43;
    private static string? AdminUrl => Environment.GetEnvironmentVariable("CENTER_DRAFT_INTEGRATION_ADMIN_URL");
    private static string? ReadUrl => Environment.GetEnvironmentVariable("CENTER_DRAFT_INTEGRATION_READ_DATABASE_URL");
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private sealed record Fixture(string CenterId,string Subject,Guid ClassId,Guid SessionId,
        Guid OccurrenceId,Guid[] ClaimIds,string[] ParticipantRefs);

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
    private static Task<IResult> Attendance(Fixture f,IConfiguration c,string? actor=null,string? scope=null,
        DateTimeOffset? beforeAt=null,Guid? beforeId=null) =>
        CenterSessionClaimHistoryEndpoint.Attendance(Actor(actor??f.Subject),c,new SyntheticGrant(),
            scope??f.CenterId,f.SessionId,f.OccurrenceId,beforeAt,beforeId,CancellationToken.None);

    private static async Task<Fixture[]> Seed(string adminUrl) {
        var batch=Guid.NewGuid().ToString("N");
        var centers=Enumerable.Range(0,CenterCount).Select(i=> {
            var participantPrefix=$"synthetic-participant-{batch}-{i:D2}-";
            return new Fixture($"s87-center-{batch}-{i:D2}",$"s87-subject-{batch}-{i:D2}",
                Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),
                Enumerable.Range(0,ClaimsPerCenter).Select(_=>Guid.NewGuid()).ToArray(),
                Enumerable.Range(0,ClaimsPerCenter).Select(n=>$"{participantPrefix}{n:D2}").ToArray());
        }).ToArray();
        await using var db=new NpgsqlConnection(adminUrl);await db.OpenAsync();
        await using var tx=await db.BeginTransactionAsync();
        foreach(var f in centers) {
            await using(var source=new NpgsqlCommand("""
                INSERT INTO centers.registry(center_id,display_name,is_active,registered_by_subject,registered_at)
                VALUES(@center,'Synthetic S87 fixture',true,'s87-fixture',now());
                INSERT INTO centers.memberships(center_id,subject_id,role_code,is_active,recorded_by_subject,recorded_at)
                VALUES(@center,@actor,'synthetic-s87-fixture',true,'s87-fixture',now());
                INSERT INTO learning.center_classes(center_id,class_id,title)
                VALUES(@center,@class,'Synthetic S87 source class');
                INSERT INTO learning.class_sessions(center_id,session_id,class_id,session_title,scheduled_at)
                VALUES(@center,@session,@class,'Synthetic S87 source session',NULL);
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
                  'synthetic:s87-evidence',NULL,NULL,@actor,@audit,@event,@correlation)
                """,db,tx)) {
                occurrence.Parameters.AddWithValue("center",f.CenterId);occurrence.Parameters.AddWithValue("occurrence",f.OccurrenceId);
                occurrence.Parameters.AddWithValue("session",f.SessionId);occurrence.Parameters.AddWithValue("actor",f.Subject);
                occurrence.Parameters.AddWithValue("audit",Guid.NewGuid());occurrence.Parameters.AddWithValue("event",Guid.NewGuid());
                occurrence.Parameters.AddWithValue("correlation",Guid.NewGuid().ToString("N"));
                await occurrence.ExecuteNonQueryAsync();
            }
            await using(var attendance=new NpgsqlCommand("""
                SELECT learning.record_session_attendance_claim(
                  @center,input.claim_id,@occurrence,@session,input.participant_ref,'REPORTED_PRESENT',
                  @actor,gen_random_uuid(),gen_random_uuid(),@correlation||input.ordinality::text)
                FROM unnest(@claimIds::uuid[],@participantRefs::text[]) WITH ORDINALITY
                  AS input(claim_id,participant_ref,ordinality)
                """,db,tx)) {
                attendance.Parameters.AddWithValue("center",f.CenterId);
                attendance.Parameters.AddWithValue("claimIds",f.ClaimIds);
                attendance.Parameters.AddWithValue("participantRefs",f.ParticipantRefs);
                attendance.Parameters.AddWithValue("occurrence",f.OccurrenceId);attendance.Parameters.AddWithValue("session",f.SessionId);
                attendance.Parameters.AddWithValue("actor",f.Subject);
                attendance.Parameters.AddWithValue("correlation",Guid.NewGuid().ToString("N"));
                await attendance.ExecuteNonQueryAsync();
            }
        }
        var dualSubject=$"s87-dual-subject-{batch}";
        await using(var dual=new NpgsqlCommand("""
            INSERT INTO centers.memberships(center_id,subject_id,role_code,is_active,recorded_by_subject,recorded_at)
            VALUES(@first,@actor,'synthetic-s87-fixture',true,'s87-fixture',now()),
                  (@second,@actor,'synthetic-s87-fixture',true,'s87-fixture',now())
            """,db,tx)) {
            dual.Parameters.AddWithValue("first",centers[0].CenterId);dual.Parameters.AddWithValue("second",centers[1].CenterId);
            dual.Parameters.AddWithValue("actor",dualSubject);await dual.ExecuteNonQueryAsync();
        }
        await tx.CommitAsync();return centers;
    }

    [Fact]
    public async Task Eighty_synthetic_centers_keep_paginated_attendance_history_isolated() {
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
            var centerClaimIds=new HashSet<Guid>();
            var centerParticipantRefs=new HashSet<string>(StringComparer.Ordinal);
            var pageSizes=new List<int>();
            DateTimeOffset? beforeAt=null;Guid? beforeId=null;
            DateTimeOffset? previousAt=null;Guid? previousId=null;
            while(true) {
                var page=Body(await Attendance(f,config,beforeAt:beforeAt,beforeId:beforeId));
                var attendance=page.GetProperty("attendanceClaims").EnumerateArray().ToArray();
                pageSizes.Add(attendance.Length);
                Assert.True(pageSizes.Count<=3);
                foreach(var claim in attendance) {
                    var claimId=claim.GetProperty("claimId").GetGuid();
                    var participantRef=claim.GetProperty("participantRef").GetString()!;
                    var recordedAt=claim.GetProperty("recordedAt").GetDateTimeOffset();
                    Assert.StartsWith($"synthetic-participant-{f.CenterId.Split('-')[2]}-{i:D2}-",participantRef,StringComparison.Ordinal);
                    Assert.Equal("REPORTED_PRESENT",claim.GetProperty("reportedPresence").GetString());
                    Assert.Equal("UNVERIFIED",claim.GetProperty("verificationStatus").GetString());
                    if(previousAt.HasValue) {
                        Assert.True(previousAt.Value>recordedAt || (previousAt.Value==recordedAt &&
                            StringComparer.Ordinal.Compare(previousId!.Value.ToString("N"),claimId.ToString("N"))>0));
                    }
                    previousAt=recordedAt;previousId=claimId;
                    Assert.True(centerClaimIds.Add(claimId));Assert.True(claimIds.Add(claimId));
                    Assert.True(centerParticipantRefs.Add(participantRef));
                }
                var nextAt=page.GetProperty("nextBeforeAt");var nextId=page.GetProperty("nextBeforeId");
                if(nextAt.ValueKind==JsonValueKind.Null) { Assert.Equal(JsonValueKind.Null,nextId.ValueKind);break; }
                Assert.NotEmpty(attendance);
                Assert.Equal(attendance[^1].GetProperty("recordedAt").GetDateTimeOffset(),nextAt.GetDateTimeOffset());
                Assert.Equal(attendance[^1].GetProperty("claimId").GetGuid(),nextId.GetGuid());
                beforeAt=nextAt.GetDateTimeOffset();beforeId=nextId.GetGuid();
            }
            Assert.Equal(new[]{20,20,3},pageSizes);
            Assert.True(f.ClaimIds.ToHashSet().SetEquals(centerClaimIds));
            Assert.Equal(ClaimsPerCenter,centerParticipantRefs.Count);
            var neighbor=centers[(i+1)%centers.Length];
            Assert.Empty(Body(await Occurrences(f,config,scope:neighbor.CenterId)).GetProperty("occurrenceReports").EnumerateArray());
            Assert.Empty(Body(await Attendance(f,config,scope:neighbor.CenterId)).GetProperty("attendanceClaims").EnumerateArray());
        }
        Assert.Equal(CenterCount,occurrenceIds.Count);Assert.Equal(CenterCount*ClaimsPerCenter,claimIds.Count);
        var dualSubject=$"s87-dual-subject-{centers[0].CenterId.Split('-')[2]}";
        foreach(var f in centers.Take(2)) {
            var occurrence=Body(await Occurrences(f,config,dualSubject)).GetProperty("occurrenceReports").EnumerateArray().ToArray();
            Assert.Single(occurrence);Assert.Equal(f.OccurrenceId,occurrence[0].GetProperty("occurrenceReportId").GetGuid());
            var attendance=Body(await Attendance(f,config,dualSubject)).GetProperty("attendanceClaims").EnumerateArray().ToArray();
            Assert.Equal(20,attendance.Length);
            var expectedRefs=f.ParticipantRefs.ToHashSet(StringComparer.Ordinal);
            Assert.All(attendance,claim=>Assert.Contains(claim.GetProperty("participantRef").GetString()!,expectedRefs));
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
        Assert.Equal(20,Body(await Attendance(centers[12],config)).GetProperty("attendanceClaims").GetArrayLength());
    }
}
