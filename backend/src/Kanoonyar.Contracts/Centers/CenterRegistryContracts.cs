namespace Kanoonyar.Contracts.Centers;

/// <summary>A headquarters-visible center registry record, not an authentication or authorization grant.</summary>
public sealed record CenterRegistryEntry
{
    public string CenterId { get; }
    public string DisplayName { get; }
    public bool IsActive { get; }

    public CenterRegistryEntry(string centerId, string displayName, bool isActive)
    {
        if (string.IsNullOrWhiteSpace(centerId)) throw new ArgumentException("Center ID is required.", nameof(centerId));
        if (string.IsNullOrWhiteSpace(displayName)) throw new ArgumentException("Display name is required.", nameof(displayName));
        CenterId = centerId;
        DisplayName = displayName;
        IsActive = isActive;
    }
}

/// <summary>Explicit human-managed association. It cannot be used as an authorization decision by itself.</summary>
public sealed record CenterMembership
{
    public string CenterId { get; }
    public string SubjectId { get; }
    public string RoleCode { get; }

    public CenterMembership(string centerId, string subjectId, string roleCode)
    {
        if (string.IsNullOrWhiteSpace(centerId)) throw new ArgumentException("Center ID is required.", nameof(centerId));
        if (string.IsNullOrWhiteSpace(subjectId)) throw new ArgumentException("Subject ID is required.", nameof(subjectId));
        if (string.IsNullOrWhiteSpace(roleCode)) throw new ArgumentException("Role code is required.", nameof(roleCode));
        CenterId = centerId;
        SubjectId = subjectId;
        RoleCode = roleCode;
    }
}
