using System.Security.Claims;
using Kanoonyar.Api.Programs;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Kanoonyar.Api.Tests;
public sealed class CenterSessionClaimHistoryEndpointTests
{
    private static readonly IConfiguration Missing=new ConfigurationBuilder().Build();
    private static ClaimsPrincipal Actor(params Claim[] claims)=>
        new(new ClaimsIdentity(claims,"fixture"));
    private static int? Status(IResult r)=>(r as IStatusCodeHttpResult)?.StatusCode;

    [Fact]
    public async Task Occurrence_history_requires_actor_independent_grant_and_complete_cursor()
    {
        var session=Guid.NewGuid();
        var user=Actor(new Claim("sub","fixture"));
        Assert.Equal(401,Status(await CenterSessionClaimHistoryEndpoint.Occurrences(
            new ClaimsPrincipal(),Missing,new TestGrant(),"c",session,null,null,default)));
        Assert.Equal(403,Status(await CenterSessionClaimHistoryEndpoint.Occurrences(
            Actor(new Claim("role","admin")),Missing,new TestGrant(),"c",session,null,null,default)));
        Assert.Equal(403,Status(await CenterSessionClaimHistoryEndpoint.Occurrences(
            user,Missing,new UnapprovedCenterSessionClaimHistoryAuthorization(),
            "c",session,null,null,default)));
        Assert.Equal(403,Status(await CenterSessionClaimHistoryEndpoint.Occurrences(
            user,Missing,new BrokenGrant(),"c",session,null,null,default)));
        Assert.Equal(400,Status(await CenterSessionClaimHistoryEndpoint.Occurrences(
            user,Missing,new TestGrant(),"c",Guid.Empty,null,null,default)));
        Assert.Equal(400,Status(await CenterSessionClaimHistoryEndpoint.Occurrences(
            user,Missing,new TestGrant(),"c",session,DateTimeOffset.UtcNow,null,default)));
        Assert.Equal(400,Status(await CenterSessionClaimHistoryEndpoint.Occurrences(
            user,Missing,new TestGrant(),"c",session,null,Guid.NewGuid(),default)));
        Assert.Equal(400,Status(await CenterSessionClaimHistoryEndpoint.Occurrences(
            user,Missing,new TestGrant(),"c",session,DateTimeOffset.UtcNow,Guid.Empty,default)));
        Assert.Equal(503,Status(await CenterSessionClaimHistoryEndpoint.Occurrences(
            user,Missing,new TestGrant(),"c",session,null,null,default)));
    }

    [Fact]
    public async Task Attendance_is_separately_granted_and_does_not_accept_wrong_identity()
    {
        var user=Actor(new Claim("sub","fixture"));
        var session=Guid.NewGuid();var report=Guid.NewGuid();
        Assert.Equal(403,Status(await CenterSessionClaimHistoryEndpoint.Attendance(
            user,Missing,new OccurrenceOnlyGrant(),"c",session,report,null,null,default)));
        Assert.Equal(400,Status(await CenterSessionClaimHistoryEndpoint.Attendance(
            user,Missing,new TestGrant(),"c",session,Guid.Empty,null,null,default)));
        Assert.Equal(503,Status(await CenterSessionClaimHistoryEndpoint.Attendance(
            user,Missing,new TestGrant(),"c",session,report,null,null,default)));
    }
    private sealed class TestGrant:ICenterSessionClaimHistoryAuthorization
    {
        public ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal actor,string c,string op,
            Guid s,CancellationToken ct)=>ValueTask.FromResult(true);
    }
    private sealed class OccurrenceOnlyGrant:ICenterSessionClaimHistoryAuthorization
    {
        public ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal actor,string c,string op,
            Guid s,CancellationToken ct)=>ValueTask.FromResult(op=="SESSION_OCCURRENCE_CLAIMS_READ");
    }
    private sealed class BrokenGrant:ICenterSessionClaimHistoryAuthorization
    {
        public ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal actor,string c,string op,
            Guid s,CancellationToken ct)=>throw new InvalidOperationException("Grant store unavailable");
    }
}
