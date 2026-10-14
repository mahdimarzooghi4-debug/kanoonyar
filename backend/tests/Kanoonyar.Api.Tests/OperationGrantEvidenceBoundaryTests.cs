using Kanoonyar.Api.Security;
using Xunit;
using Boundary = Kanoonyar.Api.Security.OperationGrantEvidenceBoundary;

namespace Kanoonyar.Api.Tests;

public sealed class OperationGrantEvidenceBoundaryTests
{
    private static Boundary.Request Request() => new(
        "https://oidc.example.invalid/realms/kanoonyar",
        "oidc-subject-a",
        "center-a",
        "CENTER_CLASS_CATALOGUE_READ",
        null,
        "external-policy-version-verified",
        7);
    private static Boundary.Evidence Evidence() => new(
        "https://oidc.example.invalid/realms/kanoonyar",
        "oidc-subject-a",
        "center-a",
        "CENTER_CLASS_CATALOGUE_READ",
        null,
        "external-policy-version-verified",
        7,
        LiveMembership: true,
        CenterActive: true,
        GrantRevoked: false);

    [Fact]
    public void A_pinned_consistent_fact_set_is_only_matching_evidence_not_authorization()
    {
        Assert.True(Boundary.HasMatchingEvidence(Request(), Evidence()));
        Assert.False(Boundary.HasMatchingEvidence(null, Evidence()));
        Assert.False(Boundary.HasMatchingEvidence(Request(), null));
    }

    [Fact]
    public void Fake_role_or_membership_does_not_compensate_for_a_missing_grant_or_wrong_identity()
    {
        var r = Request();
        var e = Evidence();
        Assert.False(Boundary.HasMatchingEvidence(r, e with { Subject = "superadmin" }));
        Assert.False(Boundary.HasMatchingEvidence(r, e with { Issuer = "https://other-issuer.invalid" }));
        Assert.False(Boundary.HasMatchingEvidence(r with { VerifiedSubject = null }, e));
        Assert.False(Boundary.HasMatchingEvidence(r with { VerifiedIssuer = " " }, e));
        Assert.False(Boundary.HasMatchingEvidence(r with { CenterId = null }, e));
        Assert.False(Boundary.HasMatchingEvidence(r with { Operation = "" }, e));
        Assert.False(Boundary.HasMatchingEvidence(r with { ExpectedPolicyVersion = "" }, e));
        Assert.False(Boundary.HasMatchingEvidence(r with { ExpectedGrantRevision = null }, e));
    }

    [Fact]
    public void Exact_scope_revision_policy_and_current_membership_cannot_be_bypassed()
    {
        var r = Request();
        var e = Evidence();
        Assert.False(Boundary.HasMatchingEvidence(r, e with { CenterId = "center-b" }));
        Assert.False(Boundary.HasMatchingEvidence(r, e with { Operation = "FINANCE_PAYMENT_APPROVE" }));
        Assert.False(Boundary.HasMatchingEvidence(r, e with { PolicyVersion = "older" }));
        Assert.False(Boundary.HasMatchingEvidence(r, e with { GrantRevision = 6 }));
        Assert.False(Boundary.HasMatchingEvidence(r, e with { LiveMembership = false }));
        Assert.False(Boundary.HasMatchingEvidence(r, e with { CenterActive = false }));
        Assert.False(Boundary.HasMatchingEvidence(r, e with { GrantRevoked = true }));
        Assert.False(Boundary.HasMatchingEvidence(r with { ExpectedGrantRevision = 0 }, e));
        Assert.False(Boundary.HasMatchingEvidence(r, e with { GrantRevision = 0 }));
        Assert.False(Boundary.HasMatchingEvidence(r, e with { Operation = "CENTER_CLASS_CATALOGUE_READ*" }));
    }

    [Fact]
    public void Resource_specific_evidence_never_escalates_to_parent_or_other_resource()
    {
        var r = Request();
        var e = Evidence();
        Assert.False(Boundary.HasMatchingEvidence(r with { ResourceId = "resource-one" }, e));
        Assert.False(Boundary.HasMatchingEvidence(r, e with { ResourceId = "resource-one" }));
        Assert.True(Boundary.HasMatchingEvidence(r with { ResourceId = "resource-one" },
            e with { ResourceId = "resource-one" }));
        Assert.False(Boundary.HasMatchingEvidence(r with { ResourceId = "resource-two" },
            e with { ResourceId = "resource-one" }));
        Assert.False(Boundary.HasMatchingEvidence(r with { ResourceId = " " },
            e with { ResourceId = " " }));
    }

    [Fact]
    public void Synthetic_eighty_tenant_contexts_cannot_share_another_tenants_grant_evidence()
    {
        var requests = Enumerable.Range(0, 80).Select(i =>
            Request() with { CenterId = $"synthetic-center-{i:00}", VerifiedSubject = $"subject-{i:00}" }).ToArray();
        var facts = Enumerable.Range(0, 80).Select(i =>
            Evidence() with { CenterId = $"synthetic-center-{i:00}", Subject = $"subject-{i:00}" }).ToArray();
        for (var i = 0; i < 80; i++)
        {
            Assert.True(Boundary.HasMatchingEvidence(requests[i], facts[i]));
            Assert.False(Boundary.HasMatchingEvidence(requests[i], facts[(i + 1) % 80]));
        }
    }
}
