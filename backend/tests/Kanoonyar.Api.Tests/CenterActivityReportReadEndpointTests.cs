using System.Security.Claims;
using Kanoonyar.Api.Programs;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Kanoonyar.Api.Tests;

public sealed class CenterActivityReportReadEndpointTests
{
    private static readonly IConfiguration Unconfigured = new ConfigurationBuilder().Build();
    private static ClaimsPrincipal Actor(params Claim[] claims) =>
        new(new ClaimsIdentity(claims, "fixture"));
    private static async Task<int?> Check(ClaimsPrincipal actor, Guid draft, long? before = null,
        ICenterActivityReportReadAuthorization? gate = null)
    {
        var response = await CenterActivityReportReadEndpoint.ListReports(actor, Unconfigured,
            gate ?? new UnapprovedCenterActivityReportReadAuthorization(),
            "center-a", draft, before, CancellationToken.None);
        return (response as IStatusCodeHttpResult)?.StatusCode;
    }
    [Fact]
    public async Task Authenticated_identity_and_valid_request_are_required()
    {
        Assert.Equal(401, await Check(new ClaimsPrincipal(), Guid.NewGuid()));
        Assert.Equal(403, await Check(Actor(new Claim("role","admin")), Guid.NewGuid()));
        var actor = Actor(new Claim("sub","actor-a"));
        Assert.Equal(400, await Check(actor, Guid.Empty));
        Assert.Equal(400, await Check(actor, Guid.NewGuid(), 0));
        Assert.Equal(400, await Check(actor, Guid.NewGuid(), -1));
    }
    [Fact]
    public async Task Activity_report_read_is_a_separate_default_denied_operation()
    {
        var actor = Actor(new Claim("sub","actor-a"));
        Assert.Equal(403, await Check(actor, Guid.NewGuid()));
        Assert.Equal(403, await Check(actor, Guid.NewGuid(), gate: new BrokenGrant()));
        Assert.Equal(503, await Check(actor, Guid.NewGuid(), gate: new SyntheticGrant()));
    }
    private sealed class BrokenGrant : ICenterActivityReportReadAuthorization
    {
        public ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal actor, string centerId,
            CancellationToken ct) => throw new InvalidOperationException("Grant lookup failed");
    }
    private sealed class SyntheticGrant : ICenterActivityReportReadAuthorization
    {
        public ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal actor, string centerId,
            CancellationToken ct) => ValueTask.FromResult(true);
    }
}
