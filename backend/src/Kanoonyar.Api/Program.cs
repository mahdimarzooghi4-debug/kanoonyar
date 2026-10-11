using Kanoonyar.Api.Security;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddHealthChecks();
builder.Services.AddProblemDetails();
var oidcConfigured = builder.AddConfiguredOidc();

var app = builder.Build();
app.UseExceptionHandler();
if (oidcConfigured)
{
    app.UseAuthentication();
    app.UseAuthorization();
    app.MapGet("/api/v1/identity/me", AuthenticatedSubjectEndpoint.GetSubject).RequireAuthorization();
    app.MapGet("/api/v1/identity/center-memberships", CenterMembershipEndpoint.GetMine).RequireAuthorization();
}
else
{
    app.MapGet("/api/v1/identity/me", () =>
        Results.Problem(statusCode: 503, title: "OIDC not configured"));
    app.MapGet("/api/v1/identity/center-memberships", () =>
        Results.Problem(statusCode: 503, title: "OIDC not configured"));
}

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
