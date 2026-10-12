using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Kanoonyar.Api.Programs;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using Xunit;

namespace Kanoonyar.Api.Tests;

/// <summary>
/// Actual ASP.NET HTTP pipeline + cryptographically signed test JWTs.
/// Metadata/signing key are supplied by an in-process test fixture, not Keycloak.
/// The separate opt-in CI suite also connects through a constrained PostgreSQL LOGIN.
/// No test grant or issuer is registered in the production application.
/// </summary>
public sealed class SignedOidcHttpIntegrationTests
{
    private const string Issuer = "https://oidc.integration.invalid/realms/kanoonyar";
    private const string Audience = "kanoonyar-api-integration";
    private static string? DbUrl =>
        Environment.GetEnvironmentVariable("CENTER_DRAFT_INTEGRATION_DATABASE_URL");
    private static string? AdminUrl =>
        Environment.GetEnvironmentVariable("CENTER_DRAFT_INTEGRATION_ADMIN_URL");
    private static string? ReadUrl =>
        Environment.GetEnvironmentVariable("CENTER_DRAFT_INTEGRATION_READ_DATABASE_URL");

    private static bool DbAvailable()
    {
        bool required = Environment.GetEnvironmentVariable("CENTER_DRAFT_INTEGRATION_REQUIRED") == "1";
        if (required)
        {
            Assert.False(string.IsNullOrWhiteSpace(DbUrl));
            Assert.False(string.IsNullOrWhiteSpace(AdminUrl));
            Assert.False(string.IsNullOrWhiteSpace(ReadUrl));
        }
        return !string.IsNullOrWhiteSpace(DbUrl) && !string.IsNullOrWhiteSpace(AdminUrl) &&
            !string.IsNullOrWhiteSpace(ReadUrl);
    }

    private sealed class SignedJwtFixture : IDisposable
    {
        private readonly RSA _rsa = RSA.Create(2048);
        private readonly RsaSecurityKey _key;
        public SignedJwtFixture()
        {
            _key = new RsaSecurityKey(_rsa) { KeyId = "fixture-only-signing-key" };
        }

        public string Token(string? subject = "actor-a", string? issuer = null,
            string? audience = null, bool expired = false, RSA? signingKey = null,
            bool roleClaim = false)
        {
            var claims = new List<Claim>();
            if (subject is not null) claims.Add(new Claim("sub", subject));
            if (roleClaim) claims.Add(new Claim("role", "superadmin"));
            var descriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(claims),
                Issuer = issuer ?? Issuer,
                Audience = audience ?? Audience,
                IssuedAt = DateTime.UtcNow.AddMinutes(-20),
                NotBefore = DateTime.UtcNow.AddMinutes(-20),
                Expires = expired ? DateTime.UtcNow.AddMinutes(-5) : DateTime.UtcNow.AddMinutes(20),
                SigningCredentials = signingKey is null
                    ? new SigningCredentials(_key, SecurityAlgorithms.RsaSha256)
                    : new SigningCredentials(new RsaSecurityKey(signingKey)
                        { KeyId = "untrusted-key" }, SecurityAlgorithms.RsaSha256)
            };
            return new JsonWebTokenHandler().CreateToken(descriptor);
        }

        public WebApplicationFactory<Program> Server(bool syntheticDbGrants = false)
        {
            return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                builder.UseSetting("OIDC_AUTHORITY", Issuer);
                builder.UseSetting("OIDC_AUDIENCE", Audience);
                if (syntheticDbGrants)
                {
                    builder.UseSetting("CENTER_DRAFT_READ_DATABASE_URL", ReadUrl);
                    builder.UseSetting("CENTER_DRAFT_WRITE_DATABASE_URL", DbUrl);
                }
                builder.ConfigureTestServices(services =>
                {
                    // Offline OpenID metadata substitutes only the test signing key.
                    // The production middleware still validates RSA signature,
                    // lifetime, issuer, audience, and every authorized HTTP route.
                    services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme,
                        options =>
                        {
                            var configuration = new OpenIdConnectConfiguration { Issuer = Issuer };
                            configuration.SigningKeys.Add(_key);
                            // Set the runtime configuration manager explicitly. JwtBearer's
                            // post-configuration may otherwise install a network discovery
                            // manager for Authority and ignore this offline fixture.
                            options.ConfigurationManager =
                                new StaticConfigurationManager<OpenIdConnectConfiguration>(configuration);
                        });
                    if (syntheticDbGrants)
                    {
                        services.RemoveAll<ILocalPlanDraftWriteAuthorization>();
                        services.RemoveAll<ILocalPlanDraftReadAuthorization>();
                        services.AddSingleton<ILocalPlanDraftWriteAuthorization, TestOnlyWriteGrant>();
                        services.AddSingleton<ILocalPlanDraftReadAuthorization, TestOnlyReadGrant>();
                    }
                });
            });
        }

        public void Dispose() => _rsa.Dispose();
    }

    private static HttpRequestMessage Request(HttpMethod method, string path, string? bearer)
    {
        var request = new HttpRequestMessage(method, path);
        if (bearer is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        return request;
    }

    [Fact]
    public async Task Valid_signed_jwt_preserves_verified_sub_and_reaches_authenticated_http_handler()
    {
        using var fixture = new SignedJwtFixture();
        await using var server = fixture.Server();
        using var client = server.CreateClient();
        using var request = Request(HttpMethod.Get, "/api/v1/identity/me", fixture.Token());
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("actor-a", body.RootElement.GetProperty("subjectId").GetString());
    }

    [Fact]
    public async Task Invalid_signature_issuer_audience_or_expiry_are_rejected_at_http_boundary()
    {
        using var fixture = new SignedJwtFixture();
        using var wrongKey = RSA.Create(2048);
        await using var server = fixture.Server();
        using var client = server.CreateClient();
        var tokens = new string?[]
        {
            null,
            fixture.Token(signingKey: wrongKey),
            fixture.Token(issuer: "https://unknown-issuer.invalid"),
            fixture.Token(audience: "wrong-audience"),
            fixture.Token(expired: true)
        };
        foreach (var token in tokens)
        {
            using var request = Request(HttpMethod.Get, "/api/v1/identity/me", token);
            using var response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
    }

    [Fact]
    public async Task Signed_identity_and_administrative_claims_never_grant_unapproved_draft_access()
    {
        using var fixture = new SignedJwtFixture();
        await using var server = fixture.Server();
        using var client = server.CreateClient();
        var bearer = fixture.Token(roleClaim: true);
        using (var request = Request(HttpMethod.Get,
            "/api/v1/centers/center-a/local-plan-drafts", bearer))
        using (var response = await client.SendAsync(request))
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        using var write = Request(HttpMethod.Post,
            "/api/v1/centers/center-a/local-plan-drafts/revisions", bearer);
        write.Content = JsonContent.Create(new
        {
            draftId = Guid.NewGuid(), expectedRevision = 0, programId = "national-1",
            nationalVersion = 1, localTitle = "Draft", localActivities = new[] { "Workshop" }
        });
        using var result = await client.SendAsync(write);
        Assert.Equal(HttpStatusCode.Forbidden, result.StatusCode);
    }

    [Fact]
    public async Task Valid_signature_but_missing_subject_fails_closed_at_application_boundary()
    {
        using var fixture = new SignedJwtFixture();
        await using var server = fixture.Server();
        using var client = server.CreateClient();
        using var request = Request(HttpMethod.Get, "/api/v1/identity/me", fixture.Token(subject: null));
        using var response = await client.SendAsync(request);
        Assert.Contains(response.StatusCode, new[] { HttpStatusCode.Forbidden, HttpStatusCode.Unauthorized });
    }

    [Fact]
    public async Task Signed_http_to_restricted_postgres_roundtrip_and_concurrency_are_real()
    {
        if (!DbAvailable()) return;
        using var fixture = new SignedJwtFixture();
        await using var server = fixture.Server(syntheticDbGrants: true);
        using var client = server.CreateClient();
        var draft = Guid.NewGuid();
        async Task<HttpResponseMessage> Post(long expected, string? token = null,
            string center = "center-a")
        {
            using var request = Request(HttpMethod.Post,
                $"/api/v1/centers/{center}/local-plan-drafts/revisions", token ?? fixture.Token());
            request.Content = JsonContent.Create(new
            {
                draftId = draft, expectedRevision = expected, programId = "national-1",
                nationalVersion = 1, localTitle = "Signed draft", localActivities = new[] { "Activity" }
            });
            return await client.SendAsync(request);
        }
        using (var response = await Post(0))
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using (var request = Request(HttpMethod.Get,
            "/api/v1/centers/center-a/local-plan-drafts", fixture.Token()))
        using (var response = await client.SendAsync(request))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Contains(body.RootElement.GetProperty("drafts").EnumerateArray(),
                item => item.GetProperty("draftId").GetGuid() == draft);
        }

        var writes = await Task.WhenAll(Post(1), Post(1));
        try
        {
            Assert.Equal(1, writes.Count(x => x.StatusCode == HttpStatusCode.OK));
            Assert.Equal(1, writes.Count(x => x.StatusCode == HttpStatusCode.Conflict));
        }
        finally
        {
            foreach (var write in writes) write.Dispose();
        }

        using (var otherCenter = await Post(2, center: "center-b"))
            Assert.Equal(HttpStatusCode.Forbidden, otherCenter.StatusCode);
        using (var wrongActor = await Post(2, fixture.Token(subject: "actor-b")))
            Assert.Equal(HttpStatusCode.Forbidden, wrongActor.StatusCode);
        using (var revoked = await Post(2, fixture.Token(subject: "actor-revoked")))
            Assert.Equal(HttpStatusCode.Forbidden, revoked.StatusCode);
        using (var noAuthority = Request(HttpMethod.Get,
            "/api/v1/centers/center-a/local-plan-drafts", fixture.Token(subject: "actor-b")))
        using (var response = await client.SendAsync(noAuthority))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Empty(body.RootElement.GetProperty("drafts").EnumerateArray());
        }

        await using var connection = new NpgsqlConnection(AdminUrl);
        await connection.OpenAsync();
        await using var check = new NpgsqlCommand(
            """
            SELECT
              (SELECT count(*) FROM programs.center_plan_draft_revisions WHERE draft_id=@draft),
              (SELECT count(*) FROM governance.command_audit WHERE target_id=@target),
              (SELECT count(*) FROM governance.outbox_events WHERE aggregate_id=@target)
            """, connection);
        check.Parameters.AddWithValue("draft", draft);
        check.Parameters.AddWithValue("target", draft.ToString("D"));
        await using var reader = await check.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(2, reader.GetInt64(0));
        Assert.Equal(2, reader.GetInt64(1));
        Assert.Equal(2, reader.GetInt64(2));
    }

    [Fact]
    public async Task Signed_jwt_history_stays_denied_without_business_grant()
    {
        using var fixture = new SignedJwtFixture();
        await using var server = fixture.Server();
        using var client = server.CreateClient();
        var url = "/api/v1/centers/center-a/local-plan-drafts/" +
            Guid.NewGuid().ToString("D") + "/revisions";
        using var request = Request(HttpMethod.Get, url, fixture.Token(roleClaim: true));
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Signed_http_history_reads_persisted_revisions_only_under_test_grant()
    {
        if (!DbAvailable()) return;
        using var fixture = new SignedJwtFixture();
        await using var server = fixture.Server(syntheticDbGrants: true);
        using var client = server.CreateClient();
        var draft = Guid.NewGuid();
        for (long revision = 0; revision < 2; revision++)
        {
            using var write = Request(HttpMethod.Post,
                "/api/v1/centers/center-a/local-plan-drafts/revisions", fixture.Token());
            write.Content = JsonContent.Create(new {
                draftId = draft, expectedRevision = revision,
                programId = "national-1", nationalVersion = 1,
                localTitle = "History", localActivities = new[] { "Workshop" }
            });
            using var accepted = await client.SendAsync(write);
            Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        }
        var url = "/api/v1/centers/center-a/local-plan-drafts/" + draft.ToString("D") + "/revisions";
        using (var request = Request(HttpMethod.Get, url, fixture.Token()))
        using (var response = await client.SendAsync(request))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var revisions = payload.RootElement.GetProperty("revisions");
            Assert.Equal(2, revisions.GetArrayLength());
            Assert.Equal(2, revisions[0].GetProperty("revision").GetInt64());
            Assert.Equal(1, revisions[1].GetProperty("revision").GetInt64());
        }
        using (var request = Request(HttpMethod.Get, url, fixture.Token(subject: "actor-b")))
        using (var response = await client.SendAsync(request))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Empty(payload.RootElement.GetProperty("revisions").EnumerateArray());
        }
        using (var request = Request(HttpMethod.Get, url + "?beforeRevision=0", fixture.Token()))
        using (var response = await client.SendAsync(request))
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private sealed class TestOnlyWriteGrant : ILocalPlanDraftWriteAuthorization
    {
        public ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal principal, string centerId,
            Guid draftId, CancellationToken ct) => ValueTask.FromResult(true);
    }

    private sealed class TestOnlyReadGrant : ILocalPlanDraftReadAuthorization
    {
        public ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal principal, string centerId,
            CancellationToken ct) => ValueTask.FromResult(true);
    }
}
