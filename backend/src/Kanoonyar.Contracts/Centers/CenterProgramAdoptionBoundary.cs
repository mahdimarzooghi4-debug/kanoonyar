using Kanoonyar.Contracts.Authorization;
using Kanoonyar.Contracts.Programs;

namespace Kanoonyar.Contracts.Centers;

/// <summary>Checks that an adoption intent is structurally bound to the matched center and subject.
/// MembershipMatched is not permission to adopt a national program.</summary>
public static class CenterProgramAdoptionBoundary
{
    public static bool Matches(CenterProgramAdoptionRequest request, CenterAccessEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(evidence);
        return CenterAccessBoundary.Check(evidence) == CenterAccessResult.MembershipMatched &&
            StringComparer.Ordinal.Equals(request.CenterId, evidence.RequestedCenterId) &&
            StringComparer.Ordinal.Equals(request.ActorSubjectId, evidence.AuthenticatedSubjectId);
    }
}
