using System.Security.Claims;
using Kanoonyar.Api.Programs;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Kanoonyar.Api.Tests;

public sealed class CenterSessionClaimWriteEndpointTests
{
    private static readonly IConfiguration EmptyStore = new ConfigurationBuilder().Build();
    private static readonly ICenterSessionClaimWriteAuthorization Grant = new TestGrant();
    private static ClaimsPrincipal Actor(params Claim[] claims) =>
        new(new ClaimsIdentity(claims, "test"));
    private static int? Code(IResult value) => (value as IStatusCodeHttpResult)?.StatusCode;

    [Fact]
    public async Task Occurrence_requires_identity_scoped_grant_and_unverified_valid_data()
    {
        var request = new CreateSessionOccurrenceClaimRequest(Guid.NewGuid(),"Reported",null,null,null);
        Assert.Equal(401, Code(await CenterSessionClaimWriteEndpoint.RecordOccurrence(
            new ClaimsPrincipal(),EmptyStore,Grant,"center-a",Guid.NewGuid(),request,default)));
        Assert.Equal(403, Code(await CenterSessionClaimWriteEndpoint.RecordOccurrence(
            Actor(new Claim("role","admin")),EmptyStore,Grant,"center-a",Guid.NewGuid(),request,default)));
        var actor=Actor(new Claim("sub","actor-a"));
        Assert.Equal(403, Code(await CenterSessionClaimWriteEndpoint.RecordOccurrence(
            actor,EmptyStore,new UnapprovedCenterSessionClaimWriteAuthorization(),
            "center-a",Guid.NewGuid(),request,default)));
        Assert.Equal(403, Code(await CenterSessionClaimWriteEndpoint.RecordOccurrence(
            actor,EmptyStore,new BrokenGrant(),"center-a",Guid.NewGuid(),request,default)));
        Assert.Equal(400, Code(await CenterSessionClaimWriteEndpoint.RecordOccurrence(
            actor,EmptyStore,Grant,"center-a",Guid.NewGuid(),
            request with {ReportedNote="  "},default)));
        Assert.Equal(400, Code(await CenterSessionClaimWriteEndpoint.RecordOccurrence(
            actor,EmptyStore,Grant,"center-a",Guid.NewGuid(),
            request with {ClaimedStartedAt=new DateTimeOffset(2026,10,12,10,0,0,TimeSpan.FromHours(4))},default)));
        Assert.Equal(400, Code(await CenterSessionClaimWriteEndpoint.RecordOccurrence(
            actor,EmptyStore,Grant,"center-a",Guid.NewGuid(),
            request with {ClaimedStartedAt=DateTimeOffset.UnixEpoch,
                          ClaimedEndedAt=DateTimeOffset.UnixEpoch.AddMinutes(-1)},default)));
        Assert.Equal(503, Code(await CenterSessionClaimWriteEndpoint.RecordOccurrence(
            actor,EmptyStore,Grant,"center-a",Guid.NewGuid(),request,default)));
    }

    [Fact]
    public async Task Attendance_never_infers_missing_values_or_accepts_verified_state()
    {
        var actor=Actor(new Claim("sub","actor-a"));
        var request=new CreateSessionAttendanceClaimRequest(
            Guid.NewGuid(),Guid.NewGuid(),"opaque-id","REPORTED_PRESENT");
        Assert.Equal(400, Code(await CenterSessionClaimWriteEndpoint.RecordAttendance(
            actor,EmptyStore,Grant,"center-a",Guid.NewGuid(),
            request with {ReportedPresence="VERIFIED_PRESENT"},default)));
        Assert.Equal(400, Code(await CenterSessionClaimWriteEndpoint.RecordAttendance(
            actor,EmptyStore,Grant,"center-a",Guid.NewGuid(),
            request with {ParticipantRef=" "},default)));
        Assert.Equal(400, Code(await CenterSessionClaimWriteEndpoint.RecordAttendance(
            actor,EmptyStore,Grant,"center-a",Guid.NewGuid(),
            request with {OccurrenceReportId=Guid.Empty},default)));
        Assert.Equal(403, Code(await CenterSessionClaimWriteEndpoint.RecordAttendance(
            actor,EmptyStore,new UnapprovedCenterSessionClaimWriteAuthorization(),
            "center-a",Guid.NewGuid(),request,default)));
        Assert.Equal(503, Code(await CenterSessionClaimWriteEndpoint.RecordAttendance(
            actor,EmptyStore,Grant,"center-a",Guid.NewGuid(),request,default)));
    }
    private sealed class TestGrant : ICenterSessionClaimWriteAuthorization
    {
        public ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal user,string center,string operation,
            Guid session,CancellationToken ct) => ValueTask.FromResult(true);
    }
    private sealed class BrokenGrant : ICenterSessionClaimWriteAuthorization
    {
        public ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal user,string center,string operation,
            Guid session,CancellationToken ct) => throw new InvalidOperationException("Grant read failed");
    }
}
