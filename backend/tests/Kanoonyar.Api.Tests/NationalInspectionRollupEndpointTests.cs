using System.Security.Claims;
using Kanoonyar.Api.Programs;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Kanoonyar.Api.Tests;

public sealed class NationalInspectionRollupEndpointTests
{
    private sealed class FixedAuthorization(bool permitted) : INationalInspectionRollupReadAuthorization
    {
        public ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal actor, CancellationToken cancellationToken) =>
            ValueTask.FromResult(permitted);
    }

    private static ClaimsPrincipal Actor(string? subject = "synthetic-hq-subject", bool authenticated = true) =>
        new(new ClaimsIdentity(
            subject is null ? [] : [new Claim("sub", subject)],
            authenticated ? "synthetic-test" : null));

    private static int Status(IResult result) =>
        (result as IStatusCodeHttpResult)?.StatusCode ?? 200;

    private static Task<IResult> Call(string query = "", ClaimsPrincipal? actor = null,
        INationalInspectionRollupReadAuthorization? authorization = null,
        IConfiguration? configuration = null, bool exportCsv = false)
    {
        var context = new DefaultHttpContext();
        context.Request.QueryString = new QueryString(query);
        return exportCsv
            ? NationalInspectionRollupEndpoint.ExportCsv(context, actor ?? Actor(),
                configuration ?? new ConfigurationBuilder().Build(),
                authorization ?? new UnapprovedNationalInspectionRollupReadAuthorization(),
                CancellationToken.None)
            : NationalInspectionRollupEndpoint.List(context, actor ?? Actor(),
                configuration ?? new ConfigurationBuilder().Build(),
                authorization ?? new UnapprovedNationalInspectionRollupReadAuthorization(),
                CancellationToken.None);
    }

    [Fact]
    public async Task Default_authorization_denies_before_opening_database()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["NATIONAL_INSPECTION_ROLLUP_READ_DATABASE_URL"] =
                "Host=127.0.0.1;Port=1;Database=missing;Username=none;Password=none;Timeout=1"
        }).Build();

        Assert.Equal(403, Status(await Call(actor: Actor(), configuration: config)));
    }

    [Fact]
    public async Task CSV_export_is_default_denied_before_opening_database()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["NATIONAL_INSPECTION_ROLLUP_READ_DATABASE_URL"] =
                "Host=127.0.0.1;Port=1;Database=missing;Username=none;Password=none;Timeout=1"
        }).Build();

        Assert.Equal(403, Status(await Call(
            "?findingsOnly=true&findingCode=SOURCE_SNAPSHOT_DIGEST_INVALID",
            configuration: config, exportCsv: true)));
    }

    [Theory]
    [InlineData("?beforeProgramId=alpha&beforeNationalVersion=1")]
    [InlineData("?beforeProgramId=alpha")]
    [InlineData("?targetProgramId=alpha&targetNationalVersion=1&unexpected=true")]
    public async Task CSV_export_rejects_cursors_and_unknown_parameters_without_storage_access(string query) =>
        Assert.Equal(400, Status(await Call(query,
            authorization: new FixedAuthorization(true), exportCsv: true)));

    [Fact]
    public async Task Anonymous_and_subjectless_actors_are_denied()
    {
        Assert.Equal(401, Status(await Call(actor: Actor(authenticated: false),
            authorization: new FixedAuthorization(true))));
        Assert.Equal(403, Status(await Call(actor: Actor(subject: null),
            authorization: new FixedAuthorization(true))));
    }

    [Theory]
    [InlineData("?beforeProgramId=alpha")]
    [InlineData("?beforeNationalVersion=1")]
    [InlineData("?beforeProgramId=alpha&beforeNationalVersion=0")]
    [InlineData("?beforeProgramId=alpha&beforeNationalVersion=-1")]
    [InlineData("?beforeProgramId=alpha&beforeNationalVersion=01")]
    [InlineData("?beforeProgramId=%20&beforeNationalVersion=1")]
    [InlineData("?beforeProgramId=alpha&beforeProgramId=beta&beforeNationalVersion=1")]
    [InlineData("?beforeProgramId=alpha&beforeNationalVersion=1&extra=true")]
    [InlineData("?findingsOnly=yes")]
    [InlineData("?findingsOnly=true&findingsOnly=false")]
    [InlineData("?beforeProgramId=alpha&beforeNationalVersion=1&findingsOnly=TRUE")]
    [InlineData("?findingCode=UNKNOWN")]
    [InlineData("?findingCode=INSPECTION_AUDIT_EVIDENCE_INVALID&findingCode=OUTBOX_EVIDENCE_INVALID")]
    [InlineData("?findingsOnly=false&findingCode=INSPECTION_AUDIT_EVIDENCE_INVALID")]
    [InlineData("?targetProgramId=alpha")]
    [InlineData("?targetNationalVersion=1")]
    [InlineData("?targetProgramId=alpha&targetNationalVersion=0")]
    [InlineData("?targetProgramId=alpha&targetNationalVersion=01")]
    [InlineData("?targetProgramId=%20alpha&targetNationalVersion=1")]
    [InlineData("?targetProgramId=alpha&targetProgramId=beta&targetNationalVersion=1")]
    [InlineData("?beforeProgramId=alpha&beforeNationalVersion=1&targetProgramId=alpha&targetNationalVersion=1")]
    public async Task Invalid_or_ambiguous_cursor_filter_or_target_is_rejected_without_storage_access(string query) =>
        Assert.Equal(400, Status(await Call(query, authorization: new FixedAuthorization(true))));

    [Theory]
    [InlineData("?findingsOnly=true")]
    [InlineData("?findingsOnly=false&beforeProgramId=alpha&beforeNationalVersion=1")]
    [InlineData("?findingsOnly=true&findingCode=INSPECTION_AUDIT_EVIDENCE_INVALID")]
    [InlineData("?findingCode=SOURCE_SNAPSHOT_DIGEST_INVALID&beforeProgramId=alpha&beforeNationalVersion=1")]
    [InlineData("?targetProgramId=alpha&targetNationalVersion=1")]
    [InlineData("?targetProgramId=alpha&targetNationalVersion=2&findingsOnly=true&findingCode=INSPECTION_AUDIT_EVIDENCE_INVALID")]
    public async Task Valid_filter_and_cursor_combinations_reach_default_deny(string query) =>
        Assert.Equal(403, Status(await Call(query)));

    [Fact]
    public async Task Authorized_reader_without_configured_storage_gets_safe_unavailable_response()
    {
        var result = await Call(authorization: new FixedAuthorization(true));
        Assert.Equal(503, Status(result));
        Assert.IsType<ProblemHttpResult>(result);
    }
}
