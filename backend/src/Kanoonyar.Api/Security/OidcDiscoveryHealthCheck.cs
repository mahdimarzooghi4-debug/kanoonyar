using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Kanoonyar.Api.Security;

public sealed class OidcDiscoveryHealthCheck(
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    IHostEnvironment environment) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        if (environment.IsDevelopment())
            return HealthCheckResult.Healthy();

        var authority = configuration["OIDC_AUTHORITY"];
        if (string.IsNullOrWhiteSpace(authority) ||
            !Uri.TryCreate(authority, UriKind.Absolute, out var issuerUri) ||
            issuerUri.Scheme != Uri.UriSchemeHttps)
        {
            return HealthCheckResult.Unhealthy("A valid HTTPS OIDC authority is required for readiness.");
        }

        var discoveryUri = new Uri($"{authority.TrimEnd('/')}/.well-known/openid-configuration");
        try
        {
            using var response = await httpClientFactory.CreateClient("oidc-discovery")
                .GetAsync(discoveryUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
                return HealthCheckResult.Unhealthy("The configured OIDC discovery endpoint is unavailable.");

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            if (!document.RootElement.TryGetProperty("issuer", out var issuerElement) ||
                issuerElement.ValueKind != JsonValueKind.String ||
                !string.Equals(issuerElement.GetString(), authority, StringComparison.Ordinal))
            {
                return HealthCheckResult.Unhealthy("The OIDC discovery issuer does not match the configured authority.");
            }

            return HealthCheckResult.Healthy();
        }
        catch (HttpRequestException)
        {
            return HealthCheckResult.Unhealthy("The configured OIDC discovery endpoint is unavailable.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return HealthCheckResult.Unhealthy("The configured OIDC discovery endpoint timed out.");
        }
        catch (JsonException)
        {
            return HealthCheckResult.Unhealthy("The configured OIDC discovery document is invalid.");
        }
    }
}
