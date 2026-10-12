using System.Security.Claims;
using Kanoonyar.Api.Programs;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Kanoonyar.Api.Tests;

public sealed class CenterSourceCatalogEndpointTests
{
    private static ClaimsPrincipal Actor(params Claim[] claims) =>
        new(new ClaimsIdentity(claims, "fixture"));
    private static readonly IConfiguration Unconfigured = new ConfigurationBuilder().Build();

    private static async Task<int?> Check(ClaimsPrincipal actor, string kind, Guid? before = null,
        ICenterSourceCatalogReadAuthorization? permission = null)
    {
        var result = await CenterSourceCatalogEndpoint.ListSources(actor,
            Unconfigured, permission ?? new UnapprovedCenterSourceCatalogReadAuthorization(),
            "center-a", kind, before, CancellationToken.None);
        return (result as IStatusCodeHttpResult)?.StatusCode;
    }

    [Fact]
    public async Task Authenticated_subject_and_known_source_kind_are_required()
    {
        Assert.Equal(401, await Check(new ClaimsPrincipal(), "events"));
        Assert.Equal(403, await Check(Actor(new Claim("role","superadmin")), "events"));
        var actor = Actor(new Claim("sub","actor-a"));
        Assert.Equal(400, await Check(actor, "not-a-kind"));
        Assert.Equal(400, await Check(actor, "events", Guid.Empty));
    }

    [Fact]
    public async Task Owner_catalog_is_separately_default_denied_and_fails_closed()
    {
        var actor = Actor(new Claim("sub","actor-a"));
        Assert.Equal(403, await Check(actor, "events"));
        Assert.Equal(403, await Check(actor, "class-sessions", permission:new BrokenGrant()));
        Assert.Equal(503, await Check(actor, "events", permission:new SyntheticGrant()));
    }

    private sealed class SyntheticGrant : ICenterSourceCatalogReadAuthorization
    {
        public ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal actor, string centerId,
            CancellationToken ct) => ValueTask.FromResult(true);
    }
    private sealed class BrokenGrant : ICenterSourceCatalogReadAuthorization
    {
        public ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal actor, string centerId,
            CancellationToken ct) => throw new InvalidOperationException("Read grant store offline");
    }
}
