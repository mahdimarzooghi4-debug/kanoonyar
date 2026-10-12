using System.Security.Claims;
using Kanoonyar.Api.Programs;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Kanoonyar.Api.Tests;

public sealed class LocalPlanDraftWriteEndpointTests
{
    private static readonly IConfiguration Empty = new ConfigurationBuilder().Build();
    private static readonly LocalPlanDraftRevisionRequest Valid =
        new(Guid.Parse("11111111-1111-4111-8111-111111111111"), 0, "national-1", 1, "Local", ["Activity"]);
    private static ClaimsPrincipal User(params Claim[] claims) =>
        new(new ClaimsIdentity(claims, "test"));
    private static async Task<int?> Status(ClaimsPrincipal user, LocalPlanDraftRevisionRequest? command,
        ILocalPlanDraftWriteAuthorization? gate = null) =>
        (await LocalPlanDraftWriteEndpoint.Append(user, Empty,
            gate ?? new UnapprovedLocalPlanDraftWriteAuthorization(), "center-a",
            command, CancellationToken.None) as IStatusCodeHttpResult)?.StatusCode;

    [Fact]
    public async Task Anonymous_and_claimless_requests_fail()
    {
        Assert.Equal(401, await Status(new ClaimsPrincipal(), Valid));
        Assert.Equal(403, await Status(User(new Claim("name", "person")), Valid));
    }

    [Fact]
    public async Task Valid_authenticated_member_cannot_write_without_independent_grant()
    {
        Assert.Equal(403, await Status(User(new Claim("sub", "member")), Valid));
    }

    [Fact]
    public async Task Invalid_versioned_commands_are_rejected()
    {
        var user = User(new Claim("sub", "member"));
        Assert.Equal(400, await Status(user, Valid with { ExpectedRevision = -1 }));
        Assert.Equal(400, await Status(user, Valid with { DraftId = Guid.Empty }));
        Assert.Equal(400, await Status(user, Valid with { NationalVersion = 0 }));
        Assert.Equal(400, await Status(user, Valid with { LocalTitle = " " }));
        Assert.Equal(400, await Status(user, Valid with { LocalActivities = [" "] }));
        Assert.Equal(400, await Status(user, null));
    }

    [Fact]
    public async Task Broken_authorization_provider_stays_closed()
    {
        Assert.Equal(403, await Status(User(new Claim("sub", "member")), Valid, new Broken()));
    }

    [Fact]
    public async Task Synthetic_permission_does_not_override_missing_storage()
    {
        Assert.Equal(503, await Status(User(new Claim("sub", "member")), Valid, new AllowForTest()));
    }

    private sealed class Broken : ILocalPlanDraftWriteAuthorization
    {
        public ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal principal, string centerId, Guid draftId,
            CancellationToken ct) => throw new InvalidOperationException("Failure");
    }
    private sealed class AllowForTest : ILocalPlanDraftWriteAuthorization
    {
        public ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal principal, string centerId, Guid draftId,
            CancellationToken ct) => ValueTask.FromResult(true);
    }
}
