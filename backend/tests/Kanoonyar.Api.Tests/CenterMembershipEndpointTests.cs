using System.Security.Claims;
using Kanoonyar.Api.Security;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Kanoonyar.Api.Tests;

public sealed class CenterMembershipEndpointTests
{
    private static readonly IConfiguration EmptyConfiguration = new ConfigurationBuilder().Build();

    [Fact]
    public async Task Anonymous_request_is_denied_before_database_access()
    {
        var result = await CenterMembershipEndpoint.GetMine(new ClaimsPrincipal(new ClaimsIdentity()), EmptyConfiguration, CancellationToken.None);
        Assert.Equal(401, Assert.IsType<Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>(result).StatusCode);
    }

    [Fact]
    public async Task Missing_subject_is_denied_before_database_access()
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("name", "person") }, "test"));
        var result = await CenterMembershipEndpoint.GetMine(user, EmptyConfiguration, CancellationToken.None);
        Assert.Equal(403, Assert.IsType<Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>(result).StatusCode);
    }

    [Fact]
    public async Task Unreachable_database_returns_service_unavailable_without_leaking_connection_details()
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("sub", "person") }, "test"));
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["CENTER_READ_DATABASE_URL"] = "Host=127.0.0.1;Port=1;Database=missing;Username=none;Password=none;Timeout=1"
        }).Build();
        var result = await CenterMembershipEndpoint.GetMine(user, configuration, CancellationToken.None);
        var problem = Assert.IsType<Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>(result);
        Assert.Equal(503, problem.StatusCode);
    }

    [Fact]
    public async Task Missing_database_configuration_fails_closed()
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("sub", "person") }, "test"));
        var result = await CenterMembershipEndpoint.GetMine(user, EmptyConfiguration, CancellationToken.None);
        Assert.Equal(503, Assert.IsType<Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>(result).StatusCode);
    }
}
