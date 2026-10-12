using System.Security.Claims;
using Kanoonyar.Api.Programs;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Kanoonyar.Api.Tests;

public sealed class CenterSessionReportLineageEndpointTests
{
    private static readonly IConfiguration NoConnection = new ConfigurationBuilder().Build();
    private static ClaimsPrincipal Principal(params Claim[] claims) =>
        new(new ClaimsIdentity(claims, "fixture"));
    private static int? Status(IResult result) => (result as IStatusCodeHttpResult)?.StatusCode;

    private static Task<IResult> Get(ClaimsPrincipal principal, Guid? session = null,
        long? before = null, ICenterSessionReportLineageAuthorization? grant = null,
        string center = "center-a") =>
        CenterSessionReportLineageEndpoint.Get(principal, NoConnection,
            grant ?? new UnapprovedCenterSessionReportLineageAuthorization(),
            center, session ?? Guid.NewGuid(), before, CancellationToken.None);

    [Fact]
    public async Task Requires_signed_subject_and_rejects_invalid_identity_or_cursor()
    {
        Assert.Equal(401, Status(await Get(new ClaimsPrincipal())));
        Assert.Equal(403, Status(await Get(Principal(new Claim("role", "admin")))));
        var actor = Principal(new Claim("sub", "fixture"));
        Assert.Equal(400, Status(await Get(actor, Guid.Empty)));
        Assert.Equal(400, Status(await Get(actor, before: 0)));
        Assert.Equal(400, Status(await Get(actor, before: -1)));
        Assert.Equal(400, Status(await Get(actor, center: "")));
    }

    [Fact]
    public async Task Independent_grant_is_denied_by_default_and_fails_closed()
    {
        var actor = Principal(new Claim("sub", "fixture"), new Claim("role", "superadmin"));
        Assert.Equal(403, Status(await Get(actor)));
        Assert.Equal(403, Status(await Get(actor, grant: new ThrowingGrant())));
        Assert.Equal(503, Status(await Get(actor, grant: new SyntheticGrant())));
    }

    private sealed class SyntheticGrant : ICenterSessionReportLineageAuthorization
    {
        public ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal user, string centerId,
            Guid sessionId, CancellationToken ct) => ValueTask.FromResult(true);
    }
    private sealed class ThrowingGrant : ICenterSessionReportLineageAuthorization
    {
        public ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal user, string centerId,
            Guid sessionId, CancellationToken ct) =>
            throw new InvalidOperationException("Synthetic grant backend failed");
    }
}
