using System.Security.Claims;
using Kanoonyar.Api.Programs;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Kanoonyar.Api.Tests;

public class LocalPlanDraftReadEndpointTests
{
    private static IConfiguration Config(string? url = null) =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["CENTER_DRAFT_READ_DATABASE_URL"] = url }).Build();

    [Fact]
    public async Task Anonymous_request_is_rejected()
    {
        var result = await LocalPlanDraftReadEndpoint.ListMine(new ClaimsPrincipal(), Config(), "c1", default);
        Assert.Equal(401, (result as IStatusCodeHttpResult)?.StatusCode);
    }

    [Fact]
    public async Task Missing_subject_is_rejected()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("name", "person") }, "test"));
        var result = await LocalPlanDraftReadEndpoint.ListMine(principal, Config(), "c1", default);
        Assert.Equal(403, (result as IStatusCodeHttpResult)?.StatusCode);
    }

    [Fact]
    public async Task Missing_storage_fails_closed()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("sub", "user-1") }, "test"));
        var result = await LocalPlanDraftReadEndpoint.ListMine(principal, Config(), "c1", default);
        Assert.Equal(503, (result as IStatusCodeHttpResult)?.StatusCode);
    }
}
