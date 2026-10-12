using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;

namespace Kanoonyar.Api.Security;

/// <summary>Authentication-only boundary: verified subject is required; center permission is not inferred.</summary>
public static class AuthenticatedSubjectEndpoint
{
    public static IResult GetSubject(ClaimsPrincipal user)
    {
        if (user.Identity?.IsAuthenticated != true)
            return Results.Problem(statusCode: 401, title: "Authentication required");

        var subject = user.FindFirst("sub")?.Value;
        if (string.IsNullOrWhiteSpace(subject))
            return Results.Problem(statusCode: 403, title: "Subject claim required");

        return Results.Ok(new { subjectId = subject });
    }
}
