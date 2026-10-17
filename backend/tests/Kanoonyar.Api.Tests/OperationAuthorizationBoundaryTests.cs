using Kanoonyar.Api.Programs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Kanoonyar.Api.Tests;

/// <summary>
/// Version-pinned inventory of every authenticated operational route on
/// main after Sprint 50. Adding an API must deliberately update this audit
/// manifest and never silently expose a new unauthenticated operation.
/// </summary>
public sealed class OperationAuthorizationBoundaryTests
{
    private static readonly string[] ExpectedAuthenticatedRoutes = """
GET /api/v1/centers/{centerId}/activity-sources/{sourceKind}
GET /api/v1/centers/{centerId}/campaigns/drafts
GET /api/v1/centers/{centerId}/campaigns/drafts/{campaignId:guid}/revisions
GET /api/v1/centers/{centerId}/campaigns/{campaignId:guid}/evidence-register
GET /api/v1/centers/{centerId}/campaigns/{campaignId:guid}/report-links
GET /api/v1/centers/{centerId}/campaigns/{campaignId:guid}/report-links/{associationId:guid}/inspection-notes
GET /api/v1/centers/{centerId}/campaigns/{campaignId:guid}/report-links/{associationId:guid}/inspection-notes/{noteId:guid}/followups
GET /api/v1/centers/{centerId}/campaigns/{campaignId:guid}/report-links/{associationId:guid}/review-workspace
GET /api/v1/centers/{centerId}/class-sessions/{sessionId:guid}/occurrence-claims
GET /api/v1/centers/{centerId}/class-sessions/{sessionId:guid}/occurrence-claims/{occurrenceReportId:guid}/attendance-claims
GET /api/v1/centers/{centerId}/class-sessions/{sessionId:guid}/report-lineage
GET /api/v1/centers/{centerId}/classes/catalogue
GET /api/v1/centers/{centerId}/classes/{classId:guid}/intake
GET /api/v1/centers/{centerId}/classes/{classId:guid}/registration-intents/{requestId:guid}/review-requests
GET /api/v1/centers/{centerId}/classes/{classId:guid}/source-sessions
GET /api/v1/centers/{centerId}/collaboration/incoming
GET /api/v1/centers/{centerId}/collaboration/outgoing
GET /api/v1/centers/{centerId}/collaboration/proposals/{proposalId:guid}/recipient-notes
GET /api/v1/centers/{centerId}/events/plans
GET /api/v1/centers/{centerId}/events/plans/{eventId:guid}/revisions
GET /api/v1/centers/{centerId}/events/plans/{eventId:guid}/source-dossier
GET /api/v1/centers/{centerId}/learning/session-preparations
GET /api/v1/centers/{centerId}/learning/session-preparations/{sessionId:guid}/revisions
GET /api/v1/centers/{centerId}/local-plan-drafts
GET /api/v1/centers/{centerId}/local-plan-drafts/{draftId:guid}/activity-reports
GET /api/v1/centers/{centerId}/local-plan-drafts/{draftId:guid}/execution-trace
GET /api/v1/centers/{centerId}/local-plan-drafts/{draftId:guid}/revisions
GET /api/v1/centers/{centerId}/operations/local-plan-workspace/{draftId:guid}/{planRevision:long}
GET /api/v1/centers/{centerId}/operations/work-cards
GET /api/v1/centers/{centerId}/operations/work-cards/{workCardId:guid}/resource-needs
GET /api/v1/centers/{centerId}/operations/work-cards/{workCardId:guid}/resource-needs/{needId:guid}/revisions
GET /api/v1/centers/{centerId}/operations/work-cards/{workCardId:guid}/revisions
GET /api/v1/centers/{centerId}/operations/work-cards/{workCardId:guid}/session-links
GET /api/v1/centers/{centerId}/schedule/private
GET /api/v1/centers/{centerId}/site/assembled-preview
GET /api/v1/centers/{centerId}/site/design
GET /api/v1/centers/{centerId}/site/design/revisions
GET /api/v1/centers/{centerId}/site/drafts
GET /api/v1/centers/{centerId}/site/drafts/{pageId:guid}/revisions
GET /api/v1/centers/{centerId}/site/forms/drafts
GET /api/v1/centers/{centerId}/site/forms/drafts/{formId:guid}/revisions
GET /api/v1/centers/{centerId}/site/navigation
GET /api/v1/centers/{centerId}/site/navigation/revisions
GET /api/v1/centers/{centerId}/site/templates
GET /api/v1/centers/{centerId}/site/templates/{templateId:guid}/revisions
GET /api/v1/headquarters/national-issuance/review-rollup
GET /api/v1/headquarters/national-issuance/review-rollup/export.csv
GET /api/v1/headquarters/national-issuance/review-dossier
GET /api/v1/identity/center-memberships
GET /api/v1/identity/me
POST /api/v1/centers/{centerId}/activity-reports/sourced
POST /api/v1/centers/{centerId}/campaigns/drafts/revisions
POST /api/v1/centers/{centerId}/campaigns/{campaignId:guid}/report-links
POST /api/v1/centers/{centerId}/campaigns/{campaignId:guid}/report-links/{associationId:guid}/inspection-notes
POST /api/v1/centers/{centerId}/campaigns/{campaignId:guid}/report-links/{associationId:guid}/inspection-notes/{noteId:guid}/followups
POST /api/v1/centers/{centerId}/class-sessions/{sessionId:guid}/attendance-claims
POST /api/v1/centers/{centerId}/class-sessions/{sessionId:guid}/occurrence-claims
POST /api/v1/centers/{centerId}/classes
POST /api/v1/centers/{centerId}/classes/sessions
POST /api/v1/centers/{centerId}/classes/{classId:guid}/capacity-revisions
POST /api/v1/centers/{centerId}/classes/{classId:guid}/registration-intents
POST /api/v1/centers/{centerId}/classes/{classId:guid}/registration-intents/{requestId:guid}/review-requests
POST /api/v1/centers/{centerId}/collaboration/proposals
POST /api/v1/centers/{centerId}/collaboration/proposals/{proposalId:guid}/recipient-notes
POST /api/v1/centers/{centerId}/events/plans/revisions
POST /api/v1/centers/{centerId}/learning/session-preparations/revisions
POST /api/v1/centers/{centerId}/local-plan-drafts/revisions
POST /api/v1/centers/{centerId}/operations/work-cards/revisions
POST /api/v1/centers/{centerId}/operations/work-cards/{workCardId:guid}/resource-needs/revisions
POST /api/v1/centers/{centerId}/operations/work-cards/{workCardId:guid}/session-links
POST /api/v1/centers/{centerId}/site/design/revisions
POST /api/v1/centers/{centerId}/site/drafts/revisions
POST /api/v1/centers/{centerId}/site/forms/drafts/revisions
POST /api/v1/centers/{centerId}/site/navigation/revisions
POST /api/v1/centers/{centerId}/site/templates/revisions
""".Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static WebApplicationFactory<Program> Create(bool configured)
    {
        var factory = new WebApplicationFactory<Program>();
        if (!configured) return factory;
        return factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("OIDC_AUTHORITY", "https://kanoonyar.s51.invalid/issuer");
            builder.UseSetting("OIDC_AUDIENCE", "kanoonyar-s51-test");
        });
    }

    private static RouteEndpoint[] Endpoints(WebApplicationFactory<Program> factory)
    {
        using var client = factory.CreateClient();
        return factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText?.StartsWith("/api/v1/", StringComparison.Ordinal) == true)
            .ToArray();
    }

    private static string[] Inventory(IEnumerable<RouteEndpoint> endpoints)
    {
        return endpoints.SelectMany(endpoint =>
        {
            var path = endpoint.RoutePattern.RawText!;
            var methods = endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ??
                throw new InvalidOperationException("API route has no method metadata: " + path);
            return methods.Select(method => method + " " + path);
        }).OrderBy(x => x, StringComparer.Ordinal).ToArray();
    }

    [Fact]
    public void All_configured_operational_routes_require_signed_identity_and_are_in_reviewed_inventory()
    {
        using var factory = Create(configured: true);
        var endpoints = Endpoints(factory);
        var expected = ExpectedAuthenticatedRoutes.OrderBy(x => x, StringComparer.Ordinal).ToArray();
        var actual = Inventory(endpoints);
        Assert.Equal(expected.Concat(new[] { "GET /api/v1/programs/published",
            "GET /api/v1/programs/published/version" })
            .OrderBy(x => x, StringComparer.Ordinal), actual);
        Assert.Equal(expected.Length + 2, endpoints.Length);

        foreach (var endpoint in endpoints)
        {
            if (endpoint.RoutePattern.RawText is "/api/v1/programs/published" or
                "/api/v1/programs/published/version")
            {
                Assert.Null(endpoint.Metadata.GetMetadata<IAuthorizeData>());
                continue;
            }
            Assert.NotNull(endpoint.Metadata.GetMetadata<IAuthorizeData>());
            Assert.Null(endpoint.Metadata.GetMetadata<IAllowAnonymous>());
        }

        // Every presently wired production operation grant adapter must remain
        // its explicit default-deny implementation. Grant approval is not a
        // side effect of introducing this evidence/audit contract.
        var services = factory.Services;
        Assert.IsType<UnapprovedCenterClassCatalogueAuthorization>(
            services.GetRequiredService<ICenterClassCatalogueAuthorization>());
        Assert.IsType<UnapprovedCenterSourceCatalogReadAuthorization>(
            services.GetRequiredService<ICenterSourceCatalogReadAuthorization>());
        Assert.IsType<UnapprovedCenterLearningPlanWriteAuthorization>(
            services.GetRequiredService<ICenterLearningPlanWriteAuthorization>());
        Assert.IsType<UnapprovedCenterCampaignAuthorization>(
            services.GetRequiredService<ICenterCampaignAuthorization>());
        Assert.IsType<UnapprovedCenterCollaborationAuthorization>(
            services.GetRequiredService<ICenterCollaborationAuthorization>());
        Assert.IsType<UnapprovedCenterEventPlanAuthorization>(
            services.GetRequiredService<ICenterEventPlanAuthorization>());
        Assert.IsType<UnapprovedLocalPlanDraftReadAuthorization>(
            services.GetRequiredService<ILocalPlanDraftReadAuthorization>());
        Assert.IsType<UnapprovedLocalPlanDraftWriteAuthorization>(
            services.GetRequiredService<ILocalPlanDraftWriteAuthorization>());
        Assert.IsType<UnapprovedNationalInspectionReadAuthorization>(
            services.GetRequiredService<INationalInspectionReadAuthorization>());
    }

    [Fact]
    public async Task Every_operational_route_fails_closed_when_identity_provider_is_unconfigured()
    {
        using var factory = Create(configured: false);
        var endpoints = Endpoints(factory);
        var expected = ExpectedAuthenticatedRoutes.OrderBy(x => x, StringComparer.Ordinal).ToArray();
        var expectedWithoutOidcOnlyRoute = expected.Where(route =>
            route != "GET /api/v1/headquarters/national-issuance/review-dossier").ToArray();
        var actual = Inventory(endpoints);
        Assert.Equal(expectedWithoutOidcOnlyRoute.Concat(new[] { "GET /api/v1/programs/published",
            "GET /api/v1/programs/published/version" })
            .OrderBy(x => x, StringComparer.Ordinal), actual);

        using var client = factory.CreateClient();
        foreach (var route in expectedWithoutOidcOnlyRoute)
        {
            var separator = route.IndexOf(' ');
            var method = route[..separator];
            var path = route[(separator + 1)..];
            path = System.Text.RegularExpressions.Regex.Replace(path,
                @"\{[^}]+\}", m => m.Value.Contains(":guid", StringComparison.Ordinal) ?
                    "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa" :
                    m.Value.Contains(":long", StringComparison.Ordinal) ? "1" :
                    m.Value.Contains("sourceKind", StringComparison.Ordinal) ? "class-sessions" :
                    "s51-test-center");
            using var request = new HttpRequestMessage(new HttpMethod(method), path);
            using var response = await client.SendAsync(request);
            Assert.Equal(System.Net.HttpStatusCode.ServiceUnavailable, response.StatusCode);
        }
    }
}
