using Kanoonyar.Contracts.Programs;
using Xunit;

namespace Kanoonyar.Api.Tests;

public sealed class NationalProgramIssuanceTests
{
    private static readonly Guid IssuanceId = Guid.Parse("12121212-1212-4212-8212-121212121212");
    private static NationalProgramVersion Version(int version) =>
        new("national-1", version, "Program", ["Goal"], ["Requirement"]);

    [Fact]
    public void Exact_center_and_immutable_published_version_are_required_to_match_local_plan()
    {
        var catalog = new NationalProgramCatalog();
        var v1 = Version(1);
        var v2 = Version(2);
        catalog.Add(v1);
        catalog.Add(v2);
        var published = new NationalProgramPublicationIndex();
        published.Record(new NationalProgramPublication("national-1", 1,
            "hq-actor", DateTimeOffset.UnixEpoch), catalog);
        var issuance = new NationalProgramIssuance(IssuanceId, "center-a",
            "national-1", 1, "hq-actor", DateTimeOffset.UnixEpoch);
        Assert.True(NationalProgramIssuanceBoundary.Matches(issuance,
            new CenterProgramPlan("center-a", v1, "Local plan", ["Local activity"]), published));
        Assert.False(NationalProgramIssuanceBoundary.Matches(issuance,
            new CenterProgramPlan("center-b", v1, "Other center", []), published));
        Assert.False(NationalProgramIssuanceBoundary.Matches(issuance,
            new CenterProgramPlan("center-a", v2, "New version", []), published));
    }

    [Fact]
    public void Publication_alone_does_not_prove_a_matching_issuance()
    {
        var catalog = new NationalProgramCatalog();
        var v1 = Version(1);
        catalog.Add(v1);
        var published = new NationalProgramPublicationIndex();
        var issuance = new NationalProgramIssuance(IssuanceId, "center-a",
            "national-1", 1, "hq-actor", DateTimeOffset.UnixEpoch);
        Assert.False(NationalProgramIssuanceBoundary.Matches(issuance,
            new CenterProgramPlan("center-a", v1, "Local", []), published));
    }

    [Fact]
    public void Required_identity_version_and_utc_timestamp_are_not_inferred()
    {
        Assert.Throws<ArgumentException>(() => new NationalProgramIssuance(Guid.Empty,
            "center-a", "national-1", 1, "hq", DateTimeOffset.UnixEpoch));
        Assert.Throws<ArgumentException>(() => new NationalProgramIssuance(IssuanceId,
            "", "national-1", 1, "hq", DateTimeOffset.UnixEpoch));
        Assert.Throws<ArgumentException>(() => new NationalProgramIssuance(IssuanceId,
            "center-a", "", 1, "hq", DateTimeOffset.UnixEpoch));
        Assert.Throws<ArgumentOutOfRangeException>(() => new NationalProgramIssuance(IssuanceId,
            "center-a", "national-1", 0, "hq", DateTimeOffset.UnixEpoch));
        Assert.Throws<ArgumentException>(() => new NationalProgramIssuance(IssuanceId,
            "center-a", "national-1", 1, "", DateTimeOffset.UnixEpoch));
        Assert.Throws<ArgumentException>(() => new NationalProgramIssuance(IssuanceId,
            "center-a", "national-1", 1, "hq", default));
        Assert.Throws<ArgumentException>(() => new NationalProgramIssuance(IssuanceId,
            "center-a", "national-1", 1, "hq",
            new DateTimeOffset(2026, 10, 12, 9, 0, 0, TimeSpan.FromHours(3))));
    }

    [Fact]
    public void Issuance_properties_are_stable_and_do_not_expose_mutators()
    {
        var issuance = new NationalProgramIssuance(IssuanceId, "center-a",
            "national-1", 1, "hq", DateTimeOffset.UnixEpoch);
        Assert.Equal(IssuanceId, issuance.IssuanceId);
        Assert.Equal("center-a", issuance.CenterId);
        Assert.Equal("national-1", issuance.ProgramId);
        Assert.Equal(1, issuance.NationalVersion);
        Assert.Equal(DateTimeOffset.UnixEpoch, issuance.IssuedAt);
    }
}
