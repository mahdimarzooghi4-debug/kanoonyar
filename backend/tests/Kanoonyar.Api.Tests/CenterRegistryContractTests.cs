using Kanoonyar.Contracts.Centers;
using Xunit;

namespace Kanoonyar.Api.Tests;

public sealed class CenterRegistryContractTests
{
    [Fact]
    public void Center_identity_is_explicit_and_not_inferred_from_name()
    {
        var center = new CenterRegistryEntry("center-042", "Cultural Center", true);
        Assert.Equal("center-042", center.CenterId);
        Assert.Equal("Cultural Center", center.DisplayName);
        Assert.True(center.IsActive);
    }

    [Fact]
    public void Membership_is_scoped_to_a_specific_center_and_subject()
    {
        var membership = new CenterMembership("center-042", "oidc-subject-1", "center-manager", true);
        Assert.Equal("center-042", membership.CenterId);
        Assert.Equal("oidc-subject-1", membership.SubjectId);
        Assert.Equal("center-manager", membership.RoleCode);
        Assert.True(membership.IsActive);
    }

    [Theory]
    [InlineData("", "Name")]
    [InlineData("center-1", " ")]
    public void Invalid_center_record_is_rejected(string id, string name)
    {
        Assert.Throws<ArgumentException>(() => new CenterRegistryEntry(id, name, true));
    }

    [Theory]
    [InlineData("", "subject", "role")]
    [InlineData("center", "", "role")]
    [InlineData("center", "subject", " ")]
    public void Invalid_membership_is_rejected(string center, string subject, string role)
    {
        Assert.Throws<ArgumentException>(() => new CenterMembership(center, subject, role, false));
    }
}
