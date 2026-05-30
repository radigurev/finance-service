using Finance.Common.Results;
using Finance.ServiceModel.Nomenclature;

namespace Finance.Nomenclature.API.Interfaces;

/// <summary>
/// Read-only application service for currency exchange rates (SDD-NOM-001 §2.2). All reads hit the
/// database directly and are never cached because exchange rates are transactional data
/// (SDD-INFRA-004). Every method returns a <see cref="Result{T}"/>.
/// </summary>
public interface IExchangeRateService
{
    /// <summary>
    /// Returns the latest exchange rate on or before the supplied date for the given currency. Yields
    /// <c>CURRENCY_NOT_FOUND</c> when the currency is unknown and <c>EXCHANGE_RATE_NOT_FOUND</c> when no
    /// rate exists on or before the date (SDD-NOM-001 §2.2, §2.6).
    /// </summary>
    /// <param name="isoCode">The ISO 4217 alphabetic code of the currency.</param>
    /// <param name="date">The inclusive upper-bound date.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>A success result carrying the rate, or a not-found failure.</returns>
    Task<Result<ExchangeRateDto>> GetLatestRateAsync(
        string isoCode,
        DateTimeOffset date,
        CancellationToken cancellationToken);

    /// <summary>
    /// Returns every exchange rate for the given currency within the inclusive <paramref name="from"/>
    /// — <paramref name="to"/> range, ordered ascending by date. Yields <c>INVALID_DATE_RANGE</c> when
    /// <paramref name="from"/> is later than <paramref name="to"/> and <c>CURRENCY_NOT_FOUND</c> when the
    /// currency is unknown (SDD-NOM-001 §2.2, §3).
    /// </summary>
    /// <param name="isoCode">The ISO 4217 alphabetic code of the currency.</param>
    /// <param name="from">The inclusive lower-bound date.</param>
    /// <param name="to">The inclusive upper-bound date.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>A success result carrying the ordered rates, or a validation / not-found failure.</returns>
    Task<Result<IReadOnlyList<ExchangeRateDto>>> GetRateRangeAsync(
        string isoCode,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken);
}
