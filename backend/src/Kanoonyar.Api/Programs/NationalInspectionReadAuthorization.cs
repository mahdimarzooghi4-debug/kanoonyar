using System.Security.Claims;

namespace Kanoonyar.Api.Programs;

/// <summary>Distinct read surfaces that a future approved HQ policy must evaluate.</summary>
public enum NationalInspectionReadOperation
{
    RollupPage,
    RollupCsvExport,
    InspectionDossierPage,
    InspectionDossierCsvExport
}

/// <summary>
/// Normalized request scope passed to HQ policy evaluation. This describes the
/// requested data only; it does not define roles, grants, or approval rules.
/// </summary>
public sealed record NationalInspectionReadScope(
    NationalInspectionReadOperation Operation,
    string? TargetProgramId,
    int? TargetNationalVersion,
    bool FindingsOnly,
    string? FindingCode);

public interface INationalInspectionReadAuthorization
{
    ValueTask<bool> IsAuthorizedAsync(
        ClaimsPrincipal actor,
        NationalInspectionReadScope scope,
        CancellationToken cancellationToken);
}

/// <summary>No approved HQ read policy exists; deny every runtime request.</summary>
public sealed class UnapprovedNationalInspectionReadAuthorization : INationalInspectionReadAuthorization
{
    public ValueTask<bool> IsAuthorizedAsync(
        ClaimsPrincipal actor,
        NationalInspectionReadScope scope,
        CancellationToken cancellationToken) => ValueTask.FromResult(false);
}
