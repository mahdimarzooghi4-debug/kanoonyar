using System.Net;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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
            .UseSetting("OIDC_AUTHORITY", "")
            .UseSetting("OIDC_AUDIENCE", ""));
        using var client = factory.CreateClient();

        using var readiness = await client.GetAsync("/health/ready");
        using var liveness = await client.GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, readiness.StatusCode);
        Assert.Equal(HttpStatusCode.OK, liveness.StatusCode);
    }

    [Fact]
    public async Task Stage_readiness_requires_reachable_discovery_with_exact_issuer_match()
    {
        const string authority = "https://identity.stage.example.invalid";
        Uri? requestedUri = null;
        using var factory = _factory.WithWebHostBuilder(builder => builder
            .UseEnvironment("Staging")
            .UseSetting("OIDC_AUTHORITY", authority)
            .UseSetting("OIDC_AUDIENCE", "kanoonyar-stage")
            .ConfigureTestServices(services =>
            {
                services.RemoveAll<IHttpClientFactory>();
                services.AddSingleton<IHttpClientFactory>(new StubHttpClientFactory(request =>
                {
                    requestedUri = request.RequestUri;
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(
                            "{\"issuer\":\"https://identity.stage.example.invalid\"}",
                            Encoding.UTF8,
                            "application/json")
                    };
                }));
            }));
        using var client = factory.CreateClient();

        using var readiness = await client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, readiness.StatusCode);
        Assert.Equal(new Uri("https://identity.stage.example.invalid/.well-known/openid-configuration"), requestedUri);
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable, "{\"issuer\":\"https://identity.stage.example.invalid\"}")]
    [InlineData(HttpStatusCode.OK, "{\"issuer\":\"https://other-issuer.example.invalid\"}")]
    [InlineData(HttpStatusCode.OK, "{}")]
    [InlineData(HttpStatusCode.OK, "not-json")]
    public async Task Stage_readiness_fails_when_discovery_is_unavailable_or_mismatched(
        HttpStatusCode statusCode,
        string document)
    {
        using var factory = _factory.WithWebHostBuilder(builder => builder
            .UseEnvironment("Staging")
            .UseSetting("OIDC_AUTHORITY", "https://identity.stage.example.invalid")
            .UseSetting("OIDC_AUDIENCE", "kanoonyar-stage")
            .ConfigureTestServices(services =>
            {
                services.RemoveAll<IHttpClientFactory>();
                services.AddSingleton<IHttpClientFactory>(new StubHttpClientFactory(_ =>
                    new HttpResponseMessage(statusCode)
                    {
                        Content = new StringContent(document, Encoding.UTF8, "application/json")
                    }));
            }));
        using var client = factory.CreateClient();

        using var readiness = await client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, readiness.StatusCode);
    }

    private sealed class StubHttpClientFactory(
        Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) =>
            new(new StubHttpMessageHandler(responseFactory));
    }

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(responseFactory(request));
    }
}
