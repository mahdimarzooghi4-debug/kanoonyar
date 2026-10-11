namespace Kanoonyar.BuildingBlocks;

/// <summary>
/// Identifies a tenant context without assuming any authorization, identity or tenancy policy.
/// An empty value is invalid; callers must obtain identity from a trusted boundary.
/// </summary>
public readonly record struct TenantId
{
    public string Value { get; }

    public TenantId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Tenant identifier is required.", nameof(value));
        Value = value;
    }

    public override string ToString() => Value;
}
