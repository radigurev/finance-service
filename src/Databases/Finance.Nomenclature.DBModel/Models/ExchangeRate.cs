using Finance.GenericFiltering.Attributes;

namespace Finance.Nomenclature.DBModel.Models;

/// <summary>
/// Persistent representation of a currency exchange rate on a given date (SDD-NOM-001 §2.0). The
/// Nomenclature service OWNS this table since the planned <c>Finance.Currency.API</c> (SDD-FIN-005)
/// is out of scope. Batch 5 exposes READ access only; the write/BNB-import path is deferred.
/// </summary>
public sealed class ExchangeRate
{
    /// <summary>Surrogate identifier.</summary>
    public int Id { get; set; }

    /// <summary>ISO 4217 alphabetic code of the currency this rate applies to.</summary>
    [Filterable]
    [Sortable]
    public required string CurrencyIsoCode { get; set; }

    /// <summary>The exchange rate, stored with six-decimal precision (<c>DECIMAL(18,6)</c>).</summary>
    [Filterable]
    [Sortable]
    public decimal Rate { get; set; }

    /// <summary>The time-zone-aware date the rate applies on.</summary>
    [Filterable]
    [Sortable]
    public DateTimeOffset RateDate { get; set; }
}
