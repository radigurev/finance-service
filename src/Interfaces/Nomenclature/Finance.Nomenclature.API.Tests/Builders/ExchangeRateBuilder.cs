using Finance.Nomenclature.DBModel.Models;

namespace Finance.Nomenclature.API.Tests.Builders;

/// <summary>
/// Builds <see cref="ExchangeRate"/> entities for the Nomenclature unit tests. Default values produce a
/// valid rate; tests override only the fields under test.
/// </summary>
public sealed class ExchangeRateBuilder
{
    private string _currencyIsoCode = "USD";
    private decimal _rate = 1.800000m;
    private DateTimeOffset _rateDate = new(2026, 5, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>Creates a new builder seeded with valid defaults.</summary>
    /// <returns>A new <see cref="ExchangeRateBuilder"/>.</returns>
    public static ExchangeRateBuilder Create() => new();

    /// <summary>Sets the currency ISO code the rate applies to.</summary>
    /// <param name="isoCode">The three-letter currency code.</param>
    /// <returns>This builder.</returns>
    public ExchangeRateBuilder WithCurrencyIsoCode(string isoCode)
    {
        _currencyIsoCode = isoCode;
        return this;
    }

    /// <summary>Sets the rate value.</summary>
    /// <param name="rate">The exchange rate with six-decimal precision.</param>
    /// <returns>This builder.</returns>
    public ExchangeRateBuilder WithRate(decimal rate)
    {
        _rate = rate;
        return this;
    }

    /// <summary>Sets the date the rate applies on.</summary>
    /// <param name="rateDate">The time-zone-aware rate date.</param>
    /// <returns>This builder.</returns>
    public ExchangeRateBuilder WithRateDate(DateTimeOffset rateDate)
    {
        _rateDate = rateDate;
        return this;
    }

    /// <summary>Builds the configured <see cref="ExchangeRate"/> entity.</summary>
    /// <returns>A new <see cref="ExchangeRate"/>.</returns>
    public ExchangeRate Build() => new()
    {
        CurrencyIsoCode = _currencyIsoCode,
        Rate = _rate,
        RateDate = _rateDate
    };
}
