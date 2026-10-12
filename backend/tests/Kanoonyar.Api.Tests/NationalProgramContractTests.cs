using Kanoonyar.Contracts.Programs;
using Xunit;

namespace Kanoonyar.Api.Tests;

public sealed class NationalProgramContractTests
{
    [Fact]
    public void Local_plan_is_pinned_to_national_version_without_modifying_national_requirements()
    {
        var national = new NationalProgramVersion("program-a", 3, "National",
            new[] { "Goal A", "Goal B" }, new[] { "Requirement A" });
        var local = new CenterProgramPlan("center-12", national, "Local execution", new[] { "Workshop" });
        Assert.Equal("program-a", local.ProgramId);
        Assert.Equal(3, local.NationalVersion);
        Assert.Equal("center-12", local.CenterId);
        Assert.Equal(new[] { "Goal A", "Goal B" }, national.NationalObjectives);
        Assert.Equal(new[] { "Requirement A" }, national.MandatoryRequirements);
        Assert.Equal(new[] { "Workshop" }, local.LocalActivities);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void National_version_must_be_positive(int version)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new NationalProgramVersion("program-a", version, "Title", Array.Empty<string>(), Array.Empty<string>()));
    }

    [Fact]
    public void National_objectives_and_requirements_reject_duplicates_and_blank_values()
    {
        Assert.Throws<ArgumentException>(() =>
            new NationalProgramVersion("program-a", 1, "Title", new[] { "Same", "Same" }, Array.Empty<string>()));
        Assert.Throws<ArgumentException>(() =>
            new NationalProgramVersion("program-a", 1, "Title", Array.Empty<string>(), new[] { " " }));
    }

    [Fact]
    public void Contracts_copy_input_collections_instead_of_aliasing_mutable_arrays()
    {
        var goals = new[] { "Original" };
        var activities = new[] { "First" };
        var national = new NationalProgramVersion("program-a", 1, "Title", goals, Array.Empty<string>());
        var local = new CenterProgramPlan("center-1", national, "Local", activities);
        goals[0] = "Mutated";
        activities[0] = "Mutated";
        Assert.Equal("Original", national.NationalObjectives[0]);
        Assert.Equal("First", local.LocalActivities[0]);
    }

    [Fact]
    public void Center_plan_requires_explicit_center_and_valid_local_activities()
    {
        var national = new NationalProgramVersion("program-a", 1, "Title", Array.Empty<string>(), Array.Empty<string>());
        Assert.Throws<ArgumentException>(() => new CenterProgramPlan(" ", national, "Local", Array.Empty<string>()));
        Assert.Throws<ArgumentException>(() => new CenterProgramPlan("center-1", national, "Local", new[] { "" }));
    }
}
