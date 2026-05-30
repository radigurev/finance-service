namespace Finance.ServiceModel.Nomenclature;

/// <summary>
/// Representation of an ISO 4217 currency exposed by the Nomenclature API (SDD-NOM-001 §2.1).
/// </summary>
public sealed record CurrencyDto
{
    /// <summary>Surrogate identifier of the currency.</summary>
    public required int Id { get; init; }

    /// <summary>ISO 4217 alphabetic code (three uppercase letters, e.g. "BGN", "EUR").</summary>
    public required string IsoCode { get; init; }

    /// <summary>Human-readable currency name.</summary>
    public required string Name { get; init; }

    /// <summary>Optional display symbol (e.g. "лв", "€", "$").</summary>
    public string? Symbol { get; init; }

    /// <summary>Whether the currency is active and offered in dropdowns.</summary>
    public required bool IsActive { get; init; }

    /// <summary>
    /// Base64-encoded SQL Server <c>rowversion</c> optimistic-concurrency token. Clients round-trip
    /// this value back on update so a stale write is rejected with <c>CONCURRENT_MODIFICATION</c>.
    /// </summary>
    public required string RowVersion { get; init; }
}
