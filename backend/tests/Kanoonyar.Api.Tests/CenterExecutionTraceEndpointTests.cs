using System.Security.Claims;
using Kanoonyar.Api.Programs;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Kanoonyar.Api.Tests;

public sealed class CenterExecutionTraceEndpointTests
{
    private static readonly IConfiguration MissingDatabase = new ConfigurationBuilder().Build();
    private static ClaimsPrincipal Actor(params Claim[] claims) =>
        new(new ClaimsIdentity(claims, "test"));

    private static async Task<int?> Status(ClaimsPrincipal principal, Guid draft,
        ILocalPlanDraftReadAuthorization? auth = null)
    {
        var result = await CenterExecutionTraceEndpoint.GetTrace(principal, MissingDatabase,
            auth ?? new UnapprovedLocalPlanDraftReadAuthorization(),
            "center-a", draft, CancellationToken.None);
        return (result as IStatusCodeHttpResult)?.StatusCode;
    }

    [Fact]
    public async Task Authentication_and_subject_are_required()
    {
        Assert.Equal(401, await Status(new ClaimsPrincipal(), Guid.NewGuid()));
        Assert.Equal(403, await Status(Actor(new Claim("role","admin")), Guid.NewGuid()));
    }

    [Fact]
    public async Task Missing_or_failed_permission_never_produces_a_trace()
    {
        var actor = Actor(new Claim("sub","actor-a"));
        Assert.Equal(403, await Status(actor, Guid.NewGuid()));
        Assert.Equal(403, await Status(actor, Guid.NewGuid(), new BrokenAuthorization()));
        Assert.Equal(400, await Status(actor, Guid.Empty, new TestAuthorization()));
        Assert.Equal(503, await Status(actor, Guid.NewGuid(), new TestAuthorization()));
    }

    private sealed class TestAuthorization : ILocalPlanDraftReadAuthorization
    {
        public ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal user, string centerId,
            CancellationToken ct) => ValueTask.FromResult(true);
    }
    private sealed class BrokenAuthorization : ILocalPlanDraftReadAuthorization
    {
        public ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal user, string centerId,
            CancellationToken ct) => throw new InvalidOperationException("Grant store failed");
    }
}
