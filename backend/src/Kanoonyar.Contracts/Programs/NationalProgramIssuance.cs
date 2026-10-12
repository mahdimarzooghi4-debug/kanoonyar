namespace Kanoonyar.Contracts.Programs;

/// <summary>
/// Structural evidence that one exact national program version was issued to one
/// center. This is NOT authentication, business approval, program receipt, or a
/// grant for that center to execute or view any private data.
/// </summary>
public sealed record NationalProgramIssuance
{
    public Guid IssuanceId { get; }
    public string CenterId { get; }
    public string ProgramId { get; }
    public int NationalVersion { get; }
    public string IssuerSubjectId { get; }
    public DateTimeOffset IssuedAt { get; }

    public NationalProgramIssuance(Guid issuanceId, string centerId, string programId,
        int nationalVersion, string issuerSubjectId, DateTimeOffset issuedAt)
    {
        if (issuanceId == Guid.Empty) throw new ArgumentException("Issuance ID required.", nameof(issuanceId));
        if (string.IsNullOrWhiteSpace(centerId)) throw new ArgumentException("Center required.", nameof(centerId));
        if (string.IsNullOrWhiteSpace(programId)) throw new ArgumentException("Program required.", nameof(programId));
        if (nationalVersion <= 0) throw new ArgumentOutOfRangeException(nameof(nationalVersion));
        if (string.IsNullOrWhiteSpace(issuerSubjectId))
            throw new ArgumentException("Issuer subject required.", nameof(issuerSubjectId));
        if (issuedAt == default || issuedAt.Offset != TimeSpan.Zero)
            throw new ArgumentException("Explicit UTC issuance time required.", nameof(issuedAt));

        IssuanceId = issuanceId;
        CenterId = centerId;
        ProgramId = programId;
        NationalVersion = nationalVersion;
        IssuerSubjectId = issuerSubjectId;
        IssuedAt = issuedAt;
    }
}

/// <summary>
/// Structural version/center association only. This does not assert the
/// authenticity of issuance, compliance, an operation grant, or plan approval.
/// </summary>
public static class NationalProgramIssuanceBoundary
{
    public static bool Matches(NationalProgramIssuance issuance,
        CenterProgramPlan plan, NationalProgramPublicationIndex publications)
    {
        ArgumentNullException.ThrowIfNull(issuance);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(publications);
        return StringComparer.Ordinal.Equals(issuance.CenterId, plan.CenterId) &&
               StringComparer.Ordinal.Equals(issuance.ProgramId, plan.ProgramId) &&
               issuance.NationalVersion == plan.NationalVersion &&
               publications.IsPublished(issuance.ProgramId, issuance.NationalVersion);
    }
}
