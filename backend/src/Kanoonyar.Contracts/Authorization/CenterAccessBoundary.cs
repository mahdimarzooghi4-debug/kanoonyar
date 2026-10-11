using Kanoonyar.Contracts.Centers;

namespace Kanoonyar.Contracts.Authorization;

/// <summary>Explicit evidence from an authenticated identity and authoritative membership lookup.
/// The caller is responsible for verifying OIDC token and current membership.</summary>
public sealed record CenterAccessEvidence
{
    public string AuthenticatedSubjectId { get; }
    public string RequestedCenterId { get; }
    public CenterRegistryEntry Center { get; }
    public CenterMembership? Membership { get; }

    public CenterAccessEvidence(string authenticatedSubjectId, string requestedCenterId,
        CenterRegistryEntry center, CenterMembership? membership)
    {
        if (string.IsNullOrWhiteSpace(authenticatedSubjectId)) throw new ArgumentException("Authenticated subject required.", nameof(authenticatedSubjectId));
        if (string.IsNullOrWhiteSpace(requestedCenterId)) throw new ArgumentException("Requested center required.", nameof(requestedCenterId));
        AuthenticatedSubjectId = authenticatedSubjectId;
        RequestedCenterId = requestedCenterId;
        Center = center ?? throw new ArgumentNullException(nameof(center));
        Membership = membership;
    }
}

public enum CenterAccessResult
{
    Denied = 0,
    MembershipMatched = 1
}

/// <summary>Structural cross-center isolation guard; NOT a permission/role policy.</summary>
public static class CenterAccessBoundary
{
    public static CenterAccessResult Check(CenterAccessEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        if (!evidence.Center.IsActive ||
            !StringComparer.Ordinal.Equals(evidence.RequestedCenterId, evidence.Center.CenterId) ||
            evidence.Membership is null ||
            !StringComparer.Ordinal.Equals(evidence.RequestedCenterId, evidence.Membership.CenterId) ||
            !StringComparer.Ordinal.Equals(evidence.AuthenticatedSubjectId, evidence.Membership.SubjectId))
            return CenterAccessResult.Denied;
        return CenterAccessResult.MembershipMatched;
    }
}
