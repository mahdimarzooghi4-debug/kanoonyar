using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Kanoonyar.Api.Tests;

public sealed class HealthEndpointIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public HealthEndpointIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Theory]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    public async Task Health_endpoint_returns_http_200(string path)
    {
        using var client = _factory.CreateClient();
        using var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Stage_readiness_fails_closed_without_oidc_but_liveness_stays_healthy()
    {
        using var factory = _factory.WithWebHostBuilder(builder => builder
            .UseEnvironment("Staging")
            .ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["OIDC_AUTHORITY"] = "",
                    ["OIDC_AUDIENCE"] = ""
                })));
        using var client = factory.CreateClient();

        using var readiness = await client.GetAsync("/health/ready");
        using var liveness = await client.GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, readiness.StatusCode);
        Assert.Equal(HttpStatusCode.OK, liveness.StatusCode);
    }

    [Fact]
    public async Task Stage_readiness_accepts_a_validly_shaped_oidc_configuration()
    {
        using var factory = _factory.WithWebHostBuilder(builder => builder
            .UseEnvironment("Staging")
            .ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["OIDC_AUTHORITY"] = "https://identity.stage.example.invalid",
                    ["OIDC_AUDIENCE"] = "kanoonyar-stage"
                })));
        using var client = factory.CreateClient();

        using var readiness = await client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, readiness.StatusCode);
    }
}
