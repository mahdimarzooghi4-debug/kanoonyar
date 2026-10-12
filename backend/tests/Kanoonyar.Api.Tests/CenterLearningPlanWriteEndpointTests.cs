using System.Security.Claims;
using Kanoonyar.Api.Programs;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Kanoonyar.Api.Tests;
public sealed class CenterLearningPlanWriteEndpointTests
{
    private static readonly IConfiguration MissingStore=new ConfigurationBuilder().Build();
    private static ClaimsPrincipal Actor(params Claim[] claims)=>
        new(new ClaimsIdentity(claims,"fixture"));
    private static int? Status(IResult r)=>(r as IStatusCodeHttpResult)?.StatusCode;
    private static readonly ICenterLearningPlanWriteAuthorization Approved=new TestGrant();

    [Fact]
    public async Task Class_write_authentication_validation_and_independent_grant_are_fail_closed()
    {
        var cmd=new CreateCenterClassRequest(Guid.NewGuid(),"Class");
        Assert.Equal(401,Status(await CenterLearningPlanWriteEndpoint.CreateClass(
            new ClaimsPrincipal(),MissingStore,Approved,"center-a",cmd,CancellationToken.None)));
        Assert.Equal(403,Status(await CenterLearningPlanWriteEndpoint.CreateClass(
            Actor(new Claim("role","superadmin")),MissingStore,Approved,"center-a",cmd,CancellationToken.None)));
        var user=Actor(new Claim("sub","actor-a"));
        Assert.Equal(400,Status(await CenterLearningPlanWriteEndpoint.CreateClass(
            user,MissingStore,Approved,"center-a",new(Guid.Empty,"Class"),CancellationToken.None)));
        Assert.Equal(400,Status(await CenterLearningPlanWriteEndpoint.CreateClass(
            user,MissingStore,Approved,"center-a",new(Guid.NewGuid()," "),CancellationToken.None)));
        Assert.Equal(403,Status(await CenterLearningPlanWriteEndpoint.CreateClass(
            user,MissingStore,new UnapprovedCenterLearningPlanWriteAuthorization(),
            "center-a",cmd,CancellationToken.None)));
        Assert.Equal(403,Status(await CenterLearningPlanWriteEndpoint.CreateClass(
            user,MissingStore,new BrokenGrant(),"center-a",cmd,CancellationToken.None)));
        Assert.Equal(503,Status(await CenterLearningPlanWriteEndpoint.CreateClass(
            user,MissingStore,Approved,"center-a",cmd,CancellationToken.None)));
    }

    [Fact]
    public async Task Session_plan_requires_exact_class_and_explicit_UTC_if_schedule_exists()
    {
        var user=Actor(new Claim("sub","actor-a"));
        var classId=Guid.NewGuid();
        Assert.Equal(400,Status(await CenterLearningPlanWriteEndpoint.ScheduleSession(
            user,MissingStore,Approved,"center-a",
            new(Guid.NewGuid(),Guid.Empty,"Invalid",null),CancellationToken.None)));
        Assert.Equal(400,Status(await CenterLearningPlanWriteEndpoint.ScheduleSession(
            user,MissingStore,Approved,"center-a",
            new(Guid.NewGuid(),classId,"Invalid time",
                new DateTimeOffset(2026,10,12,14,0,0,TimeSpan.FromHours(3))),
            CancellationToken.None)));
        Assert.Equal(403,Status(await CenterLearningPlanWriteEndpoint.ScheduleSession(
            user,MissingStore,new UnapprovedCenterLearningPlanWriteAuthorization(),
            "center-a",new(Guid.NewGuid(),classId,"Valid",null),CancellationToken.None)));
        Assert.Equal(503,Status(await CenterLearningPlanWriteEndpoint.ScheduleSession(
            user,MissingStore,Approved,"center-a",
            new(Guid.NewGuid(),classId,"Valid",null),CancellationToken.None)));
    }
    private sealed class TestGrant : ICenterLearningPlanWriteAuthorization
    {
        public ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal principal,string centerId,
            string operation,Guid resourceId,CancellationToken ct)=>ValueTask.FromResult(true);
    }
    private sealed class BrokenGrant : ICenterLearningPlanWriteAuthorization
    {
        public ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal principal,string centerId,
            string operation,Guid resourceId,CancellationToken ct)=>
            throw new InvalidOperationException("Grant unavailable");
    }
}
