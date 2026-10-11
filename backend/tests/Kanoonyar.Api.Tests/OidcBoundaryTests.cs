using Xunit;
using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Hosting;

namespace Kanoonyar.Api.Tests;

public sealed class OidcBoundaryTests
{
    [Fact]
    public async Task Identity_endpoint_fails_closed_when_oidc_is_not_configured()
    {
        await using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/api/v1/identity/me");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task Configured_oidc_rejects_anonymous_identity_requests()
    {
        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.UseSetting("OIDC_AUTHORITY", "https://identity.example.test/realms/kanoonyar")
                .UseSetting("OIDC_AUDIENCE", "kanoonyar-api"));
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/api/v1/identity/me");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public void Rejects_insecure_oidc_authority()
    {
        Assert.Throws<InvalidOperationException>(() =>
        {
            using var factory = new WebApplicationFactory<Program>()
                .WithWebHostBuilder(builder => builder.UseSetting("OIDC_AUTHORITY", "http://identity.example.test/realms/kanoonyar")
                    .UseSetting("OIDC_AUDIENCE", "kanoonyar-api"));
            using var client = factory.CreateClient();
        });
    }
}
