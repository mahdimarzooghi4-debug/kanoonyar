using Kanoonyar.Contracts.Programs;
using Xunit;

namespace Kanoonyar.Api.Tests;

public sealed class CenterProgramAdoptionRequestTests
{
    [Fact]
    public void Adoption_intent_keeps_center_actor_and_version_explicit()
    {
        var request = new CenterProgramAdoptionRequest("center-042", "subject-1", "program-a", 2, "Local plan");
        Assert.Equal("center-042", request.CenterId);
        Assert.Equal("subject-1", request.ActorSubjectId);
        Assert.Equal("program-a", request.ProgramId);
        Assert.Equal(2, request.NationalVersion);
    }

    [Fact]
    public void Invalid_adoption_version_fails_closed()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new CenterProgramAdoptionRequest("center-1", "subject-1", "program-a", 0, "Local"));
    }

    [Fact]
    public void Missing_actor_is_rejected()
    {
        Assert.Throws<ArgumentException>(() =>
            new CenterProgramAdoptionRequest("center-1", " ", "program-a", 1, "Local"));
    }
}
