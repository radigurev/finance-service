using Finance.GenericFiltering.Models;
using Finance.ServiceModel.Accounts;
using Refit;

namespace Finance.Journal.API.Interfaces;

/// <summary>
/// Refit contract for the Accounts read endpoint consumed through the Finance Gateway
/// (SDD-FIN-001 §2.6, resolved §7). The Journal service owns only <c>finance_journal</c> and MUST NOT
/// cross-database-join into <c>finance_accounts</c>; account postability is asserted at draft/post time
/// via a synchronous read of <c>GET /api/v1/accounts/{id}</c>. Registered with the standard handler
/// chain (<c>CorrelationIdDelegatingHandler</c> → bearer forwarding → resilience).
/// </summary>
public interface IAccountReadClient
{
    /// <summary>Reads a single account by its surrogate identifier.</summary>
    /// <param name="id">The surrogate account identifier.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>The account when found; otherwise an API error captured by the caller.</returns>
    [Get("/api/v1/accounts/{id}")]
    Task<AccountDto> GetAccountAsync(int id, CancellationToken cancellationToken);

    /// <summary>
    /// Lists accounts whose <c>Code</c> equals <paramref name="value"/> via the filtered list endpoint,
    /// used by the Posting Engine to resolve an <c>AccountSelector</c> code to a postable account id
    /// (SDD-FIN-006 §2.2). The query parameters bind to a single <c>eq</c> filter clause on the
    /// <c>[Filterable]</c> <c>Code</c> property.
    /// </summary>
    /// <param name="field">The filterable field name (always <c>Code</c>).</param>
    /// <param name="op">The filter operator wire token (always <c>eq</c>).</param>
    /// <param name="value">The account code to match.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>A paged result of matching accounts (expected to contain at most one).</returns>
    [Get("/api/v1/accounts")]
    Task<PagedResult<AccountDto>> FindAccountsByCodeAsync(
        [AliasAs("Filters[0].Field")] string field,
        [AliasAs("Filters[0].Operator")] string op,
        [AliasAs("Filters[0].Value")] string value,
        CancellationToken cancellationToken);
}
