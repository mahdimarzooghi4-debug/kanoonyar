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
builder.Services.AddSingleton<ICenterLearningPlanWriteAuthorization, UnapprovedCenterLearningPlanWriteAuthorization>();
builder.Services.AddSingleton<ICenterSessionClaimWriteAuthorization, UnapprovedCenterSessionClaimWriteAuthorization>();
builder.Services.AddSingleton<ICenterSessionClaimHistoryAuthorization, UnapprovedCenterSessionClaimHistoryAuthorization>();
builder.Services.AddSingleton<ICenterSessionReportLineageAuthorization, UnapprovedCenterSessionReportLineageAuthorization>();
builder.Services.AddSingleton<ICenterSourcedActivityReportWriteAuthorization, UnapprovedCenterSourcedActivityReportWriteAuthorization>();
builder.Services.AddSingleton<ICenterClassIntakeAuthorization, UnapprovedCenterClassIntakeAuthorization>();
builder.Services.AddSingleton<ICenterRegistrationReviewAuthorization, UnapprovedCenterRegistrationReviewAuthorization>();

var app = builder.Build();
app.UseExceptionHandler();
if (oidcConfigured)
{
    app.UseAuthentication();
    app.UseAuthorization();
    app.MapGet("/api/v1/identity/me", AuthenticatedSubjectEndpoint.GetSubject).RequireAuthorization();
    app.MapGet("/api/v1/identity/center-memberships", CenterMembershipEndpoint.GetMine).RequireAuthorization();
    app.MapGet("/api/v1/centers/{centerId}/local-plan-drafts", LocalPlanDraftReadEndpoint.ListMine).RequireAuthorization();
    app.MapGet("/api/v1/centers/{centerId}/local-plan-drafts/{draftId:guid}/revisions", LocalPlanDraftHistoryEndpoint.ListHistory).RequireAuthorization();
    app.MapGet("/api/v1/centers/{centerId}/local-plan-drafts/{draftId:guid}/execution-trace", CenterExecutionTraceEndpoint.GetTrace).RequireAuthorization();
    app.MapGet("/api/v1/centers/{centerId}/local-plan-drafts/{draftId:guid}/activity-reports", CenterActivityReportReadEndpoint.ListReports).RequireAuthorization();
    app.MapGet("/api/v1/centers/{centerId}/activity-sources/{sourceKind}", CenterSourceCatalogEndpoint.ListSources).RequireAuthorization();
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
