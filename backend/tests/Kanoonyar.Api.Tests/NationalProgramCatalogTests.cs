using Kanoonyar.Contracts.Programs;
using Xunit;

namespace Kanoonyar.Api.Tests;

public sealed class NationalProgramCatalogTests
{
    private static NationalProgramVersion Version(int version, string programId = "national-a") =>
        new(programId, version, "Program", new[] { "National goal" }, new[] { "Mandatory item" });

    [Fact]
    public void Keeps_multiple_immutable_versions_and_returns_them_in_order()
    {
        var catalog = new NationalProgramCatalog();
        catalog.Add(Version(3));
        catalog.Add(Version(1));
        Assert.Equal(new[] { 1, 3 }, catalog.ListVersions("national-a").Select(x => x.Version));
        Assert.Equal(3, catalog.Find("national-a", 3)?.Version);
        Assert.Null(catalog.Find("national-a", 2));
    }

    [Fact]
    public void Duplicate_program_version_is_rejected_without_overwriting_original()
    {
        var catalog = new NationalProgramCatalog();
        var original = Version(1);
        catalog.Add(original);
        Assert.Throws<InvalidOperationException>(() => catalog.Add(Version(1)));
        Assert.Same(original, catalog.Find("national-a", 1));
    }

    [Fact]
    public void Different_programs_can_have_the_same_version_number()
    {
        var catalog = new NationalProgramCatalog();
        catalog.Add(Version(1, "national-a"));
        catalog.Add(Version(1, "national-b"));
        Assert.Single(catalog.ListVersions("national-a"));
        Assert.Single(catalog.ListVersions("national-b"));
    }

    [Fact]
    public void Missing_program_returns_no_versions_and_never_infers_a_default()
    {
        var catalog = new NationalProgramCatalog();
        Assert.Empty(catalog.ListVersions("missing"));
        Assert.Null(catalog.Find("missing", 1));
        Assert.Null(catalog.Find("national-a", 0));
    }
}
