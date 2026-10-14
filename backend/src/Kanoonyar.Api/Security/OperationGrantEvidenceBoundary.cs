namespace Kanoonyar.Api.Security;

/// <summary>
/// S51: non-authorizing proof consistency check. This is NEVER an ALLOW decision.
/// Identity must come from a trusted, already verified OIDC request context;
/// facts must come from independently authorized, version-pinned storage.
/// No grant repository, administrator action, final grant evaluator or runtime
/// promotion exists until B02/T04 delegated-rights policy is approved.
/// </summary>
public static class OperationGrantEvidenceBoundary
{
    public sealed record Request(
        string? VerifiedIssuer,
        string? VerifiedSubject,
        string? CenterId,
        string? Operation,
        string? ResourceId,
        string? ExpectedPolicyVersion,
        long? ExpectedGrantRevision);

    public sealed record Evidence(
        string? Issuer,
        string? Subject,
        string? CenterId,
        string? Operation,
        string? ResourceId,
        string? PolicyVersion,
        long GrantRevision,
        bool LiveMembership,
        bool CenterActive,
        bool GrantRevoked);

    /// <summary>
    /// Checks exact fact consistency only, not that facts are genuinely authorized.
    /// In particular, a matching result cannot be used to activate any operation.
    /// </summary>
    public static bool HasMatchingEvidence(Request? request, Evidence? evidence)
    {
        if (request is null || evidence is null ||
            string.IsNullOrWhiteSpace(request.VerifiedIssuer) ||
            string.IsNullOrWhiteSpace(request.VerifiedSubject) ||
            string.IsNullOrWhiteSpace(request.CenterId) ||
            string.IsNullOrWhiteSpace(request.Operation) ||
            string.IsNullOrWhiteSpace(request.ExpectedPolicyVersion) ||
            !request.ExpectedGrantRevision.HasValue ||
            request.ExpectedGrantRevision.Value <= 0 ||
            !evidence.LiveMembership || !evidence.CenterActive || evidence.GrantRevoked ||
            evidence.GrantRevision <= 0)
            return false;

        // No partial grants, wildcard operations or parent/other-center inheritance.
        // Resource scope has an explicit all-resources request representation (null);
        // null on both sides denotes EXACTLY the declared center-wide operation, not
        // access to another operation, center or person's data.
        if (request.ResourceId is not null && string.IsNullOrWhiteSpace(request.ResourceId))
            return false;
        if (evidence.ResourceId is not null && string.IsNullOrWhiteSpace(evidence.ResourceId))
            return false;

        return string.Equals(request.VerifiedIssuer, evidence.Issuer, StringComparison.Ordinal) &&
               string.Equals(request.VerifiedSubject, evidence.Subject, StringComparison.Ordinal) &&
               string.Equals(request.CenterId, evidence.CenterId, StringComparison.Ordinal) &&
               string.Equals(request.Operation, evidence.Operation, StringComparison.Ordinal) &&
               string.Equals(request.ResourceId, evidence.ResourceId, StringComparison.Ordinal) &&
               string.Equals(request.ExpectedPolicyVersion, evidence.PolicyVersion, StringComparison.Ordinal) &&
               request.ExpectedGrantRevision.Value == evidence.GrantRevision;
    }
}
