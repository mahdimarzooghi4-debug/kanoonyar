using System.Security.Claims;
using Kanoonyar.Api.Programs;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Kanoonyar.Api.Tests;

public sealed class NationalInspectionDossierEndpointTests
{
    private sealed class FixedAuthorization(bool permitted) : INationalInspectionDossierReadAuthorization
    {
        public ValueTask<bool> IsAuthorizedAsync(ClaimsPrincipal actor, CancellationToken cancellationToken) =>
            ValueTask.FromResult(permitted);
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

    private static async Task<int?> Status(IResult result)
    {
        var context = new DefaultHttpContext();
        await result.ExecuteAsync(context);
        return context.Response.StatusCode;
    }

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
            valid, Actor(), Config(), new UnapprovedNationalInspectionDossierReadAuthorization(), default)));
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
}
