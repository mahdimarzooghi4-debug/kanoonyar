using Kanoonyar.Contracts.Authorization;
using Kanoonyar.Contracts.Centers;
using Kanoonyar.Contracts.Programs;
using Xunit;

namespace Kanoonyar.Api.Tests;

public sealed class CenterProgramAdoptionBoundaryTests
{
    private static CenterAccessEvidence Evidence() => new("subject-1", "center-1",
        new CenterRegistryEntry("center-1", "Center", true),
        new CenterMembership("center-1", "subject-1", "uninterpreted-role", true));

    [Fact]
    public void Exact_actor_and_center_are_structurally_matched()
    {
        var request = new CenterProgramAdoptionRequest("center-1", "subject-1", "national-1", 2, "Local");
        Assert.True(CenterProgramAdoptionBoundary.Matches(request, Evidence()));
    }

    [Fact]
    public void Adoption_cannot_substitute_a_different_actor_or_center()
    {
        Assert.False(CenterProgramAdoptionBoundary.Matches(
            new CenterProgramAdoptionRequest("center-2", "subject-1", "national-1", 2, "Local"), Evidence()));
        Assert.False(CenterProgramAdoptionBoundary.Matches(
            new CenterProgramAdoptionRequest("center-1", "subject-2", "national-1", 2, "Local"), Evidence()));
    }
}
