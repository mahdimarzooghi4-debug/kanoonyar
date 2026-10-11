using Kanoonyar.Contracts.Programs;
using Xunit;

namespace Kanoonyar.Api.Tests;

public sealed class NationalProgramPublicationTests
{
    private static NationalProgramVersion Version(int version) =>
        new("program-a", version, "National", Array.Empty<string>(), Array.Empty<string>());

    [Fact]
    public void Version_is_not_published_just_because_it_is_registered()
    {
        var catalog = new NationalProgramCatalog();
        catalog.Add(Version(1));
        var published = new NationalProgramPublicationIndex();
        Assert.False(published.IsPublished("program-a", 1));
    }

    [Fact]
    public void Only_exact_published_version_is_visible_as_published()
    {
        var catalog = new NationalProgramCatalog();
        catalog.Add(Version(1));
        catalog.Add(Version(2));
        var published = new NationalProgramPublicationIndex();
        published.Record(new NationalProgramPublication("program-a", 1, "headquarters-subject", DateTimeOffset.UnixEpoch), catalog);
        Assert.True(published.IsPublished("program-a", 1));
        Assert.False(published.IsPublished("program-a", 2));
    }

    [Fact]
    public void Unknown_and_duplicate_publication_are_rejected()
    {
        var catalog = new NationalProgramCatalog();
        catalog.Add(Version(1));
        var published = new NationalProgramPublicationIndex();
        Assert.Throws<InvalidOperationException>(() => published.Record(
            new NationalProgramPublication("program-a", 2, "headquarters-subject", DateTimeOffset.UnixEpoch), catalog));
        var record = new NationalProgramPublication("program-a", 1, "headquarters-subject", DateTimeOffset.UnixEpoch);
        published.Record(record, catalog);
        Assert.Throws<InvalidOperationException>(() => published.Record(record, catalog));
    }

    [Fact]
    public void Publication_requires_explicit_utc_time_and_publisher()
    {
        Assert.Throws<ArgumentException>(() => new NationalProgramPublication("program-a", 1, "", DateTimeOffset.UnixEpoch));
        Assert.Throws<ArgumentException>(() => new NationalProgramPublication("program-a", 1, "actor", default));
        Assert.Throws<ArgumentException>(() => new NationalProgramPublication("program-a", 1, "actor",
            new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.FromHours(3))));
    }
}
