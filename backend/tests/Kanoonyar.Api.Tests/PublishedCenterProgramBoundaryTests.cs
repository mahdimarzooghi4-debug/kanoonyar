using Kanoonyar.Contracts.Authorization;
using Kanoonyar.Contracts.Centers;
using Kanoonyar.Contracts.Programs;
using Xunit;

namespace Kanoonyar.Api.Tests;

public sealed class PublishedCenterProgramBoundaryTests
{
    [Fact]
    public void Adoption_is_structurally_denied_until_exact_version_is_published()
    {
        var catalog = new NationalProgramCatalog();
        var national = new NationalProgramVersion("program-a", 1, "National", Array.Empty<string>(), Array.Empty<string>());
        catalog.Add(national);
        var publication = new NationalProgramPublicationIndex();
        var plan = new CenterProgramPlan("center-a", national, "Local", Array.Empty<string>());
        var request = new CenterProgramAdoptionRequest("center-a", "subject-a", "program-a", 1, "Local");
        var evidence = new CenterAccessEvidence("subject-a", "center-a",
            new CenterRegistryEntry("center-a", "Center", true),
            new CenterMembership("center-a", "subject-a", "uninterpreted-role", true));
        Assert.False(PublishedCenterProgramBoundary.Matches(request, evidence, plan, catalog, publication));
        publication.Record(new NationalProgramPublication("program-a", 1, "headquarters-subject", DateTimeOffset.UnixEpoch), catalog);
        Assert.True(PublishedCenterProgramBoundary.Matches(request, evidence, plan, catalog, publication));
        Assert.False(PublishedCenterProgramBoundary.Matches(
            new CenterProgramAdoptionRequest("center-b", "subject-a", "program-a", 1, "Local"),
            evidence, plan, catalog, publication));
    }
}
