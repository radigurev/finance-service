using Finance.Common.Results;
using Finance.GenericFiltering.Models;
using Finance.ServiceModel.Accounts;

namespace Finance.Accounts.API.Interfaces;

/// <summary>
/// Application service for managing the chart of accounts. Every method returns a
/// <see cref="Result"/> / <see cref="Result{T}"/>; business outcomes are never signalled
/// via <c>null</c> or exceptions (SDD-ACCT-001 §2, SDD-INFRA-009).
/// </summary>
public interface IAccountService
{
    /// <summary>
    /// Returns a filtered, sorted, and paged page of accounts, defaulting to ascending
    /// <c>CountryCode</c> then <c>Code</c> ordering (SDD-ACCT-001 §2.1).
    /// </summary>
    /// <param name="request">The client-supplied filter, sort, and pagination request.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>A success result carrying the page, or a failure carrying the filter error code.</returns>
    Task<Result<PagedResult<AccountDto>>> SearchAsync(FilterRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Returns the account with the given ID, or an <c>ACCOUNT_NOT_FOUND</c> failure when it
    /// does not exist (SDD-ACCT-001 §2.2). The single-account read is served from the reference-read
    /// cache under <c>finance-accounts:account:{id}</c> (SDD-INFRA-004).
    /// </summary>
    /// <param name="id">The surrogate account identifier.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>A success result carrying the account, or a not-found failure.</returns>
    Task<Result<AccountDto>> GetAsync(int id, CancellationToken cancellationToken);

    /// <summary>
    /// Returns the full active chart of accounts used to populate dropdowns, ordered by
    /// <c>CountryCode</c> then <c>Code</c> and served from the reference-read cache under
    /// <c>finance-accounts:chart:all</c> (SDD-ACCT-001 §2.7, SDD-INFRA-004).
    /// </summary>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>A success result carrying the active chart.</returns>
    Task<Result<IReadOnlyList<AccountDto>>> GetActiveChartAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Creates a new account in the chart for the supplied country, enforcing the cross-aggregate
    /// duplicate-code and parent validation chain before persisting (SDD-ACCT-001 §2.3).
    /// </summary>
    /// <param name="request">The create request body.</param>
    /// <param name="countryCode">The owning country code derived from configuration.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>A success result carrying the created account, or a validation/conflict failure.</returns>
    Task<Result<AccountDto>> CreateAsync(
        CreateAccountRequest request,
        string countryCode,
        CancellationToken cancellationToken);

    /// <summary>
    /// Updates the mutable fields (<c>Name</c>, <c>IsActive</c>) of an existing account, enforcing
    /// optimistic concurrency via the supplied row version (SDD-ACCT-001 §2.4, §2.10).
    /// </summary>
    /// <param name="id">The surrogate account identifier.</param>
    /// <param name="request">The update request body.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>
    /// A success result carrying the updated account, or a not-found / concurrency failure.
    /// </returns>
    Task<Result<AccountDto>> UpdateAsync(
        int id,
        UpdateAccountRequest request,
        CancellationToken cancellationToken);
}
