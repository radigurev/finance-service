using Finance.Common.Results;
using Finance.ServiceModel.Accounts;

namespace Finance.Payments.API.Interfaces;

/// <summary>
/// The settlement-account validation seam (SDD-PAY-001 §2.8; SDD-ACCT-001). Asserts that the cash/bank GL
/// account a payment is recorded against exists and is active, reading it through the Accounts read seam
/// (Refit through the Finance Gateway) — never a cross-database join and never a foreign key.
/// <para>Resolution: missing/404 ⇒ <c>PAYMENT_SETTLEMENT_ACCOUNT_NOT_FOUND</c>; found but inactive ⇒
/// <c>PAYMENT_SETTLEMENT_ACCOUNT_INACTIVE</c>; unreachable ⇒ <b>fail closed</b> with
/// <c>PAYMENT_SETTLEMENT_ACCOUNT_NOT_FOUND</c> (financial safety over availability).</para>
/// </summary>
public interface ISettlementAccountReader
{
    /// <summary>
    /// Asserts that the supplied settlement account exists and is active.
    /// </summary>
    /// <param name="settlementAccountId">The surrogate identifier of the cash/bank GL account.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>
    /// A success result when the account is usable; otherwise a
    /// <c>PAYMENT_SETTLEMENT_ACCOUNT_NOT_FOUND</c> or <c>PAYMENT_SETTLEMENT_ACCOUNT_INACTIVE</c> failure.
    /// </returns>
    Task<Result> EnsureUsableAsync(int settlementAccountId, CancellationToken cancellationToken);

    /// <summary>
    /// The DORMANT "account is postable / a non-header leaf" strictness predicate (SDD-PAY-001 §2.8). The
    /// Chart of Accounts exposes no <c>IsPostable</c>/<c>IsHeader</c> flag today, so this returns
    /// <c>true</c> unconditionally in v1. Expressing the assertion as a single named predicate localizes the
    /// future change: when <c>CHG-ENH-002</c> lands only this body changes and no call site moves.
    /// </summary>
    /// <param name="account">The account read through the Accounts seam.</param>
    /// <returns><c>true</c> in v1, unconditionally.</returns>
    bool IsPostable(AccountDto account);
}
