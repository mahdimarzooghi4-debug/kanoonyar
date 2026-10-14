using Kanoonyar.Api.Security;
using Kanoonyar.Api.Programs;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddHealthChecks();
builder.Services.AddProblemDetails();
var oidcConfigured = builder.AddConfiguredOidc();

builder.Services.AddSingleton<ILocalPlanDraftWriteAuthorization, UnapprovedLocalPlanDraftWriteAuthorization>();
builder.Services.AddSingleton<ILocalPlanDraftReadAuthorization, UnapprovedLocalPlanDraftReadAuthorization>();
builder.Services.AddSingleton<ICenterActivityReportReadAuthorization, UnapprovedCenterActivityReportReadAuthorization>();
builder.Services.AddSingleton<ICenterSourceCatalogReadAuthorization, UnapprovedCenterSourceCatalogReadAuthorization>();
builder.Services.AddSingleton<ICenterClassCatalogueAuthorization, UnapprovedCenterClassCatalogueAuthorization>();
builder.Services.AddSingleton<ICenterLearningPlanWriteAuthorization, UnapprovedCenterLearningPlanWriteAuthorization>();
builder.Services.AddSingleton<ICenterSessionClaimWriteAuthorization, UnapprovedCenterSessionClaimWriteAuthorization>();
builder.Services.AddSingleton<ICenterSessionClaimHistoryAuthorization, UnapprovedCenterSessionClaimHistoryAuthorization>();
builder.Services.AddSingleton<ICenterSessionReportLineageAuthorization, UnapprovedCenterSessionReportLineageAuthorization>();
builder.Services.AddSingleton<ICenterSourcedActivityReportWriteAuthorization, UnapprovedCenterSourcedActivityReportWriteAuthorization>();
builder.Services.AddSingleton<ICenterClassIntakeAuthorization, UnapprovedCenterClassIntakeAuthorization>();
builder.Services.AddSingleton<ICenterRegistrationReviewAuthorization, UnapprovedCenterRegistrationReviewAuthorization>();
builder.Services.AddSingleton<ICenterSiteDraftAuthorization, UnapprovedCenterSiteDraftAuthorization>();
builder.Services.AddSingleton<ICenterSiteDesignAuthorization, UnapprovedCenterSiteDesignAuthorization>();
builder.Services.AddSingleton<ICenterSiteNavigationAuthorization, UnapprovedCenterSiteNavigationAuthorization>();
builder.Services.AddSingleton<ICenterPrivatePageTemplateAuthorization, UnapprovedPrivatePageTemplateAuthorization>();
builder.Services.AddSingleton<ICenterWorkCardAuthorization, UnapprovedCenterWorkCardAuthorization>();
builder.Services.AddSingleton<ICenterWorkResourceNeedAuthorization, UnapprovedWorkResourceNeedAuthorization>();
builder.Services.AddSingleton<IWorkCardSessionLinkAuthorization, UnapprovedWorkCardSessionLinkAuthorization>();
builder.Services.AddSingleton<ICenterCollaborationAuthorization, UnapprovedCenterCollaborationAuthorization>();
builder.Services.AddSingleton<ICenterSessionPreparationAuthorization, UnapprovedCenterSessionPreparationAuthorization>();
builder.Services.AddSingleton<ICenterFormDraftAuthorization, UnapprovedCenterFormDraftAuthorization>();
builder.Services.AddSingleton<ICenterCampaignAuthorization, UnapprovedCenterCampaignAuthorization>();
builder.Services.AddSingleton<ICenterEventPlanAuthorization, UnapprovedCenterEventPlanAuthorization>();
builder.Services.AddSingleton<ICenterCampaignReportLinkAuthorization, UnapprovedCenterCampaignReportLinkAuthorization>();

var app = builder.Build();
app.UseExceptionHandler();
if (oidcConfigured)
{
    app.UseAuthentication();
    app.UseAuthorization();
    app.MapGet("/api/v1/identity/me", AuthenticatedSubjectEndpoint.GetSubject).RequireAuthorization();
    app.MapGet("/api/v1/identity/center-memberships", CenterMembershipEndpoint.GetMine).RequireAuthorization();
    app.MapGet("/api/v1/centers/{centerId}/campaigns/{campaignId:guid}/evidence-register", CenterCampaignEvidenceRegisterEndpoint.Get).RequireAuthorization();
    app.MapGet("/api/v1/centers/{centerId}/campaigns/{campaignId:guid}/report-links/{associationId:guid}/review-workspace", CenterCampaignReviewWorkspaceEndpoint.Get).RequireAuthorization();
    app.MapPost("/api/v1/centers/{centerId}/campaigns/{campaignId:guid}/report-links/{associationId:guid}/inspection-notes/{noteId:guid}/followups", CenterCampaignFollowupEndpoint.Append).RequireAuthorization();
    app.MapGet("/api/v1/centers/{centerId}/campaigns/{campaignId:guid}/report-links/{associationId:guid}/inspection-notes/{noteId:guid}/followups", CenterCampaignFollowupEndpoint.List).RequireAuthorization();
    app.MapPost("/api/v1/centers/{centerId}/campaigns/{campaignId:guid}/report-links/{associationId:guid}/inspection-notes", CenterCampaignInspectionNotesEndpoint.Append).RequireAuthorization();
    app.MapGet("/api/v1/centers/{centerId}/campaigns/{campaignId:guid}/report-links/{associationId:guid}/inspection-notes", CenterCampaignInspectionNotesEndpoint.List).RequireAuthorization();
    app.MapPost("/api/v1/centers/{centerId}/campaigns/{campaignId:guid}/report-links", CenterCampaignReportLinkEndpoint.Link).RequireAuthorization();
    app.MapGet("/api/v1/centers/{centerId}/campaigns/{campaignId:guid}/report-links", CenterCampaignReportLinkEndpoint.List).RequireAuthorization();
    app.MapGet("/api/v1/centers/{centerId}/schedule/private", CenterPrivateCalendarEndpoint.Get).RequireAuthorization();
    app.MapGet("/api/v1/centers/{centerId}/events/plans/{eventId:guid}/source-dossier", CenterEventSourceDossierEndpoint.Get).RequireAuthorization();
    app.MapPost("/api/v1/centers/{centerId}/events/plans/revisions", CenterEventPlanEndpoint.Append).RequireAuthorization();
    app.MapGet("/api/v1/centers/{centerId}/events/plans", CenterEventPlanEndpoint.List).RequireAuthorization();
    app.MapGet("/api/v1/centers/{centerId}/events/plans/{eventId:guid}/revisions", CenterEventPlanEndpoint.History).RequireAuthorization();
    app.MapPost("/api/v1/centers/{centerId}/campaigns/drafts/revisions", CenterCampaignEndpoint.Append).RequireAuthorization();
    app.MapGet("/api/v1/centers/{centerId}/campaigns/drafts", CenterCampaignEndpoint.List).RequireAuthorization();
    app.MapGet("/api/v1/centers/{centerId}/campaigns/drafts/{campaignId:guid}/revisions", CenterCampaignEndpoint.History).RequireAuthorization();
    app.MapPost("/api/v1/centers/{centerId}/site/forms/drafts/revisions", CenterFormDraftEndpoint.Append).RequireAuthorization();
    app.MapGet("/api/v1/centers/{centerId}/site/forms/drafts", CenterFormDraftEndpoint.List).RequireAuthorization();
    app.MapGet("/api/v1/centers/{centerId}/site/forms/drafts/{formId:guid}/revisions", CenterFormDraftEndpoint.History).RequireAuthorization();
    app.MapPost("/api/v1/centers/{centerId}/learning/session-preparations/revisions", CenterSessionPreparationEndpoint.Append).RequireAuthorization();
    app.MapGet("/api/v1/centers/{centerId}/learning/session-preparations", CenterSessionPreparationEndpoint.List).RequireAuthorization();
    app.MapGet("/api/v1/centers/{centerId}/learning/session-preparations/{sessionId:guid}/revisions", CenterSessionPreparationEndpoint.History).RequireAuthorization();
    app.MapPost("/api/v1/centers/{centerId}/collaboration/proposals/{proposalId:guid}/recipient-notes", CenterRecipientPrivateNotesEndpoint.Append).RequireAuthorization();
    app.MapGet("/api/v1/centers/{centerId}/collaboration/proposals/{proposalId:guid}/recipient-notes", CenterRecipientPrivateNotesEndpoint.List).RequireAuthorization();
    app.MapPost("/api/v1/centers/{centerId}/collaboration/proposals", CenterCollaborationEndpoint.Submit).RequireAuthorization();
    app.MapGet("/api/v1/centers/{centerId}/collaboration/outgoing", CenterCollaborationEndpoint.Outgoing).RequireAuthorization();
    app.MapGet("/api/v1/centers/{centerId}/collaboration/incoming", CenterCollaborationEndpoint.Incoming).RequireAuthorization();
    app.MapPost("/api/v1/centers/{centerId}/operations/work-cards/{workCardId:guid}/resource-needs/revisions", CenterWorkResourceNeedEndpoint.Append).RequireAuthorization();
    app.MapGet("/api/v1/centers/{centerId}/operations/work-cards/{workCardId:guid}/resource-needs", CenterWorkResourceNeedEndpoint.List).RequireAuthorization();
    app.MapGet("/api/v1/centers/{centerId}/operations/work-cards/{workCardId:guid}/resource-needs/{needId:guid}/revisions", CenterWorkResourceNeedEndpoint.History).RequireAuthorization();
    app.MapPost("/api/v1/centers/{centerId}/operations/work-cards/revisions", CenterWorkCardEndpoint.Append).RequireAuthorization();
    app.MapPost("/api/v1/centers/{centerId}/operations/work-cards/{workCardId:guid}/session-links", CenterWorkSessionLinkEndpoint.Append).RequireAuthorization();
    app.MapGet("/api/v1/centers/{centerId}/operations/work-cards/{workCardId:guid}/session-links", CenterWorkSessionLinkEndpoint.List).RequireAuthorization();
    app.MapGet("/api/v1/centers/{centerId}/operations/local-plan-workspace/{draftId:guid}/{planRevision:long}", CenterLocalPlanWorkspaceEndpoint.Get).RequireAuthorization();
    app.MapGet("/api/v1/centers/{centerId}/operations/work-cards", CenterWorkCardEndpoint.List).RequireAuthorization();
    app.MapGet("/api/v1/centers/{centerId}/operations/work-cards/{workCardId:guid}/revisions", CenterWorkCardEndpoint.History).RequireAuthorization();
    app.MapPost("/api/v1/centers/{centerId}/site/navigation/revisions", CenterSiteNavigationEndpoint.Append).RequireAuthorization();
    app.MapPost("/api/v1/centers/{centerId}/site/templates/revisions", CenterPrivatePageTemplateEndpoint.Append).RequireAuthorization();
    app.MapGet("/api/v1/centers/{centerId}/site/templates", CenterPrivatePageTemplateEndpoint.List).RequireAuthorization();
    app.MapGet("/api/v1/centers/{centerId}/site/templates/{templateId:guid}/revisions", CenterPrivatePageTemplateEndpoint.History).RequireAuthorization();
    app.MapGet("/api/v1/centers/{centerId}/site/assembled-preview", CenterAssembledSitePreviewEndpoint.Read).RequireAuthorization();
    app.MapGet("/api/v1/centers/{centerId}/site/navigation", CenterSiteNavigationEndpoint.Latest).RequireAuthorization();
    app.MapGet("/api/v1/centers/{centerId}/site/navigation/revisions", CenterSiteNavigationEndpoint.History).RequireAuthorization();
    app.MapPost("/api/v1/centers/{centerId}/site/design/revisions", CenterSiteDesignEndpoint.Append).RequireAuthorization();
    app.MapGet("/api/v1/centers/{centerId}/site/design", CenterSiteDesignEndpoint.Latest).RequireAuthorization();
    app.MapGet("/api/v1/centers/{centerId}/site/design/revisions", CenterSiteDesignEndpoint.History).RequireAuthorization();
    app.MapPost("/api/v1/centers/{centerId}/site/drafts/revisions", CenterSiteDraftEndpoint.Append).RequireAuthorization();
    app.MapGet("/api/v1/centers/{centerId}/site/drafts", CenterSiteDraftEndpoint.List).RequireAuthorization();
    app.MapGet("/api/v1/centers/{centerId}/site/drafts/{pageId:guid}/revisions", CenterSiteDraftEndpoint.History).RequireAuthorization();
    app.MapGet("/api/v1/centers/{centerId}/local-plan-drafts", LocalPlanDraftReadEndpoint.ListMine).RequireAuthorization();
    app.MapGet("/api/v1/centers/{centerId}/local-plan-drafts/{draftId:guid}/revisions", LocalPlanDraftHistoryEndpoint.ListHistory).RequireAuthorization();
    app.MapGet("/api/v1/centers/{centerId}/local-plan-drafts/{draftId:guid}/execution-trace", CenterExecutionTraceEndpoint.GetTrace).RequireAuthorization();
    app.MapGet("/api/v1/centers/{centerId}/local-plan-drafts/{draftId:guid}/activity-reports", CenterActivityReportReadEndpoint.ListReports).RequireAuthorization();
    app.MapGet("/api/v1/centers/{centerId}/activity-sources/{sourceKind}", CenterSourceCatalogEndpoint.ListSources).RequireAuthorization();
    app.MapGet("/api/v1/centers/{centerId}/classes/catalogue", CenterClassCatalogueEndpoint.List).RequireAuthorization();
    app.MapGet("/api/v1/centers/{centerId}/classes/{classId:guid}/source-sessions", CenterClassSessionsEndpoint.List).RequireAuthorization();
    app.MapPost("/api/v1/centers/{centerId}/local-plan-drafts/revisions", LocalPlanDraftWriteEndpoint.Append).RequireAuthorization();
    app.MapPost("/api/v1/centers/{centerId}/classes", CenterLearningPlanWriteEndpoint.CreateClass).RequireAuthorization();
    app.MapPost("/api/v1/centers/{centerId}/classes/sessions", CenterLearningPlanWriteEndpoint.ScheduleSession).RequireAuthorization();
    app.MapPost("/api/v1/centers/{centerId}/class-sessions/{sessionId:guid}/occurrence-claims", CenterSessionClaimWriteEndpoint.RecordOccurrence).RequireAuthorization();
    app.MapPost("/api/v1/centers/{centerId}/class-sessions/{sessionId:guid}/attendance-claims", CenterSessionClaimWriteEndpoint.RecordAttendance).RequireAuthorization();
    app.MapPost("/api/v1/centers/{centerId}/classes/{classId:guid}/capacity-revisions", CenterClassIntakeEndpoint.SetCapacity).RequireAuthorization();
    app.MapPost("/api/v1/centers/{centerId}/classes/{classId:guid}/registration-intents", CenterClassIntakeEndpoint.RequestRegistration).RequireAuthorization();
    app.MapPost("/api/v1/centers/{centerId}/classes/{classId:guid}/registration-intents/{requestId:guid}/review-requests", CenterRegistrationReviewEndpoint.Raise).RequireAuthorization();
    app.MapGet("/api/v1/centers/{centerId}/classes/{classId:guid}/registration-intents/{requestId:guid}/review-requests", CenterRegistrationReviewEndpoint.List).RequireAuthorization();
    app.MapGet("/api/v1/centers/{centerId}/classes/{classId:guid}/intake", CenterClassIntakeEndpoint.Read).RequireAuthorization();
    app.MapPost("/api/v1/centers/{centerId}/activity-reports/sourced", CenterSourcedActivityReportWriteEndpoint.Record).RequireAuthorization();
    app.MapGet("/api/v1/centers/{centerId}/class-sessions/{sessionId:guid}/report-lineage", CenterSessionReportLineageEndpoint.Get).RequireAuthorization();
    app.MapGet("/api/v1/centers/{centerId}/class-sessions/{sessionId:guid}/occurrence-claims", CenterSessionClaimHistoryEndpoint.Occurrences).RequireAuthorization();
    app.MapGet("/api/v1/centers/{centerId}/class-sessions/{sessionId:guid}/occurrence-claims/{occurrenceReportId:guid}/attendance-claims", CenterSessionClaimHistoryEndpoint.Attendance).RequireAuthorization();
}
else
{
    app.MapGet("/api/v1/identity/me", () =>
        Results.Problem(statusCode: 503, title: "OIDC not configured"));
    app.MapGet("/api/v1/identity/center-memberships", () =>
        Results.Problem(statusCode: 503, title: "OIDC not configured"));
    app.MapGet("/api/v1/centers/{centerId}/campaigns/{campaignId:guid}/evidence-register", () => Results.Problem(statusCode:503,title:"OIDC not configured"));
    app.MapGet("/api/v1/centers/{centerId}/campaigns/{campaignId:guid}/report-links/{associationId:guid}/review-workspace", () => Results.Problem(statusCode:503,title:"OIDC not configured"));
    app.MapPost("/api/v1/centers/{centerId}/campaigns/{campaignId:guid}/report-links/{associationId:guid}/inspection-notes/{noteId:guid}/followups", () => Results.Problem(statusCode:503,title:"OIDC not configured"));
    app.MapGet("/api/v1/centers/{centerId}/campaigns/{campaignId:guid}/report-links/{associationId:guid}/inspection-notes/{noteId:guid}/followups", () => Results.Problem(statusCode:503,title:"OIDC not configured"));
    app.MapPost("/api/v1/centers/{centerId}/campaigns/{campaignId:guid}/report-links/{associationId:guid}/inspection-notes", () => Results.Problem(statusCode:503,title:"OIDC not configured"));
    app.MapGet("/api/v1/centers/{centerId}/campaigns/{campaignId:guid}/report-links/{associationId:guid}/inspection-notes", () => Results.Problem(statusCode:503,title:"OIDC not configured"));
    app.MapPost("/api/v1/centers/{centerId}/campaigns/{campaignId:guid}/report-links", () => Results.Problem(statusCode:503,title:"OIDC not configured"));
    app.MapGet("/api/v1/centers/{centerId}/campaigns/{campaignId:guid}/report-links", () => Results.Problem(statusCode:503,title:"OIDC not configured"));
    app.MapGet("/api/v1/centers/{centerId}/schedule/private", () => Results.Problem(statusCode:503,title:"OIDC not configured"));
    app.MapGet("/api/v1/centers/{centerId}/events/plans/{eventId:guid}/source-dossier", () => Results.Problem(statusCode:503,title:"OIDC not configured"));
    app.MapPost("/api/v1/centers/{centerId}/events/plans/revisions", () => Results.Problem(statusCode:503,title:"OIDC not configured"));
    app.MapGet("/api/v1/centers/{centerId}/events/plans", () => Results.Problem(statusCode:503,title:"OIDC not configured"));
    app.MapGet("/api/v1/centers/{centerId}/events/plans/{eventId:guid}/revisions", () => Results.Problem(statusCode:503,title:"OIDC not configured"));
    app.MapPost("/api/v1/centers/{centerId}/campaigns/drafts/revisions", () => Results.Problem(statusCode:503,title:"OIDC not configured"));
    app.MapGet("/api/v1/centers/{centerId}/campaigns/drafts", () => Results.Problem(statusCode:503,title:"OIDC not configured"));
    app.MapGet("/api/v1/centers/{centerId}/campaigns/drafts/{campaignId:guid}/revisions", () => Results.Problem(statusCode:503,title:"OIDC not configured"));
    app.MapPost("/api/v1/centers/{centerId}/site/forms/drafts/revisions", () => Results.Problem(statusCode:503,title:"OIDC not configured"));
    app.MapGet("/api/v1/centers/{centerId}/site/forms/drafts", () => Results.Problem(statusCode:503,title:"OIDC not configured"));
    app.MapGet("/api/v1/centers/{centerId}/site/forms/drafts/{formId:guid}/revisions", () => Results.Problem(statusCode:503,title:"OIDC not configured"));
    app.MapPost("/api/v1/centers/{centerId}/learning/session-preparations/revisions", () => Results.Problem(statusCode:503,title:"OIDC not configured"));
    app.MapGet("/api/v1/centers/{centerId}/learning/session-preparations", () => Results.Problem(statusCode:503,title:"OIDC not configured"));
    app.MapGet("/api/v1/centers/{centerId}/learning/session-preparations/{sessionId:guid}/revisions", () => Results.Problem(statusCode:503,title:"OIDC not configured"));
    app.MapPost("/api/v1/centers/{centerId}/collaboration/proposals/{proposalId:guid}/recipient-notes", () => Results.Problem(statusCode:503,title:"OIDC not configured"));
    app.MapGet("/api/v1/centers/{centerId}/collaboration/proposals/{proposalId:guid}/recipient-notes", () => Results.Problem(statusCode:503,title:"OIDC not configured"));
    app.MapPost("/api/v1/centers/{centerId}/collaboration/proposals", () => Results.Problem(statusCode:503,title:"OIDC not configured"));
    app.MapGet("/api/v1/centers/{centerId}/collaboration/outgoing", () => Results.Problem(statusCode:503,title:"OIDC not configured"));
    app.MapGet("/api/v1/centers/{centerId}/collaboration/incoming", () => Results.Problem(statusCode:503,title:"OIDC not configured"));
    app.MapPost("/api/v1/centers/{centerId}/operations/work-cards/{workCardId:guid}/resource-needs/revisions", () => Results.Problem(statusCode:503,title:"OIDC not configured"));
    app.MapGet("/api/v1/centers/{centerId}/operations/work-cards/{workCardId:guid}/resource-needs", () => Results.Problem(statusCode:503,title:"OIDC not configured"));
    app.MapGet("/api/v1/centers/{centerId}/operations/work-cards/{workCardId:guid}/resource-needs/{needId:guid}/revisions", () => Results.Problem(statusCode:503,title:"OIDC not configured"));
    app.MapPost("/api/v1/centers/{centerId}/operations/work-cards/revisions", () => Results.Problem(statusCode:503,title:"OIDC not configured"));
    app.MapPost("/api/v1/centers/{centerId}/operations/work-cards/{workCardId:guid}/session-links", () => Results.Problem(statusCode:503,title:"OIDC not configured"));
    app.MapGet("/api/v1/centers/{centerId}/operations/work-cards/{workCardId:guid}/session-links", () => Results.Problem(statusCode:503,title:"OIDC not configured"));
    app.MapGet("/api/v1/centers/{centerId}/operations/local-plan-workspace/{draftId:guid}/{planRevision:long}", () => Results.Problem(statusCode:503,title:"OIDC not configured"));
    app.MapGet("/api/v1/centers/{centerId}/operations/work-cards", () => Results.Problem(statusCode:503,title:"OIDC not configured"));
    app.MapGet("/api/v1/centers/{centerId}/operations/work-cards/{workCardId:guid}/revisions", () => Results.Problem(statusCode:503,title:"OIDC not configured"));
    app.MapPost("/api/v1/centers/{centerId}/site/navigation/revisions", () => Results.Problem(statusCode:503,title:"OIDC not configured"));
    app.MapPost("/api/v1/centers/{centerId}/site/templates/revisions", () => Results.Problem(statusCode:503,title:"OIDC not configured"));
    app.MapGet("/api/v1/centers/{centerId}/site/templates", () => Results.Problem(statusCode:503,title:"OIDC not configured"));
    app.MapGet("/api/v1/centers/{centerId}/site/templates/{templateId:guid}/revisions", () => Results.Problem(statusCode:503,title:"OIDC not configured"));
    app.MapGet("/api/v1/centers/{centerId}/site/assembled-preview", () => Results.Problem(statusCode:503,title:"OIDC not configured"));
    app.MapGet("/api/v1/centers/{centerId}/site/navigation", () => Results.Problem(statusCode:503,title:"OIDC not configured"));
    app.MapGet("/api/v1/centers/{centerId}/site/navigation/revisions", () => Results.Problem(statusCode:503,title:"OIDC not configured"));
    app.MapPost("/api/v1/centers/{centerId}/site/design/revisions", () => Results.Problem(statusCode:503,title:"OIDC not configured"));
    app.MapGet("/api/v1/centers/{centerId}/site/design", () => Results.Problem(statusCode:503,title:"OIDC not configured"));
    app.MapGet("/api/v1/centers/{centerId}/site/design/revisions", () => Results.Problem(statusCode:503,title:"OIDC not configured"));
    app.MapPost("/api/v1/centers/{centerId}/site/drafts/revisions", () => Results.Problem(statusCode:503,title:"OIDC not configured"));
    app.MapGet("/api/v1/centers/{centerId}/site/drafts", () => Results.Problem(statusCode:503,title:"OIDC not configured"));
    app.MapGet("/api/v1/centers/{centerId}/site/drafts/{pageId:guid}/revisions", () => Results.Problem(statusCode:503,title:"OIDC not configured"));
    app.MapGet("/api/v1/centers/{centerId}/local-plan-drafts", () =>
        Results.Problem(statusCode: 503, title: "OIDC not configured"));
    app.MapGet("/api/v1/centers/{centerId}/local-plan-drafts/{draftId:guid}/revisions", () =>
        Results.Problem(statusCode: 503, title: "OIDC not configured"));
    app.MapGet("/api/v1/centers/{centerId}/local-plan-drafts/{draftId:guid}/execution-trace", () =>
        Results.Problem(statusCode: 503, title: "OIDC not configured"));
    app.MapGet("/api/v1/centers/{centerId}/local-plan-drafts/{draftId:guid}/activity-reports", () =>
        Results.Problem(statusCode: 503, title: "OIDC not configured"));
    app.MapGet("/api/v1/centers/{centerId}/activity-sources/{sourceKind}", () =>
        Results.Problem(statusCode: 503, title: "OIDC not configured"));
    app.MapGet("/api/v1/centers/{centerId}/classes/catalogue", () =>
        Results.Problem(statusCode: 503, title: "OIDC not configured"));
    app.MapGet("/api/v1/centers/{centerId}/classes/{classId:guid}/source-sessions", () =>
        Results.Problem(statusCode: 503, title: "OIDC not configured"));
    app.MapPost("/api/v1/centers/{centerId}/local-plan-drafts/revisions", () =>
        Results.Problem(statusCode: 503, title: "OIDC not configured"));
    app.MapPost("/api/v1/centers/{centerId}/classes", () =>
        Results.Problem(statusCode: 503, title: "OIDC not configured"));
    app.MapPost("/api/v1/centers/{centerId}/classes/sessions", () =>
        Results.Problem(statusCode: 503, title: "OIDC not configured"));
    app.MapPost("/api/v1/centers/{centerId}/class-sessions/{sessionId:guid}/occurrence-claims", () =>
        Results.Problem(statusCode: 503, title: "OIDC not configured"));
    app.MapPost("/api/v1/centers/{centerId}/class-sessions/{sessionId:guid}/attendance-claims", () =>
        Results.Problem(statusCode: 503, title: "OIDC not configured"));
    app.MapPost("/api/v1/centers/{centerId}/classes/{classId:guid}/capacity-revisions", () => Results.Problem(statusCode:503,title:"OIDC not configured"));
    app.MapPost("/api/v1/centers/{centerId}/classes/{classId:guid}/registration-intents", () => Results.Problem(statusCode:503,title:"OIDC not configured"));
    app.MapPost("/api/v1/centers/{centerId}/classes/{classId:guid}/registration-intents/{requestId:guid}/review-requests", () => Results.Problem(statusCode:503,title:"OIDC not configured"));
    app.MapGet("/api/v1/centers/{centerId}/classes/{classId:guid}/registration-intents/{requestId:guid}/review-requests", () => Results.Problem(statusCode:503,title:"OIDC not configured"));
    app.MapGet("/api/v1/centers/{centerId}/classes/{classId:guid}/intake", () => Results.Problem(statusCode:503,title:"OIDC not configured"));
    app.MapPost("/api/v1/centers/{centerId}/activity-reports/sourced", () =>
        Results.Problem(statusCode: 503, title: "OIDC not configured"));
    app.MapGet("/api/v1/centers/{centerId}/class-sessions/{sessionId:guid}/report-lineage", () =>
        Results.Problem(statusCode: 503, title: "OIDC not configured"));
    app.MapGet("/api/v1/centers/{centerId}/class-sessions/{sessionId:guid}/occurrence-claims", () =>
        Results.Problem(statusCode: 503, title: "OIDC not configured"));
    app.MapGet("/api/v1/centers/{centerId}/class-sessions/{sessionId:guid}/occurrence-claims/{occurrenceReportId:guid}/attendance-claims", () =>
        Results.Problem(statusCode: 503, title: "OIDC not configured"));
}

app.MapGet("/api/v1/programs/published", NationalProgramReadEndpoint.ListPublished);

app.UseStatusCodePages(async context =>
{
    if (!context.HttpContext.Request.Path.StartsWithSegments("/api")) return;
    var response = context.HttpContext.Response;
    if (response.StatusCode != StatusCodes.Status404NotFound) return;
    await Results.Problem(statusCode: 404, title: "Not Found").ExecuteAsync(context.HttpContext);
});
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false
});
app.MapHealthChecks("/health/ready");
app.Run();

public partial class Program { }
