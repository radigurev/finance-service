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
}
