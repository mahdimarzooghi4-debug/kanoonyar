using System.Security.Claims;
using System.Text.Json;
using Kanoonyar.Api.Programs;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Xunit;

namespace Kanoonyar.Api.Tests;

/// <summary>Uses disposable PostgreSQL and a restricted read login; all writes are synthetic fixtures.</summary>
public sealed class CenterSessionReportLineagePostgresTests
{
    private static string? WriteUrl => Environment.GetEnvironmentVariable("CENTER_DRAFT_INTEGRATION_DATABASE_URL");
    private static string? ReadUrl => Environment.GetEnvironmentVariable("CENTER_DRAFT_INTEGRATION_READ_DATABASE_URL");
    private static string? AdminUrl => Environment.GetEnvironmentVariable("CENTER_DRAFT_INTEGRATION_ADMIN_URL");
    private static string? EscalationUrl => Environment.GetEnvironmentVariable("CENTER_DRAFT_INTEGRATION_ESCALATION_DATABASE_URL");
    private static bool Enabled()
    {
        if (Environment.GetEnvironmentVariable("CENTER_DRAFT_INTEGRATION_REQUIRED") == "1")
        {
            Assert.False(string.IsNullOrWhiteSpace(WriteUrl));
            Assert.False(string.IsNullOrWhiteSpace(ReadUrl));
            Assert.False(string.IsNullOrWhiteSpace(AdminUrl));
            Assert.False(string.IsNullOrWhiteSpace(EscalationUrl));
        }
        return !string.IsNullOrWhiteSpace(WriteUrl) && !string.IsNullOrWhiteSpace(ReadUrl) &&
            !string.IsNullOrWhiteSpace(AdminUrl) && !string.IsNullOrWhiteSpace(EscalationUrl);
    }
    private static IConfiguration Config(string? url = null) =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["CENTER_DRAFT_WRITE_DATABASE_URL"] = WriteUrl,
            ["CENTER_DRAFT_READ_DATABASE_URL"] = url ?? ReadUrl
        }).Build();
    private static ClaimsPrincipal Actor(string subject) =>
        new(new ClaimsIdentity([new Claim("sub",subject)], "synthetic-actor"));
    private static int? Status(IResult response) => (response as IStatusCodeHttpResult)?.StatusCode;
    private sealed class FixtureGrant : ICenterSessionReportLineageAuthorization
    {
        public ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal user, string centerId,
            Guid sessionId, CancellationToken ct) => ValueTask.FromResult(true);
    }
    private sealed class FixtureWrite : ILocalPlanDraftWriteAuthorization
    {
        public ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal user, string centerId,
            Guid draftId, CancellationToken ct) => ValueTask.FromResult(true);
    }

    [Fact]
    public async Task Exact_source_bound_reports_are_paged_and_isolated_from_other_centers_and_legacy_reports()
    {
        if (!Enabled()) return;
        var center = "center-a";
        var session = Guid.NewGuid();
        var unrelatedSession = Guid.NewGuid();
        var classId = Guid.NewGuid();
        var draft = Guid.NewGuid();
        var link = Guid.NewGuid();
        var issuance = Guid.NewGuid();
        var created = await LocalPlanDraftWriteEndpoint.Append(
            Actor("actor-a"), Config(), new FixtureWrite(), center,
            new LocalPlanDraftRevisionRequest(draft,0,"national-1",1,"Synthetic local plan",["Lesson"]),
            CancellationToken.None);
        Assert.Equal(200,Status(created));

        await using (var db = new NpgsqlConnection(AdminUrl))
        {
            await db.OpenAsync();
            await using (var source = new NpgsqlCommand(
                """
                INSERT INTO learning.center_classes(center_id,class_id,title)
                  VALUES('center-a',@class,'Synthetic course');
                INSERT INTO learning.class_sessions(center_id,session_id,class_id,session_title,scheduled_at)
                  VALUES('center-a',@session,@class,'Synthetic lesson',NULL),
                        ('center-a',@other,@class,'Unrelated planned lesson',NULL);
                """,db))
            {
                source.Parameters.AddWithValue("class",classId);
                source.Parameters.AddWithValue("session",session);
                source.Parameters.AddWithValue("other",unrelatedSession);
                await source.ExecuteNonQueryAsync();
            }
            await using (var issue = new NpgsqlCommand(
                """
                INSERT INTO programs.national_program_issuances(
                    issuance_id,center_id,program_id,national_version,issued_by_subject,issued_at)
                VALUES(@id,'center-a','national-1',1,'fixture',now())
                ON CONFLICT(center_id,program_id,national_version) DO NOTHING;
                SELECT issuance_id FROM programs.national_program_issuances
                WHERE center_id='center-a' AND program_id='national-1' AND national_version=1;
                """,db))
            {
                issue.Parameters.AddWithValue("id",issuance);
                issuance = Assert.IsType<Guid>(await issue.ExecuteScalarAsync());
            }
            await using (var attach = new NpgsqlCommand(
                """
                SELECT programs.record_center_plan_issuance_link(
                  @link,'center-a',@draft,1,@issuance,'national-1',1,
                  'fixture',@audit,@event,'s16-test-link')
                """,db))
            {
                attach.Parameters.AddWithValue("link",link);
                attach.Parameters.AddWithValue("draft",draft);
                attach.Parameters.AddWithValue("issuance",issuance);
                attach.Parameters.AddWithValue("audit",Guid.NewGuid());
                attach.Parameters.AddWithValue("event",Guid.NewGuid());
                Assert.Equal(link,await attach.ExecuteScalarAsync());
            }
            for(var i=0;i<22;i++)
            {
                await using var cmd = new NpgsqlCommand(
                    """
                    SELECT programs.record_center_sourced_activity_report(
                      @id,'center-a',@link,@draft,'CLASS_SESSION',@session,
                      @evidence,'Synthetic activity assertion',NULL,'fixture',
                      @audit,@event,'s16-test-activity')
                    """,db);
                cmd.Parameters.AddWithValue("id",Guid.NewGuid());
                cmd.Parameters.AddWithValue("link",link);
                cmd.Parameters.AddWithValue("draft",draft);
                cmd.Parameters.AddWithValue("session",session);
                cmd.Parameters.AddWithValue("evidence","synthetic-source-evidence-"+i);
                cmd.Parameters.AddWithValue("audit",Guid.NewGuid());
                cmd.Parameters.AddWithValue("event",Guid.NewGuid());
                Assert.IsType<Guid>(await cmd.ExecuteScalarAsync());
            }
            // An older unbound report is not silently upgraded to a typed owner linkage.
            await using var legacy = new NpgsqlCommand(
                """
                SELECT programs.record_center_activity_report(
                  @id,'center-a',@link,@draft,'CLASS_SESSION',@session,
                  'synthetic-legacy-evidence','Unbound synthetic legacy',NULL,'fixture',
                  @audit,@event,'s16-test-legacy')
                """,db);
            legacy.Parameters.AddWithValue("id",Guid.NewGuid());
            legacy.Parameters.AddWithValue("link",link);
            legacy.Parameters.AddWithValue("draft",draft);
            legacy.Parameters.AddWithValue("session",session.ToString("D"));
            legacy.Parameters.AddWithValue("audit",Guid.NewGuid());
            legacy.Parameters.AddWithValue("event",Guid.NewGuid());
            Assert.IsType<Guid>(await legacy.ExecuteScalarAsync());
        }

        async Task<IResult> Read(Guid sessionId, string actor="actor-a",
            string ownCenter="center-a",long? before=null,
            ICenterSessionReportLineageAuthorization? grant=null,IConfiguration? config=null) =>
            await CenterSessionReportLineageEndpoint.Get(Actor(actor),config??Config(),
                grant??new FixtureGrant(),ownCenter,sessionId,before,CancellationToken.None);
        (JsonElement Rows,long? Next) Page(IResult result)
        {
            Assert.Equal(200,Status(result));
            using var doc = JsonDocument.Parse(JsonSerializer.Serialize(
                Assert.IsAssignableFrom<IValueHttpResult>(result).Value));
            var root = doc.RootElement;
            Assert.Equal(session,root.GetProperty("sessionId").GetGuid());
            Assert.Equal(classId,root.GetProperty("classId").GetGuid());
            Assert.Equal("Synthetic course",root.GetProperty("classTitle").GetString());
            Assert.Equal(JsonValueKind.Null,root.GetProperty("scheduledAt").ValueKind);
            Assert.DoesNotContain("participantRef",root.GetRawText());
            var cursor = root.GetProperty("nextBeforeSequence");
            return (root.GetProperty("activityReports").Clone(),
                cursor.ValueKind==JsonValueKind.Null?null:cursor.GetInt64());
        }
        var first=Page(await Read(session));
        Assert.Equal(20,first.Rows.GetArrayLength());
        Assert.NotNull(first.Next);
        Assert.All(first.Rows.EnumerateArray(),r=>
        {
            Assert.Equal("UNVERIFIED",r.GetProperty("verificationStatus").GetString());
            Assert.Equal(draft,r.GetProperty("draftId").GetGuid());
            Assert.Equal(link,r.GetProperty("linkId").GetGuid());
            Assert.Equal(issuance,r.GetProperty("issuanceId").GetGuid());
            Assert.Equal(1,r.GetProperty("nationalVersion").GetInt32());
            Assert.Equal(1L,r.GetProperty("pinnedRevision").GetInt64());
            Assert.Equal(JsonValueKind.Null,r.GetProperty("claimedOccurredAt").ValueKind);
        });
        var second=Page(await Read(session,before:first.Next));
        Assert.Equal(2,second.Rows.GetArrayLength());
        Assert.Null(second.Next);
        Assert.True(first.Rows[19].GetProperty("reportSequence").GetInt64() >
            second.Rows[0].GetProperty("reportSequence").GetInt64());
        Assert.Equal(0, (await Read(unrelatedSession) as IValueHttpResult) is { Value: not null }
            ? JsonDocument.Parse(JsonSerializer.Serialize(
                ((IValueHttpResult)(await Read(unrelatedSession))).Value))
                .RootElement.GetProperty("activityReports").GetArrayLength() : -1);
        Assert.Equal(404,Status(await Read(session,actor:"actor-b")));
        Assert.Equal(404,Status(await Read(session,actor:"actor-revoked")));
        Assert.Equal(404,Status(await Read(session,ownCenter:"center-b")));
        Assert.Equal(403,Status(await Read(session,grant:new UnapprovedCenterSessionReportLineageAuthorization())));
        Assert.Equal(400,Status(await Read(session,before:0)));
        foreach(var unsafeConnection in new[]{AdminUrl,WriteUrl,EscalationUrl})
            Assert.Equal(503,Status(await Read(session,config:Config(unsafeConnection))));
    }
}
