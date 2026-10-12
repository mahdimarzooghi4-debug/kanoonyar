using System.Security.Claims;
using Kanoonyar.Api.Programs;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Kanoonyar.Api.Tests;

public class LocalPlanDraftReadEndpointTests
{
    private static IConfiguration Config(string? url = null) =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["CENTER_DRAFT_READ_DATABASE_URL"] = url }).Build();

    private static ClaimsPrincipal User(params Claim[] claims) =>
        new(new ClaimsIdentity(claims, "test"));

    private static async Task<int?> Status(ClaimsPrincipal principal, IConfiguration? configuration = null,
        ILocalPlanDraftReadAuthorization? authorization = null) =>
        (await LocalPlanDraftReadEndpoint.ListMine(principal, configuration ?? Config(),
            authorization ?? new UnapprovedLocalPlanDraftReadAuthorization(), "c1", default) as IStatusCodeHttpResult)?.StatusCode;

    [Fact]
    public async Task Anonymous_and_missing_subject_are_rejected()
    {
        Assert.Equal(401, await Status(new ClaimsPrincipal()));
        Assert.Equal(403, await Status(User(new Claim("name", "person"))));
    }

    [Fact]
    public async Task Membership_never_substitutes_for_separately_approved_read_permission()
    {
        Assert.Equal(403, await Status(User(new Claim("sub", "active-member"))));
    }

    [Fact]
    public async Task Authorization_dependency_failure_fails_closed()
    {
        Assert.Equal(403, await Status(User(new Claim("sub", "active-member")), authorization: new Throwing()));
    }

    [Fact]
    public async Task Synthetic_test_only_permission_does_not_substitute_for_storage()
    {
        Assert.Equal(503, await Status(User(new Claim("sub", "active-member")), authorization: new AllowForTest()));
    }

    public sealed class AllowForTest : ILocalPlanDraftReadAuthorization
    {
        public ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal principal, string centerId,
            CancellationToken ct) => ValueTask.FromResult(true);
    }

    private sealed class Throwing : ILocalPlanDraftReadAuthorization
    {
        public ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal principal, string centerId,
            CancellationToken ct) => throw new InvalidOperationException("Provider failure");
    }
}
