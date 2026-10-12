namespace Kanoonyar.BuildingBlocks;

/// <summary>
/// Identifies an opaque request correlation value for diagnostics only.
/// Never represents an authenticated principal, authorization grant, or tenant.
/// </summary>
public readonly record struct CorrelationId
{
    public string Value { get; }

    public CorrelationId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Correlation identifier is required.", nameof(value));
        Value = value;
    }

    public override string ToString() => Value;
}
