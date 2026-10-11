using System.Security.Claims;
using Kanoonyar.Api.Programs;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Kanoonyar.Api.Tests;

public sealed class LocalPlanDraftHistoryEndpointTests
{
    private static readonly IConfiguration Empty = new ConfigurationBuilder().Build();
    private static readonly Guid Draft = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static ClaimsPrincipal User(params Claim[] claims) =>
        new(new ClaimsIdentity(claims, "test"));
    private static async Task<int?> Status(ClaimsPrincipal principal, Guid draft,
        long? before = null, ILocalPlanDraftReadAuthorization? gate = null) =>
        (await LocalPlanDraftHistoryEndpoint.ListHistory(principal, Empty,
            gate ?? new UnapprovedLocalPlanDraftReadAuthorization(), "center-a",
            draft, before, CancellationToken.None) as IStatusCodeHttpResult)?.StatusCode;

    [Fact]
    public async Task Anonymous_and_missing_subject_are_rejected()
    {
        Assert.Equal(401, await Status(new ClaimsPrincipal(), Draft));
        Assert.Equal(403, await Status(User(new Claim("name", "actor")), Draft));
    }

    [Fact]
    public async Task Membership_never_grants_immutable_history_read()
    {
        Assert.Equal(403, await Status(User(new Claim("sub", "member")), Draft));
    }

    [Fact]
    public async Task Invalid_cursor_and_draft_fail_before_access()
    {
        var user = User(new Claim("sub", "member"));
        Assert.Equal(400, await Status(user, Guid.Empty));
        Assert.Equal(400, await Status(user, Draft, 0));
        Assert.Equal(400, await Status(user, Draft, -1));
    }

    [Fact]
    public async Task Failed_permission_evaluator_denies_access()
    {
        Assert.Equal(403, await Status(User(new Claim("sub", "member")), Draft, gate: new Broken()));
    }

    [Fact]
    public async Task Test_permission_does_not_override_missing_database_configuration()
    {
        Assert.Equal(503, await Status(User(new Claim("sub", "member")), Draft, gate: new TestAllow()));
    }

    private sealed class Broken : ILocalPlanDraftReadAuthorization
    {
        public ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal principal, string centerId,
            CancellationToken ct) => throw new InvalidOperationException("Failed");
    }

    private sealed class TestAllow : ILocalPlanDraftReadAuthorization
    {
        public ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal principal, string centerId,
            CancellationToken ct) => ValueTask.FromResult(true);
    }
}
