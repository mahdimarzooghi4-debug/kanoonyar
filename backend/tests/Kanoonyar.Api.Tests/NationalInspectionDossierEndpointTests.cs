using System.Security.Claims;
using Kanoonyar.Api.Programs;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Kanoonyar.Api.Tests;

public sealed class NationalInspectionDossierEndpointTests
{
    private sealed class FixedAuthorization(bool permitted) : INationalInspectionReadAuthorization
    {
        public NationalInspectionReadScope? LastScope { get; private set; }

        public ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal actor, NationalInspectionReadScope scope,
            CancellationToken cancellationToken)
        {
            LastScope = scope;
            return ValueTask.FromResult(permitted);
        }
    }

    private static ClaimsPrincipal Actor(bool authenticated = true, string? subject = "synthetic-reader") =>
        new(new ClaimsIdentity(subject is null ? [] : [new Claim("sub", subject)],
            authenticated ? "synthetic-test" : null));

    private static DefaultHttpContext Context(string query)
    {
        var context = new DefaultHttpContext();
        context.Request.QueryString = new QueryString(query);
        return context;
    }

    private static IConfiguration Config(bool configured = false) =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["NATIONAL_INSPECTION_DOSSIER_READ_DATABASE_URL"] = configured ? "Host=127.0.0.1;Database=unused" : null
        }).Build();

    private static Task<int?> Status(IResult result) =>
        Task.FromResult(Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);

    [Fact]
    public async Task Dossier_requires_subject_scope_and_exact_bounded_cursor()
    {
        var valid = Context("?programId=national-1&nationalVersion=3");
        Assert.Equal(401, await Status(await NationalInspectionDossierEndpoint.List(
            Context("?programId=national-1&nationalVersion=3"), Actor(false), Config(),
            new FixedAuthorization(true), default)));
        Assert.Equal(403, await Status(await NationalInspectionDossierEndpoint.List(
            valid, Actor(subject: null), Config(), new FixedAuthorization(true), default)));
        Assert.Equal(403, await Status(await NationalInspectionDossierEndpoint.List(
            valid, Actor(), Config(), new FixedAuthorization(false), default)));
        Assert.Equal(403, await Status(await NationalInspectionDossierEndpoint.List(
            valid, Actor(), Config(), new UnapprovedNationalInspectionReadAuthorization(), default)));
        Assert.Equal(400, await Status(await NationalInspectionDossierEndpoint.List(
            Context("?programId=%20&nationalVersion=3"), Actor(), Config(), new FixedAuthorization(true), default)));
        Assert.Equal(400, await Status(await NationalInspectionDossierEndpoint.List(
            Context("?programId=national-1&nationalVersion=03"), Actor(), Config(), new FixedAuthorization(true), default)));
        Assert.Equal(400, await Status(await NationalInspectionDossierEndpoint.List(
            Context("?programId=national-1&nationalVersion=3&beforeNoteId=00000000-0000-0000-0000-000000000001"),
            Actor(), Config(), new FixedAuthorization(true), default)));
        Assert.Equal(400, await Status(await NationalInspectionDossierEndpoint.List(
            Context("?programId=national-1&nationalVersion=3&beforeInspectedAt=not-a-date&beforeNoteId=00000000-0000-0000-0000-000000000001"),
            Actor(), Config(), new FixedAuthorization(true), default)));
        Assert.Equal(503, await Status(await NationalInspectionDossierEndpoint.List(
            valid, Actor(), Config(), new FixedAuthorization(true), default)));
    }

    [Fact]
    public async Task Authorization_receives_exact_program_version_scope()
    {
        var authorization = new FixedAuthorization(false);
        Assert.Equal(403, await Status(await NationalInspectionDossierEndpoint.List(
            Context("?programId=national-1&nationalVersion=3"), Actor(), Config(), authorization, default)));
        Assert.Equal(new NationalInspectionReadScope(
            NationalInspectionReadOperation.InspectionDossierPage, "national-1", 3,
            FindingsOnly: false, FindingCode: null), authorization.LastScope);
    }

    [Fact]
    public async Task CSV_export_has_distinct_exact_target_scope_and_rejects_page_cursor()
    {
        var authorization = new FixedAuthorization(false);
        Assert.Equal(403, await Status(await NationalInspectionDossierEndpoint.ExportCsv(
            Context("?programId=national-2&nationalVersion=8"), Actor(), Config(), authorization, default)));
        Assert.Equal(new NationalInspectionReadScope(
            NationalInspectionReadOperation.InspectionDossierCsvExport, "national-2", 8,
            FindingsOnly: false, FindingCode: null), authorization.LastScope);

        var untouchedAuthorization = new FixedAuthorization(true);
        Assert.Equal(400, await Status(await NationalInspectionDossierEndpoint.ExportCsv(
            Context("?programId=national-2&nationalVersion=8&beforeInspectedAt=2026-10-17T12:00:00Z&beforeNoteId=00000000-0000-0000-0000-000000000001"),
            Actor(), Config(), untouchedAuthorization, default)));
        Assert.Null(untouchedAuthorization.LastScope);
    }

    [Fact]
    public async Task CSV_export_authorization_failure_is_default_deny_before_database_access()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["NATIONAL_INSPECTION_DOSSIER_READ_DATABASE_URL"] =
                "Host=127.0.0.1;Port=1;Database=missing;Username=none;Password=none;Timeout=1"
        }).Build();
        var denial = new UnapprovedNationalInspectionReadAuthorization();
        Assert.Equal(403, await Status(await NationalInspectionDossierEndpoint.ExportCsv(
            Context("?programId=national-2&nationalVersion=8"), Actor(), config, denial, default)));
    }
}
