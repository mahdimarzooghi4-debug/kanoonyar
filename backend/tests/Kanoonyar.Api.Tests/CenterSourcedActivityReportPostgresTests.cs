using System.Security.Claims;
using System.Text.Json;
using Kanoonyar.Api.Programs;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Xunit;

namespace Kanoonyar.Api.Tests;

/// <summary>Disposable PostgreSQL; granted actor + writer are synthetic test-only.</summary>
public sealed class CenterSourcedActivityReportPostgresTests
{
    private static string? Admin=>Environment.GetEnvironmentVariable("CENTER_DRAFT_INTEGRATION_ADMIN_URL");
    private static string? Draft=>Environment.GetEnvironmentVariable("CENTER_DRAFT_INTEGRATION_DATABASE_URL");
    private static string? Read=>Environment.GetEnvironmentVariable("CENTER_DRAFT_INTEGRATION_READ_DATABASE_URL");
    private static string? Write=>Environment.GetEnvironmentVariable("CENTER_ACTIVITY_REPORT_INTEGRATION_DATABASE_URL");
    private static bool Enabled()
    {
        if(Environment.GetEnvironmentVariable("CENTER_DRAFT_INTEGRATION_REQUIRED")=="1")
        {
            Assert.False(string.IsNullOrWhiteSpace(Admin));
            Assert.False(string.IsNullOrWhiteSpace(Draft));
            Assert.False(string.IsNullOrWhiteSpace(Read));
            Assert.False(string.IsNullOrWhiteSpace(Write));
        }
        return !string.IsNullOrWhiteSpace(Admin)&&!string.IsNullOrWhiteSpace(Draft)
            &&!string.IsNullOrWhiteSpace(Read)&&!string.IsNullOrWhiteSpace(Write);
    }
    private static IConfiguration Config(string? reportWrite=null)=>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{
            ["CENTER_DRAFT_WRITE_DATABASE_URL"]=Draft,
            ["CENTER_DRAFT_READ_DATABASE_URL"]=Read,
            ["CENTER_ACTIVITY_REPORT_WRITE_DATABASE_URL"]=reportWrite??Write
        }).Build();
    private static ClaimsPrincipal Actor(string sub)=>
        new(new ClaimsIdentity([new Claim("sub",sub)],"fixture"));
    private static int? Status(IResult r)=>(r as IStatusCodeHttpResult)?.StatusCode;
    private sealed class WriteGrant:ICenterSourcedActivityReportWriteAuthorization
    {
        public ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal p,string c,string k,Guid s,
            Guid d,CancellationToken ct)=>ValueTask.FromResult(true);
    }
    private sealed class DraftGrant:ILocalPlanDraftWriteAuthorization
    {
        public ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal p,string c,Guid d,
            CancellationToken ct)=>ValueTask.FromResult(true);
    }
    private sealed class DossierGrant:ICenterSessionReportLineageAuthorization
    {
        public ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal p,string c,Guid s,
            CancellationToken ct)=>ValueTask.FromResult(true);
    }

    [Fact]
    public async Task Restricted_command_creates_source_bound_reports_without_cross_center_replay()
    {
        if(!Enabled())return;
        var draft=Guid.NewGuid();var link=Guid.NewGuid();var issuance=Guid.NewGuid();
        var session=Guid.NewGuid();var classId=Guid.NewGuid();var eventId=Guid.NewGuid();
        var report=Guid.NewGuid();var other=Guid.NewGuid();
        Assert.Equal(200,Status(await LocalPlanDraftWriteEndpoint.Append(
            Actor("actor-a"),Config(),new DraftGrant(),"center-a",
            new LocalPlanDraftRevisionRequest(draft,0,"national-1",1,"S17",["Synthetic"]),
            CancellationToken.None)));
        await using(var admin=new NpgsqlConnection(Admin))
        {
            await admin.OpenAsync();
            await using(var setup=new NpgsqlCommand(
                """
                INSERT INTO learning.center_classes(center_id,class_id,title)
                VALUES('center-a',@class,'Synthetic class');
                INSERT INTO learning.class_sessions(center_id,session_id,class_id,session_title)
                VALUES('center-a',@session,@class,'Synthetic session');
                INSERT INTO events.center_events(center_id,event_id,event_title)
                VALUES('center-a',@event,'Synthetic event');
                """,admin))
            {
                setup.Parameters.AddWithValue("class",classId);
                setup.Parameters.AddWithValue("session",session);
                setup.Parameters.AddWithValue("event",eventId);
                await setup.ExecuteNonQueryAsync();
            }
            await using(var issue=new NpgsqlCommand(
                """
                INSERT INTO programs.national_program_issuances
                  (issuance_id,center_id,program_id,national_version,issued_by_subject,issued_at)
                VALUES(@issue,'center-a','national-1',1,'fixture',now())
                ON CONFLICT(center_id,program_id,national_version) DO NOTHING;
                SELECT issuance_id FROM programs.national_program_issuances
                WHERE center_id='center-a' AND program_id='national-1' AND national_version=1;
                """,admin))
            {
                issue.Parameters.AddWithValue("issue",issuance);
                issuance=Assert.IsType<Guid>(await issue.ExecuteScalarAsync());
            }
            await using(var binding=new NpgsqlCommand(
                """
                SELECT programs.record_center_plan_issuance_link(
                @link,'center-a',@draft,1,@issuance,'national-1',1,
                'fixture',@audit,@event,'s17-http-test')
                """,admin))
            {
                binding.Parameters.AddWithValue("link",link);
                binding.Parameters.AddWithValue("draft",draft);
                binding.Parameters.AddWithValue("issuance",issuance);
                binding.Parameters.AddWithValue("audit",Guid.NewGuid());
                binding.Parameters.AddWithValue("event",Guid.NewGuid());
                Assert.Equal(link,await binding.ExecuteScalarAsync());
            }
        }
        var payload=new CreateSourcedActivityReportRequest(report,draft,link,"CLASS_SESSION",
            session,"synthetic-report-evidence","Unverified synthetic report",null);
        async Task<IResult> Send(CreateSourcedActivityReportRequest req,string actor="actor-a",
            string center="center-a",IConfiguration? config=null,
            ICenterSourcedActivityReportWriteAuthorization? grant=null)=>
            await CenterSourcedActivityReportWriteEndpoint.Record(
                Actor(actor),config??Config(),grant??new WriteGrant(),center,req,default);
        var result=await Send(payload);
        Assert.Equal(200,Status(result));
        using(var doc=JsonDocument.Parse(JsonSerializer.Serialize(
            Assert.IsAssignableFrom<IValueHttpResult>(result).Value)))
        {
            Assert.Equal(report,doc.RootElement.GetProperty("reportId").GetGuid());
            Assert.Equal("UNVERIFIED",doc.RootElement.GetProperty("verificationStatus").GetString());
        }
        Assert.Equal(200,Status(await Send(payload with{
            ReportId=other,SourceKind="CENTER_EVENT",SourceId=eventId,
            EvidenceRef="synthetic-other-evidence"
        })));
        Assert.Equal(409,Status(await Send(payload)));
        Assert.Equal(409,Status(await Send(payload with{ReportId=Guid.NewGuid()})));
        Assert.Equal(409,Status(await Send(payload with{ReportId=Guid.NewGuid(),
            SourceId=Guid.NewGuid(),EvidenceRef="missing-source"})));
        Assert.Equal(403,Status(await Send(payload with{ReportId=Guid.NewGuid(),
            EvidenceRef="revoked"},actor:"actor-revoked")));
        Assert.Equal(403,Status(await Send(payload with{ReportId=Guid.NewGuid(),
            EvidenceRef="foreign"},actor:"actor-b")));
        Assert.Equal(403,Status(await Send(payload with{ReportId=Guid.NewGuid(),
            EvidenceRef="denied"},grant:new UnapprovedCenterSourcedActivityReportWriteAuthorization())));
        foreach(var unsafeUrl in new[]{Admin,Draft,Read})
            Assert.Equal(503,Status(await Send(payload with{ReportId=Guid.NewGuid(),
                EvidenceRef="unsafe-sql"},config:Config(unsafeUrl))));
        var dossier=await CenterSessionReportLineageEndpoint.Get(
            Actor("actor-a"),Config(),new DossierGrant(),"center-a",session,null,default);
        Assert.Equal(200,Status(dossier));
        using(var doc=JsonDocument.Parse(JsonSerializer.Serialize(
            Assert.IsAssignableFrom<IValueHttpResult>(dossier).Value)))
        {
            Assert.Contains(doc.RootElement.GetProperty("activityReports").EnumerateArray(),r=>
                r.GetProperty("reportId").GetGuid()==report &&
                r.GetProperty("draftId").GetGuid()==draft &&
                r.GetProperty("issuanceId").GetGuid()==issuance &&
                r.GetProperty("verificationStatus").GetString()=="UNVERIFIED");
        }
        await using var db=new NpgsqlConnection(Admin);
        await db.OpenAsync();
        await using var count=new NpgsqlCommand(
            """
            SELECT
            (SELECT count(*) FROM programs.center_activity_reports WHERE report_id IN(@one,@two)),
            (SELECT count(*) FROM programs.activity_report_source_bindings WHERE report_id IN(@one,@two)),
            (SELECT count(*) FROM governance.command_audit WHERE target_id IN(@id1,@id2)),
            (SELECT count(*) FROM governance.outbox_events WHERE aggregate_id IN(@id1,@id2))
            """,db);
        count.Parameters.AddWithValue("one",report);
        count.Parameters.AddWithValue("two",other);
        count.Parameters.AddWithValue("id1",report.ToString("D"));
        count.Parameters.AddWithValue("id2",other.ToString("D"));
        await using var reader=await count.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        for(var i=0;i<4;i++)Assert.Equal(2L,reader.GetInt64(i));
    }
}
