using Kanoonyar.Api.Programs;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Http.HttpResults;
using Xunit;

namespace Kanoonyar.Api.Tests;

public sealed class NationalProgramReadEndpointTests
{
    [Fact]
    public async Task Missing_storage_configuration_returns_safe_503()
    {
        var result = await NationalProgramReadEndpoint.ListPublished(new ConfigurationBuilder().Build(), CancellationToken.None);
        Assert.Equal(503, Assert.IsType<ProblemHttpResult>(result).StatusCode);
    }

    [Fact]
    public async Task Unreachable_storage_returns_safe_503()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["PROGRAM_READ_DATABASE_URL"] = "Host=127.0.0.1;Port=1;Database=missing;Username=none;Password=none;Timeout=1"
        }).Build();
        var result = await NationalProgramReadEndpoint.ListPublished(configuration, CancellationToken.None);
        Assert.Equal(503, Assert.IsType<ProblemHttpResult>(result).StatusCode);
    }
}
