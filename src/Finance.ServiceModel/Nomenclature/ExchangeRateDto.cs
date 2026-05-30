namespace Finance.ServiceModel.Nomenclature;

/// <summary>
/// Representation of a currency exchange rate on a given date exposed by the Nomenclature API
/// (SDD-NOM-001 §2.2). These reads are transactional and are never cached.
/// </summary>
public sealed record ExchangeRateDto
{
    /// <summary>ISO 4217 alphabetic code of the currency this rate applies to.</summary>
    public required string CurrencyIsoCode { get; init; }

    /// <summary>The exchange rate, carrying six-decimal precision.</summary>
    public required decimal Rate { get; init; }

    /// <summary>The time-zone-aware date the rate applies on.</summary>
    public required DateTimeOffset RateDate { get; init; }
}
