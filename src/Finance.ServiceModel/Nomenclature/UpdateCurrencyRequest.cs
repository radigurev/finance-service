namespace Finance.ServiceModel.Nomenclature;

/// <summary>
/// Request body for updating mutable fields on an existing currency (SDD-NOM-001 §2.1, §2.6).
/// The <c>IsoCode</c> is immutable after creation: the path code is authoritative and a body that
/// attempts to change it is rejected.
/// </summary>
public sealed record UpdateCurrencyRequest
{
    /// <summary>Human-readable currency name.</summary>
    public required string Name { get; init; }

    /// <summary>Optional display symbol (e.g. "лв", "€", "$").</summary>
    public string? Symbol { get; init; }

    /// <summary>Whether the currency is active and offered in dropdowns.</summary>
    public required bool IsActive { get; init; }

    /// <summary>
    /// Base64-encoded <c>rowversion</c> token captured from the prior read, used for optimistic
    /// concurrency. A stale token is rejected with <c>CONCURRENT_MODIFICATION</c>.
    /// </summary>
    public required string RowVersion { get; init; }
}
