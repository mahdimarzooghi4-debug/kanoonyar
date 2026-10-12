using System.Security.Claims;
using Kanoonyar.Api.Programs;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Kanoonyar.Api.Tests;

public sealed class CenterSourcedActivityReportWriteEndpointTests
{
    private static readonly IConfiguration Empty=new ConfigurationBuilder().Build();
    private static ClaimsPrincipal Actor(params Claim[] claims)=>
        new(new ClaimsIdentity(claims,"synthetic"));
    private static int? Status(IResult r)=>(r as IStatusCodeHttpResult)?.StatusCode;
    private static CreateSourcedActivityReportRequest Report()=>
        new(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),"CLASS_SESSION",
            Guid.NewGuid(),"synthetic-evidence","Reported only",null);
    private static Task<IResult> Send(ClaimsPrincipal user,
        CreateSourcedActivityReportRequest? request=null,
        ICenterSourcedActivityReportWriteAuthorization? grant=null)=>
        CenterSourcedActivityReportWriteEndpoint.Record(user,Empty,
            grant??new UnapprovedCenterSourcedActivityReportWriteAuthorization(),
            "center-a",request??Report(),CancellationToken.None);

    [Fact]
    public async Task Write_requires_authenticated_actor_and_independent_permission()
    {
        Assert.Equal(401,Status(await Send(new ClaimsPrincipal())));
        Assert.Equal(403,Status(await Send(Actor(new Claim("role","admin")))));
        var user=Actor(new Claim("sub","actor-a"),new Claim("role","admin"));
        Assert.Equal(403,Status(await Send(user)));
        Assert.Equal(403,Status(await Send(user,grant:new ErrorGrant())));
        Assert.Equal(503,Status(await Send(user,grant:new TestGrant())));
    }

    [Fact]
    public async Task Rejects_invalid_source_plan_and_non_utc_report_without_mutation()
    {
        var user=Actor(new Claim("sub","actor-a"));
        var report=Report();
        Assert.Equal(400,Status(await Send(user,report with{ReportId=Guid.Empty},new TestGrant())));
        Assert.Equal(400,Status(await Send(user,report with{DraftId=Guid.Empty},new TestGrant())));
        Assert.Equal(400,Status(await Send(user,report with{LinkId=Guid.Empty},new TestGrant())));
        Assert.Equal(400,Status(await Send(user,report with{SourceId=Guid.Empty},new TestGrant())));
        Assert.Equal(400,Status(await Send(user,report with{SourceKind="UNKNOWN"},new TestGrant())));
        Assert.Equal(400,Status(await Send(user,report with{EvidenceRef=" "},new TestGrant())));
        Assert.Equal(400,Status(await Send(user,report with{ReportedSummary=""},new TestGrant())));
        Assert.Equal(400,Status(await Send(user,report with{
            ClaimedOccurredAt=new DateTimeOffset(2026,10,12,12,0,0,TimeSpan.FromHours(4))
        },new TestGrant())));
    }
    private sealed class TestGrant:ICenterSourcedActivityReportWriteAuthorization
    {
        public ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal p,string c,string k,Guid s,Guid d,
            CancellationToken ct)=>ValueTask.FromResult(true);
    }
    private sealed class ErrorGrant:ICenterSourcedActivityReportWriteAuthorization
    {
        public ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal p,string c,string k,Guid s,Guid d,
            CancellationToken ct)=>throw new InvalidOperationException("No authorization source");
    }
}
