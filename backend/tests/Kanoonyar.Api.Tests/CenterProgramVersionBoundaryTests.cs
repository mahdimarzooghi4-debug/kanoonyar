using Kanoonyar.Contracts.Programs;
using Xunit;

namespace Kanoonyar.Api.Tests;

public sealed class CenterProgramVersionBoundaryTests
{
    [Fact]
    public void Only_the_exact_existing_national_version_matches_local_adoption()
    {
        var catalog = new NationalProgramCatalog();
        var national = new NationalProgramVersion("program-a", 2, "National", Array.Empty<string>(), Array.Empty<string>());
        catalog.Add(national);
        var plan = new CenterProgramPlan("center-a", national, "Local", Array.Empty<string>());
        var exact = new CenterProgramAdoptionRequest("center-a", "subject-a", "program-a", 2, "Local");
        Assert.True(CenterProgramVersionBoundary.MatchesAdoptionIntent(plan, exact, catalog));
        Assert.False(CenterProgramVersionBoundary.MatchesAdoptionIntent(plan,
            new CenterProgramAdoptionRequest("center-a", "subject-a", "program-a", 3, "Local"), catalog));
        Assert.False(CenterProgramVersionBoundary.MatchesAdoptionIntent(plan,
            new CenterProgramAdoptionRequest("center-b", "subject-a", "program-a", 2, "Local"), catalog));
    }

    [Fact]
    public void Unregistered_national_version_fails_closed()
    {
        var catalog = new NationalProgramCatalog();
        var national = new NationalProgramVersion("program-a", 1, "National", Array.Empty<string>(), Array.Empty<string>());
        var plan = new CenterProgramPlan("center-a", national, "Local", Array.Empty<string>());
        Assert.False(CenterProgramVersionBoundary.MatchesExistingVersion(plan, catalog));
    }
}
