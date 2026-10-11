using Kanoonyar.Contracts.Centers;
using Kanoonyar.Contracts.Programs;

namespace Kanoonyar.Contracts.Programs;

/// <summary>Checks only that a center plan references an existing exact national version.
/// It does not grant adoption, publish a plan, or infer the latest version.</summary>
public static class CenterProgramVersionBoundary
{
    public static bool MatchesExistingVersion(CenterProgramPlan plan, NationalProgramCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(catalog);
        return catalog.Find(plan.ProgramId, plan.NationalVersion) is not null;
    }

    public static bool MatchesAdoptionIntent(CenterProgramPlan plan, CenterProgramAdoptionRequest request,
        NationalProgramCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(catalog);
        return StringComparer.Ordinal.Equals(plan.CenterId, request.CenterId) &&
            StringComparer.Ordinal.Equals(plan.ProgramId, request.ProgramId) &&
            plan.NationalVersion == request.NationalVersion &&
            MatchesExistingVersion(plan, catalog);
    }
}
