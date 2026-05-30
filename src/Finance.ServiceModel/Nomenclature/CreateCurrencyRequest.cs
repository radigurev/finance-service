namespace Finance.ServiceModel.Nomenclature;

/// <summary>
/// Request body for creating a new currency (SDD-NOM-001 §2.1).
/// </summary>
public sealed record CreateCurrencyRequest
{
    /// <summary>ISO 4217 alphabetic code (exactly three uppercase letters, e.g. "BGN").</summary>
    public required string IsoCode { get; init; }

    /// <summary>Human-readable currency name.</summary>
    public required string Name { get; init; }

    /// <summary>Optional display symbol (e.g. "лв", "€", "$").</summary>
    public string? Symbol { get; init; }

    /// <summary>Whether the currency is active on creation.</summary>
    public bool IsActive { get; init; } = true;
}
