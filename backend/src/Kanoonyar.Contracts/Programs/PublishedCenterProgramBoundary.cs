using Kanoonyar.Contracts.Authorization;
using Kanoonyar.Contracts.Centers;

namespace Kanoonyar.Contracts.Programs;

/// <summary>Structural prerequisites only; not an operation-specific permission decision.</summary>
public static class PublishedCenterProgramBoundary
{
    public static bool Matches(CenterProgramAdoptionRequest request, CenterAccessEvidence evidence,
        CenterProgramPlan plan, NationalProgramCatalog catalog, NationalProgramPublicationIndex publication)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(evidence);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(publication);
        return CenterProgramAdoptionBoundary.Matches(request, evidence) &&
            CenterProgramVersionBoundary.MatchesAdoptionIntent(plan, request, catalog) &&
            publication.IsPublished(request.ProgramId, request.NationalVersion);
    }
}
