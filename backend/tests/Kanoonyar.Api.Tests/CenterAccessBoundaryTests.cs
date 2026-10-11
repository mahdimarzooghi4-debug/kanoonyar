using Kanoonyar.Contracts.Authorization;
using Kanoonyar.Contracts.Centers;
using Xunit;

namespace Kanoonyar.Api.Tests;

public sealed class CenterAccessBoundaryTests
{
    private static CenterAccessEvidence Evidence(string subject = "user-a", string requested = "center-a",
        string actualCenter = "center-a", string memberCenter = "center-a",
        string memberSubject = "user-a", bool active = true, bool membership = true, bool memberActive = true) =>
        new(subject, requested, new CenterRegistryEntry(actualCenter, "Center", active),
            membership ? new CenterMembership(memberCenter, memberSubject, "uninterpreted-role", memberActive) : null);

    [Fact]
    public void Exact_active_center_and_authenticated_subject_match_is_only_structurally_matched()
    {
        Assert.Equal(CenterAccessResult.MembershipMatched, CenterAccessBoundary.Check(Evidence()));
    }

    [Fact]
    public void Cross_center_membership_never_matches_requested_center()
    {
        Assert.Equal(CenterAccessResult.Denied, CenterAccessBoundary.Check(Evidence(memberCenter: "center-b")));
        Assert.Equal(CenterAccessResult.Denied, CenterAccessBoundary.Check(Evidence(actualCenter: "center-b")));
    }

    [Fact]
    public void Other_subjects_membership_is_denied()
    {
        Assert.Equal(CenterAccessResult.Denied, CenterAccessBoundary.Check(Evidence(memberSubject: "user-b")));
    }

    [Fact]
    public void Missing_membership_and_inactive_center_fail_closed()
    {
        Assert.Equal(CenterAccessResult.Denied, CenterAccessBoundary.Check(Evidence(membership: false)));
        Assert.Equal(CenterAccessResult.Denied, CenterAccessBoundary.Check(Evidence(active: false)));
    }

    [Fact]
    public void Revoked_membership_fails_closed_even_when_center_is_active() =>
        Assert.Equal(CenterAccessResult.Denied, CenterAccessBoundary.Check(Evidence(memberActive: false)));

    [Fact]
    public void Identifiers_use_exact_ordinal_comparison()
    {
        Assert.Equal(CenterAccessResult.Denied, CenterAccessBoundary.Check(Evidence(requested: "CENTER-A")));
        Assert.Equal(CenterAccessResult.Denied, CenterAccessBoundary.Check(Evidence(subject: "USER-A")));
    }

    [Fact]
    public void Unauthenticated_or_missing_center_input_is_invalid()
    {
        Assert.Throws<ArgumentException>(() => Evidence(subject: " "));
        Assert.Throws<ArgumentException>(() => Evidence(requested: ""));
    }
}
