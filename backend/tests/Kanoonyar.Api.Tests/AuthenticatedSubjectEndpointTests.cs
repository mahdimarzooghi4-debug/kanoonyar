using System.Security.Claims;
using Kanoonyar.Api.Security;
using Xunit;

namespace Kanoonyar.Api.Tests;

public sealed class AuthenticatedSubjectEndpointTests
{
    [Fact]
    public void Anonymous_principal_is_rejected()
    {
        var result = AuthenticatedSubjectEndpoint.GetSubject(new ClaimsPrincipal(new ClaimsIdentity()));
        Assert.IsType<Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>(result);
    }

    [Fact]
    public void Authenticated_principal_without_subject_is_rejected()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim("other", "value")
        }, "test"));
        var result = AuthenticatedSubjectEndpoint.GetSubject(principal);
        Assert.IsType<Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>(result);
    }

    [Fact]
    public void Authenticated_principal_with_subject_returns_success()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim("sub", "subject-1")
        }, "test"));
        var result = AuthenticatedSubjectEndpoint.GetSubject(principal);
        Assert.StartsWith("Ok", result.GetType().Name, StringComparison.Ordinal);
    }
}
