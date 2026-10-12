using Kanoonyar.Api.Security;
using Kanoonyar.Api.Programs;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddHealthChecks();
builder.Services.AddProblemDetails();
var oidcConfigured = builder.AddConfiguredOidc();

builder.Services.AddSingleton<ILocalPlanDraftWriteAuthorization, UnapprovedLocalPlanDraftWriteAuthorization>();
builder.Services.AddSingleton<ILocalPlanDraftReadAuthorization, UnapprovedLocalPlanDraftReadAuthorization>();

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
    app.MapPost("/api/v1/centers/{centerId}/local-plan-drafts/revisions", LocalPlanDraftWriteEndpoint.Append).RequireAuthorization();
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
    app.MapPost("/api/v1/centers/{centerId}/local-plan-drafts/revisions", () =>
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
