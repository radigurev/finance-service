using Finance.Common.Results;
using Finance.GenericFiltering.Models;
using Finance.ServiceModel.Nomenclature;

namespace Finance.Nomenclature.API.Interfaces;

/// <summary>
/// Application service for managing ISO 4217 currencies. Every method returns a
/// <see cref="Result"/> / <see cref="Result{T}"/>; business outcomes are never signalled via
/// <c>null</c> or exceptions (SDD-NOM-001 §2.1, SDD-INFRA-009).
/// </summary>
public interface ICurrencyService
{
    /// <summary>
    /// Returns a filtered, sorted, and paged page of currencies, defaulting to ascending
    /// <c>IsoCode</c> ordering. The page includes both active and inactive currencies
    /// (SDD-NOM-001 §2.1).
    /// </summary>
    /// <param name="request">The client-supplied filter, sort, and pagination request.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>A success result carrying the page, or a failure carrying the filter error code.</returns>
    Task<Result<PagedResult<CurrencyDto>>> SearchAsync(FilterRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Returns the full active-currency list used to populate dropdowns, ordered by <c>IsoCode</c>
    /// (SDD-NOM-001 §2.1).
    /// </summary>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>A success result carrying the active currencies.</returns>
    Task<Result<IReadOnlyList<CurrencyDto>>> GetActiveAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Returns the currency with the given ISO code, or a <c>CURRENCY_NOT_FOUND</c> failure when it
    /// does not exist (SDD-NOM-001 §2.1).
    /// </summary>
    /// <param name="isoCode">The ISO 4217 alphabetic code.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>A success result carrying the currency, or a not-found failure.</returns>
    Task<Result<CurrencyDto>> GetByIsoCodeAsync(string isoCode, CancellationToken cancellationToken);

    /// <summary>
    /// Creates a new currency, enforcing the cross-aggregate duplicate-code validation chain before
    /// persisting (SDD-NOM-001 §2.1, §3).
    /// </summary>
    /// <param name="request">The create request body.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>A success result carrying the created currency, or a validation/conflict failure.</returns>
    Task<Result<CurrencyDto>> CreateAsync(CreateCurrencyRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Updates the mutable fields (<c>Name</c>, <c>Symbol</c>, <c>IsActive</c>) of an existing currency
    /// identified by its immutable ISO code, enforcing optimistic concurrency (SDD-NOM-001 §2.1, §2.6).
    /// </summary>
    /// <param name="isoCode">The immutable ISO 4217 alphabetic code identifying the currency.</param>
    /// <param name="request">The update request body.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>A success result carrying the updated currency, or a not-found / concurrency failure.</returns>
    Task<Result<CurrencyDto>> UpdateAsync(
        string isoCode,
        UpdateCurrencyRequest request,
        CancellationToken cancellationToken);
}
