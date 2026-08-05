using Finance.ServiceModel.Accounts;
using Refit;

namespace Finance.Payments.API.Interfaces;

/// <summary>
/// Refit contract for the Accounts read endpoint consumed through the Finance Gateway
/// (SDD-PAY-001 §2.8; SDD-ACCT-001). The Payments service owns only <c>finance_payments</c> and MUST NOT
/// cross-database-join into <c>finance_accounts</c>; settlement-account existence and activeness are asserted
/// at create/update/confirm time via a synchronous read of <c>GET /api/v1/accounts/{id}</c>. Registered with
/// the standard handler chain (<c>CorrelationIdDelegatingHandler</c> → bearer forwarding → resilience).
/// <para>This is a per-service copy: the shipped contract of the same name is private to
/// <c>Finance.Journal.API</c>. Promoting it into <c>Finance.Infrastructure.Web</c> is a recorded
/// SDD-INFRA-009 change (SDD-PAY-001 §2.8, §7).</para>
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
