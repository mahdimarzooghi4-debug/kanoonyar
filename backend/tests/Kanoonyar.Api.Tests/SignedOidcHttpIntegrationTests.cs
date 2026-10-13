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
    private static string? LearningUrl =>
        Environment.GetEnvironmentVariable("CENTER_LEARNING_INTEGRATION_DATABASE_URL");
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
                    builder.UseSetting("CENTER_LEARNING_WRITE_DATABASE_URL", LearningUrl);
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
                        services.RemoveAll<ICenterActivityReportReadAuthorization>();
                        services.RemoveAll<ICenterLearningPlanWriteAuthorization>();
                        services.RemoveAll<ICenterSessionClaimWriteAuthorization>();
                        services.AddSingleton<ILocalPlanDraftWriteAuthorization, TestOnlyWriteGrant>();
                        services.AddSingleton<ILocalPlanDraftReadAuthorization, TestOnlyReadGrant>();
                        services.AddSingleton<ICenterActivityReportReadAuthorization, TestOnlyReportReadGrant>();
                        services.AddSingleton<ICenterLearningPlanWriteAuthorization, TestLearningWriteGrant>();
                        services.AddSingleton<ICenterSessionClaimWriteAuthorization, TestSessionClaimWriteGrant>();
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

    [Fact]
    public async Task Signed_jwt_without_an_independent_grant_cannot_read_execution_trace()
    {
        using var fixture = new SignedJwtFixture();
        await using var app = fixture.Server();
        using var client = app.CreateClient();
        var path = "/api/v1/centers/center-a/local-plan-drafts/" +
            Guid.NewGuid().ToString("D") + "/execution-trace";
        using (var anonymous = Request(HttpMethod.Get, path, null))
        using (var result = await client.SendAsync(anonymous))
            Assert.Equal(HttpStatusCode.Unauthorized, result.StatusCode);
        using (var signed = Request(HttpMethod.Get, path, fixture.Token(roleClaim: true)))
        using (var result = await client.SendAsync(signed))
            Assert.Equal(HttpStatusCode.Forbidden, result.StatusCode);
    }

    [Fact]
    public async Task Signed_jwt_does_not_grant_activity_reports_even_with_administrative_claims()
    {
        using var fixture = new SignedJwtFixture();
        await using var app = fixture.Server();
        using var client = app.CreateClient();
        var path = "/api/v1/centers/center-a/local-plan-drafts/" +
            Guid.NewGuid().ToString("D") + "/activity-reports";
        using (var anonymous = Request(HttpMethod.Get, path, null))
        using (var denied = await client.SendAsync(anonymous))
            Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        using (var signed = Request(HttpMethod.Get, path, fixture.Token(roleClaim:true)))
        using (var denied = await client.SendAsync(signed))
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
    }

    [Fact]
    public async Task Signed_role_claim_never_grants_sourced_activity_report_write()
    {
        using var fixture = new SignedJwtFixture();
        await using var server = fixture.Server();
        using var client = server.CreateClient();
        const string route = "/api/v1/centers/center-a/activity-reports/sourced";
        using (var anonymous = Request(HttpMethod.Post, route, null))
        {
            anonymous.Content = JsonContent.Create(new { });
            using var response = await client.SendAsync(anonymous);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
        using var request = Request(HttpMethod.Post, route, fixture.Token(roleClaim:true));
        request.Content = JsonContent.Create(new {
            reportId = Guid.NewGuid(), draftId = Guid.NewGuid(),
            linkId = Guid.NewGuid(), sourceKind = "CLASS_SESSION",
            sourceId = Guid.NewGuid(), evidenceRef = "synthetic-reference",
            reportedSummary = "Unverified fixture activity",
            claimedOccurredAt = (DateTimeOffset?)null
        });
        using var denied = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
    }

    [Fact]
    public async Task Signed_admin_role_cannot_configure_capacity_or_submit_registration_intent()
    {
        using var fixture=new SignedJwtFixture();
        await using var server=fixture.Server();
        using var client=server.CreateClient();
        var cls=Guid.NewGuid();
        foreach(var path in new[]{
            $"/api/v1/centers/center-a/classes/{cls}/capacity-revisions",
            $"/api/v1/centers/center-a/classes/{cls}/registration-intents"
        })
        {
            using var request=Request(HttpMethod.Post,path,fixture.Token(roleClaim:true));
            request.Content=JsonContent.Create(new {
                expectedRevision=0,capacity=3,requestId=Guid.NewGuid(),applicantRef=Guid.NewGuid()
            });
            using var result=await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.Forbidden,result.StatusCode);
        }
        using var read=Request(HttpMethod.Get,
            $"/api/v1/centers/center-a/classes/{cls}/intake",fixture.Token(roleClaim:true));
        using var denied=await client.SendAsync(read);
        Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);
    }

    [Fact]
    public async Task Signed_admin_JWT_cannot_raise_or_read_registration_human_review()
    {
        using var fixture=new SignedJwtFixture();
        await using var server=fixture.Server();
        using var client=server.CreateClient();
        var path=$"/api/v1/centers/center-a/classes/{Guid.NewGuid()}/registration-intents/{Guid.NewGuid()}/review-requests";
        using(var anonymous=Request(HttpMethod.Get,path,null))
        using(var denied=await client.SendAsync(anonymous))
            Assert.Equal(HttpStatusCode.Unauthorized,denied.StatusCode);
        using(var authenticated=Request(HttpMethod.Get,path,fixture.Token(roleClaim:true)))
        using(var denied=await client.SendAsync(authenticated))
            Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);
        using(var command=Request(HttpMethod.Post,path,fixture.Token(roleClaim:true)))
        {
            command.Content=JsonContent.Create(new{
                reviewRequestId=Guid.NewGuid(),reviewTopic="CONSENT_AUTHORITY"
            });
            using var denied=await client.SendAsync(command);
            Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);
        }
    }

    [Fact]
    public async Task Signed_admin_role_cannot_edit_or_read_center_website_drafts()
    {
        using var fixture=new SignedJwtFixture();
        await using var server=fixture.Server();
        using var client=server.CreateClient();
        const string list="/api/v1/centers/center-a/site/drafts";
        using(var anonymous=Request(HttpMethod.Get,list,null))
        using(var result=await client.SendAsync(anonymous))
            Assert.Equal(HttpStatusCode.Unauthorized,result.StatusCode);
        using(var request=Request(HttpMethod.Get,list,fixture.Token(roleClaim:true)))
        using(var denied=await client.SendAsync(request))
            Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);
        using(var command=Request(HttpMethod.Post,list+"/revisions",fixture.Token(roleClaim:true)))
        {
            command.Content=JsonContent.Create(new {
                pageId=Guid.NewGuid(),expectedRevision=0,slug="home",title="Home",bodyText="draft"
            });
            using var denied=await client.SendAsync(command);
            Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);
        }
    }

    [Fact]
    public async Task Signed_admin_role_cannot_edit_read_or_replay_private_site_design()
    {
        using var fixture=new SignedJwtFixture();
        await using var server=fixture.Server();
        using var client=server.CreateClient();
        const string route="/api/v1/centers/center-a/site/design";
        using(var anon=Request(HttpMethod.Get,route,null))
        using(var result=await client.SendAsync(anon))
            Assert.Equal(HttpStatusCode.Unauthorized,result.StatusCode);
        foreach(var path in new[]{route,route+"/revisions"})
        {
            using var request=Request(HttpMethod.Get,path,fixture.Token(roleClaim:true));
            using var denied=await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);
        }
        using(var command=Request(HttpMethod.Post,route+"/revisions",fixture.Token(roleClaim:true)))
        {
            command.Content=JsonContent.Create(new{
                expectedRevision=0,layoutKey="HERO",paletteKey="OCEAN",typeKey="SYSTEM",
                headerText="Private title",footerText="",showIntro=true,showFooter=false
            });
            using var denied=await client.SendAsync(command);
            Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);
        }
    }

    [Fact]
    public async Task Signed_admin_JWT_never_grants_form_authoring_or_private_reads()
    {
        using var fixture=new SignedJwtFixture();
        await using var server=fixture.Server();
        using var client=server.CreateClient();
        const string route="/api/v1/centers/center-a/site/forms/drafts";
        using(var anonymous=Request(HttpMethod.Get,route,null))
        using(var result=await client.SendAsync(anonymous))
            Assert.Equal(HttpStatusCode.Unauthorized,result.StatusCode);
        foreach(var path in new[]{route,route+"/"+Guid.NewGuid()+"/revisions"})
        {
            using var request=Request(HttpMethod.Get,path,fixture.Token(roleClaim:true));
            using var denied=await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);
        }
        using(var request=Request(HttpMethod.Post,route+"/revisions",fixture.Token(roleClaim:true)))
        {
            request.Content=JsonContent.Create(new{
                formId=Guid.NewGuid(),expectedRevision=0,slug="test-form",title="Private",
                fields=new[]{new{key="topic",label="Topic",kind="SHORT_TEXT",required=true,
                  options=Array.Empty<string>()}}
            });
            using var denied=await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);
        }
    }

    [Fact]
    public async Task Signed_admin_JWT_never_authorizes_campaign_drafts_without_a_live_grant()
    {
        using var fixture=new SignedJwtFixture();
        await using var server=fixture.Server();
        using var client=server.CreateClient();
        const string url="/api/v1/centers/center-a/campaigns/drafts";
        using(var req=Request(HttpMethod.Get,url,null))
        using(var denied=await client.SendAsync(req))
            Assert.Equal(HttpStatusCode.Unauthorized,denied.StatusCode);
        foreach(var path in new[]{url,url+"/"+Guid.NewGuid()+"/revisions"})
        {
            using var req=Request(HttpMethod.Get,path,fixture.Token(roleClaim:true));
            using var denied=await client.SendAsync(req);
            Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);
        }
        using(var req=Request(HttpMethod.Post,url+"/revisions",fixture.Token(roleClaim:true)))
        {
            req.Content=JsonContent.Create(new{
                campaignId=Guid.NewGuid(),expectedRevision=0,slug="synthetic",
                draftId=Guid.NewGuid(),pinnedPlanRevision=1,title="Draft",
                goalText="Awareness",audienceDescription="Families",
                plannedActivities=new[]{new{activityKey="visit",title="Visit"}}
            });
            using var denied=await client.SendAsync(req);
            Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);
        }
    }

    [Fact]
    public async Task Signed_admin_JWT_cannot_read_or_link_private_campaign_source_reports()
    {
        using var fixture=new SignedJwtFixture();
        await using var server=fixture.Server();
        using var client=server.CreateClient();
        var url="/api/v1/centers/center-a/campaigns/"+Guid.NewGuid()+"/report-links";
        using(var request=Request(HttpMethod.Get,url,null))
        using(var denied=await client.SendAsync(request))
            Assert.Equal(HttpStatusCode.Unauthorized,denied.StatusCode);
        using(var request=Request(HttpMethod.Get,url,fixture.Token(roleClaim:true)))
        using(var denied=await client.SendAsync(request))
            Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);
        using(var request=Request(HttpMethod.Post,url,fixture.Token(roleClaim:true)))
        {
            request.Content=JsonContent.Create(new{
                associationId=Guid.NewGuid(),campaignRevision=1,
                activityKey="workshop",reportId=Guid.NewGuid()
            });
            using var denied=await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);
        }
    }

    [Fact]
    public async Task Signed_admin_token_cannot_read_or_write_campaign_inspection_notes()
    {
        using var fixture=new SignedJwtFixture();
        await using var server=fixture.Server();
        using var client=server.CreateClient();
        var url="/api/v1/centers/center-a/campaigns/"+Guid.NewGuid()+
          "/report-links/"+Guid.NewGuid()+"/inspection-notes";
        using(var request=Request(HttpMethod.Get,url,null))
        using(var denied=await client.SendAsync(request))
            Assert.Equal(HttpStatusCode.Unauthorized,denied.StatusCode);
        using(var request=Request(HttpMethod.Get,url,fixture.Token(roleClaim:true)))
        using(var denied=await client.SendAsync(request))
            Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);
        using(var request=Request(HttpMethod.Post,url,fixture.Token(roleClaim:true)))
        {
            request.Content=JsonContent.Create(new{noteId=Guid.NewGuid(),noteText="Comment only"});
            using var denied=await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);
        }
    }

    [Fact]
    public async Task Signed_admin_role_token_cannot_read_or_append_inspection_followups()
    {
        using var fixture=new SignedJwtFixture();
        await using var server=fixture.Server();
        using var client=server.CreateClient();
        var url="/api/v1/centers/center-a/campaigns/"+Guid.NewGuid()+
          "/report-links/"+Guid.NewGuid()+"/inspection-notes/"+Guid.NewGuid()+"/followups";
        using(var request=Request(HttpMethod.Get,url+"?entryKind=REQUEST",null))
        using(var denied=await client.SendAsync(request))
            Assert.Equal(HttpStatusCode.Unauthorized,denied.StatusCode);
        using(var request=Request(HttpMethod.Get,url+"?entryKind=REQUEST",fixture.Token(roleClaim:true)))
        using(var denied=await client.SendAsync(request))
            Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);
        using(var request=Request(HttpMethod.Post,url,fixture.Token(roleClaim:true)))
        {
            request.Content=JsonContent.Create(new{
                entryId=Guid.NewGuid(),entryKind="REQUEST",
                requestId=(Guid?)null,content="Human clarification only"
            });
            using var denied=await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);
        }
    }

    [Fact]
    public async Task Signed_admin_role_token_cannot_open_combined_campaign_review_workspace()
    {
        using var fixture=new SignedJwtFixture();
        await using var server=fixture.Server();
        using var client=server.CreateClient();
        var url="/api/v1/centers/center-a/campaigns/"+Guid.NewGuid()+
         "/report-links/"+Guid.NewGuid()+"/review-workspace";
        using(var request=Request(HttpMethod.Get,url,null))
        using(var denied=await client.SendAsync(request))
            Assert.Equal(HttpStatusCode.Unauthorized,denied.StatusCode);
        using(var request=Request(HttpMethod.Get,url,fixture.Token(roleClaim:true)))
        using(var denied=await client.SendAsync(request))
            Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);
    }

    [Fact]
    public async Task Signed_admin_role_cannot_read_center_campaign_evidence_register()
    {
        using var fixture=new SignedJwtFixture();
        await using var server=fixture.Server();
        using var client=server.CreateClient();
        var url="/api/v1/centers/center-a/campaigns/"+Guid.NewGuid()+"/evidence-register";
        using(var request=Request(HttpMethod.Get,url,null))
        using(var denied=await client.SendAsync(request))
            Assert.Equal(HttpStatusCode.Unauthorized,denied.StatusCode);
        using(var request=Request(HttpMethod.Get,url,fixture.Token(roleClaim:true)))
        using(var denied=await client.SendAsync(request))
            Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);
    }

    private sealed class TestOnlyReportReadGrant : ICenterActivityReportReadAuthorization
    {
        public ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal actor, string centerId,
            CancellationToken ct) => ValueTask.FromResult(true);
    }

    [Fact]
    public async Task Signed_jwt_role_claim_cannot_grant_owner_catalog_read()
    {
        using var fixture = new SignedJwtFixture();
        await using var app = fixture.Server();
        using var client = app.CreateClient();
        foreach (var kind in new []{"events","class-sessions"})
        {
            var path = "/api/v1/centers/center-a/activity-sources/" + kind;
            using (var anonymous = Request(HttpMethod.Get,path,null))
            using (var denied = await client.SendAsync(anonymous))
                Assert.Equal(HttpStatusCode.Unauthorized,denied.StatusCode);
            using (var signed = Request(HttpMethod.Get,path,fixture.Token(roleClaim:true)))
            using (var denied = await client.SendAsync(signed))
                Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);
        }
    }

    [Fact]
    public async Task Signed_jwt_still_needs_independent_class_and_session_write_grants()
    {
        using var fixture=new SignedJwtFixture();
        await using var app=fixture.Server();
        using var client=app.CreateClient();
        foreach(var path in new[]{
            "/api/v1/centers/center-a/classes",
            "/api/v1/centers/center-a/classes/sessions"
        })
        {
            using(var anonymous=Request(HttpMethod.Post,path,null))
            {
                anonymous.Content=JsonContent.Create(new {classId=Guid.NewGuid(),title="Fixture"});
                using var response=await client.SendAsync(anonymous);
                Assert.Equal(HttpStatusCode.Unauthorized,response.StatusCode);
            }
            using(var signed=Request(HttpMethod.Post,path,fixture.Token(roleClaim:true)))
            {
                signed.Content=path.EndsWith("sessions")
                  ? JsonContent.Create(new {sessionId=Guid.NewGuid(),classId=Guid.NewGuid(),
                      title="Attempted",scheduledAt=(DateTimeOffset?)null})
                  : JsonContent.Create(new {classId=Guid.NewGuid(),title="Attempted"});
                using var response=await client.SendAsync(signed);
                Assert.Equal(HttpStatusCode.Forbidden,response.StatusCode);
            }
        }
    }

    [Fact]
    public async Task Signed_http_synthetic_grant_roundtrips_class_and_session_to_postgres()
    {
        if(!DbAvailable())return;
        Assert.False(string.IsNullOrWhiteSpace(LearningUrl));
        using var fixture=new SignedJwtFixture();
        await using var app=fixture.Server(syntheticDbGrants:true);
        using var client=app.CreateClient();
        var classId=Guid.NewGuid();
        var sessionId=Guid.NewGuid();
        using(var create=Request(HttpMethod.Post,
            "/api/v1/centers/center-a/classes",fixture.Token()))
        {
            create.Content=JsonContent.Create(new {classId,title="Signed class"});
            using var response=await client.SendAsync(create);
            Assert.Equal(HttpStatusCode.OK,response.StatusCode);
            using var payload=JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal(classId,payload.RootElement.GetProperty("classId").GetGuid());
        }
        using(var create=Request(HttpMethod.Post,
            "/api/v1/centers/center-a/classes/sessions",fixture.Token()))
        {
            create.Content=JsonContent.Create(new {
                sessionId,classId,title="Signed planned session",
                scheduledAt=(DateTimeOffset?)null
            });
            using var response=await client.SendAsync(create);
            Assert.Equal(HttpStatusCode.OK,response.StatusCode);
            using var payload=JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal(sessionId,payload.RootElement.GetProperty("sessionId").GetGuid());
            Assert.Equal(JsonValueKind.Null,payload.RootElement.GetProperty("scheduledAt").ValueKind);
        }
        using(var wrong=Request(HttpMethod.Post,
            "/api/v1/centers/center-a/classes",fixture.Token(subject:"actor-b")))
        {
            wrong.Content=JsonContent.Create(new {classId=Guid.NewGuid(),title="Cross center"});
            using var response=await client.SendAsync(wrong);
            Assert.Equal(HttpStatusCode.Forbidden,response.StatusCode);
        }
    }

    [Fact]
    public async Task Signed_jwt_role_does_not_grant_session_occurrence_or_attendance()
    {
        using var fixture=new SignedJwtFixture();
        await using var app=fixture.Server();
        using var client=app.CreateClient();
        var id=Guid.NewGuid();
        foreach(var kind in new []{"occurrence-claims","attendance-claims"})
        {
            var route="/api/v1/centers/center-a/class-sessions/"+id+"/"+kind;
            using(var anonymous=Request(HttpMethod.Post,route,null))
            {
                anonymous.Content=kind=="occurrence-claims" ?
                    JsonContent.Create(new {occurrenceReportId=Guid.NewGuid(),reportedNote="Reported"}) :
                    JsonContent.Create(new {claimId=Guid.NewGuid(),occurrenceReportId=Guid.NewGuid(),
                        participantRef="synthetic",reportedPresence="REPORTED_PRESENT"});
                using var response=await client.SendAsync(anonymous);
                Assert.Equal(HttpStatusCode.Unauthorized,response.StatusCode);
            }
            using(var admin=Request(HttpMethod.Post,route,fixture.Token(roleClaim:true)))
            {
                admin.Content=kind=="occurrence-claims" ?
                    JsonContent.Create(new {occurrenceReportId=Guid.NewGuid(),reportedNote="Reported"}) :
                    JsonContent.Create(new {claimId=Guid.NewGuid(),occurrenceReportId=Guid.NewGuid(),
                        participantRef="synthetic",reportedPresence="REPORTED_PRESENT"});
                using var response=await client.SendAsync(admin);
                Assert.Equal(HttpStatusCode.Forbidden,response.StatusCode);
            }
        }
    }

    [Fact]
    public async Task Signed_jwt_role_does_not_authorize_occurrence_or_attendance_history()
    {
        using var fixture=new SignedJwtFixture();
        await using var app=fixture.Server();
        using var client=app.CreateClient();
        var session=Guid.NewGuid();
        var prefix="/api/v1/centers/center-a/class-sessions/"+session+"/occurrence-claims";
        foreach(var route in new[]{
            prefix,prefix+"/"+Guid.NewGuid()+"/attendance-claims"
        })
        {
            using(var anonymous=Request(HttpMethod.Get,route,null))
            using(var result=await client.SendAsync(anonymous))
                Assert.Equal(HttpStatusCode.Unauthorized,result.StatusCode);
            using(var signed=Request(HttpMethod.Get,route,fixture.Token(roleClaim:true)))
            using(var result=await client.SendAsync(signed))
                Assert.Equal(HttpStatusCode.Forbidden,result.StatusCode);
        }
    }

    [Fact]
    public async Task Signed_admin_cannot_read_or_write_private_navigation_without_independent_grants()
    {
        using var fixture=new SignedJwtFixture();
        await using var app=fixture.Server();
        using var client=app.CreateClient();
        foreach(var route in new[]{
            "/api/v1/centers/center-a/site/navigation",
            "/api/v1/centers/center-a/site/navigation/revisions"
        })
        {
            using var anonymous=Request(HttpMethod.Get,route,null);
            using var anonymousReply=await client.SendAsync(anonymous);
            Assert.Equal(HttpStatusCode.Unauthorized,anonymousReply.StatusCode);
            using var signed=Request(HttpMethod.Get,route,fixture.Token(roleClaim:true));
            using var signedReply=await client.SendAsync(signed);
            Assert.Equal(HttpStatusCode.Forbidden,signedReply.StatusCode);
        }
        using(var request=Request(HttpMethod.Post,
            "/api/v1/centers/center-a/site/navigation/revisions",fixture.Token(roleClaim:true)))
        {
            request.Content=JsonContent.Create(new {expectedRevision=0,items=Array.Empty<object>()});
            using var response=await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.Forbidden,response.StatusCode);
        }
    }

    [Fact]
    public async Task Signed_superadmin_without_independent_work_card_grants_is_denied()
    {
        using var fixture=new SignedJwtFixture();
        await using var app=fixture.Server();
        using var client=app.CreateClient();
        foreach(var path in new[]{
            "/api/v1/centers/center-a/operations/work-cards",
            "/api/v1/centers/center-a/operations/work-cards/"+Guid.NewGuid()+"/revisions"
        })
        {
            using var anonymous=Request(HttpMethod.Get,path,null);
            using var rejected=await client.SendAsync(anonymous);
            Assert.Equal(HttpStatusCode.Unauthorized,rejected.StatusCode);
            using var signed=Request(HttpMethod.Get,path,fixture.Token(roleClaim:true));
            using var response=await client.SendAsync(signed);
            Assert.Equal(HttpStatusCode.Forbidden,response.StatusCode);
        }
        using var write=Request(HttpMethod.Post,
            "/api/v1/centers/center-a/operations/work-cards/revisions",
            fixture.Token(roleClaim:true));
        write.Content=JsonContent.Create(new {workCardId=Guid.NewGuid(),expectedRevision=0,
            draftId=Guid.NewGuid(),pinnedPlanRevision=1,title="Test only",planningNote="",
            plannedDueAt=(DateTimeOffset?)null});
        using var writeResponse=await client.SendAsync(write);
        Assert.Equal(HttpStatusCode.Forbidden,writeResponse.StatusCode);
    }

    [Fact]
    public async Task Signed_superadmin_cannot_send_or_read_private_collaboration_without_independent_rights()
    {
        using var fixture=new SignedJwtFixture();
        await using var app=fixture.Server();
        using var client=app.CreateClient();
        foreach(var route in new[]{
            "/api/v1/centers/center-a/collaboration/outgoing",
            "/api/v1/centers/center-b/collaboration/incoming"
        })
        {
            using var anonymous=Request(HttpMethod.Get,route,null);
            using var anonymousResult=await client.SendAsync(anonymous);
            Assert.Equal(HttpStatusCode.Unauthorized,anonymousResult.StatusCode);
            using var superadmin=Request(HttpMethod.Get,route,fixture.Token(roleClaim:true));
            using var signedResult=await client.SendAsync(superadmin);
            Assert.Equal(HttpStatusCode.Forbidden,signedResult.StatusCode);
        }
        using var propose=Request(HttpMethod.Post,
          "/api/v1/centers/center-a/collaboration/proposals",
          fixture.Token(roleClaim:true));
        propose.Content=JsonContent.Create(new {
            proposalId=Guid.NewGuid(),targetCenterId="center-b",
            title="Synthetic collaboration",summary="No business grant"
        });
        using var reject=await client.SendAsync(propose);
        Assert.Equal(HttpStatusCode.Forbidden,reject.StatusCode);
    }

    [Fact]
    public async Task Signed_admin_cannot_change_or_read_private_session_preparation_without_two_grants()
    {
        using var fixture=new SignedJwtFixture();
        await using var app=fixture.Server();
        using var client=app.CreateClient();
        foreach(var route in new[]{
            "/api/v1/centers/center-a/learning/session-preparations",
            "/api/v1/centers/center-a/learning/session-preparations/"+Guid.NewGuid()+"/revisions"
        })
        {
            using var anonymous=Request(HttpMethod.Get,route,null);
            using var anonymousResult=await client.SendAsync(anonymous);
            Assert.Equal(HttpStatusCode.Unauthorized,anonymousResult.StatusCode);
            using var role=Request(HttpMethod.Get,route,fixture.Token(roleClaim:true));
            using var denied=await client.SendAsync(role);
            Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);
        }
        using var mutation=Request(HttpMethod.Post,
           "/api/v1/centers/center-a/learning/session-preparations/revisions",
           fixture.Token(roleClaim:true));
        mutation.Content=JsonContent.Create(new{
            sessionId=Guid.NewGuid(),classId=Guid.NewGuid(),expectedRevision=0,
            title="Private test lesson",teachingOutline="Only planning",materialsNote=""
        });
        using var answer=await client.SendAsync(mutation);
        Assert.Equal(HttpStatusCode.Forbidden,answer.StatusCode);
    }

    [Fact]
    public async Task Signed_superadmin_lacks_private_recipient_notes_rights()
    {
        using var fixture=new SignedJwtFixture();
        await using var app=fixture.Server();
        using var client=app.CreateClient();
        var id=Guid.NewGuid();
        var route="/api/v1/centers/center-b/collaboration/proposals/"+id+"/recipient-notes";
        using(var anonymous=Request(HttpMethod.Get,route,null))
        {
            using var response=await client.SendAsync(anonymous);
            Assert.Equal(HttpStatusCode.Unauthorized,response.StatusCode);
        }
        using(var actor=Request(HttpMethod.Get,route,fixture.Token(roleClaim:true)))
        {
            using var response=await client.SendAsync(actor);
            Assert.Equal(HttpStatusCode.Forbidden,response.StatusCode);
        }
        using var post=Request(HttpMethod.Post,route,fixture.Token(roleClaim:true));
        post.Content=JsonContent.Create(new {noteId=Guid.NewGuid(),noteText="Internal only"});
        using var rejected=await client.SendAsync(post);
        Assert.Equal(HttpStatusCode.Forbidden,rejected.StatusCode);
    }

    [Fact]
    public async Task Signed_superadmin_without_all_three_site_read_rights_cannot_see_assembled_preview()
    {
        using var fixture=new SignedJwtFixture();
        await using var app=fixture.Server();
        using var client=app.CreateClient();
        var path="/api/v1/centers/center-a/site/assembled-preview";
        using(var anonymous=Request(HttpMethod.Get,path,null))
        {
            using var response=await client.SendAsync(anonymous);
            Assert.Equal(HttpStatusCode.Unauthorized,response.StatusCode);
        }
        using var elevated=Request(HttpMethod.Get,path,fixture.Token(roleClaim:true));
        using var denied=await client.SendAsync(elevated);
        Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);
    }

    [Fact]
    public async Task Signed_role_only_superadmin_has_no_private_page_template_rights()
    {
        using var fixture=new SignedJwtFixture();
        await using var app=fixture.Server();
        using var client=app.CreateClient();
        var paths=new[]{
            "/api/v1/centers/center-a/site/templates",
            "/api/v1/centers/center-a/site/templates/"+Guid.NewGuid()+"/revisions"
        };
        foreach(var path in paths)
        {
            using var anonymous=Request(HttpMethod.Get,path,null);
            using var unauthenticated=await client.SendAsync(anonymous);
            Assert.Equal(HttpStatusCode.Unauthorized,unauthenticated.StatusCode);
            using var elevated=Request(HttpMethod.Get,path,fixture.Token(roleClaim:true));
            using var denied=await client.SendAsync(elevated);
            Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);
        }
        using var submit=Request(HttpMethod.Post,
          "/api/v1/centers/center-a/site/templates/revisions",fixture.Token(roleClaim:true));
        submit.Content=JsonContent.Create(new {
          templateId=Guid.NewGuid(),sourcePageId=Guid.NewGuid(),
          expectedRevision=0,pinnedPageRevision=1,label="Private template"
        });
        using var rejected=await client.SendAsync(submit);
        Assert.Equal(HttpStatusCode.Forbidden,rejected.StatusCode);
    }

    private sealed class TestSessionClaimWriteGrant : ICenterSessionClaimWriteAuthorization
    {
        public ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal user,string center,
            string operation,Guid session,CancellationToken ct) => ValueTask.FromResult(true);
    }

    private sealed class TestLearningWriteGrant : ICenterLearningPlanWriteAuthorization
    {
        public ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal principal,string centerId,
            string operation,Guid resourceId,CancellationToken ct) =>
            ValueTask.FromResult(true);
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
