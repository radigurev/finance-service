using Finance.Common.Results;

namespace Finance.Payments.API.Interfaces;

/// <summary>
/// The fiscal-period guard seam for the payment lifecycle (SDD-PAY-001 §2.9; SDD-FIN-004 §2.7). Production
/// registers <c>GatewayPaymentPeriodGuard</c>, which performs the real period-status lookup through the
/// Finance Gateway from day one — deliberately NOT the Invoices service's always-open registration. The
/// always-open stub exists for tests only and MUST NOT be registered in production.
/// <para>The guard runs on the two reachable operator paths — <c>Draft → Confirmed</c> (through
/// <c>PaymentPeriodWorkflowGuard</c>) and <c>POST /{id}/reverse</c> — plus the unreachable
/// defense-in-depth pre-check on the operator post. The back-event link path is exempt by design: the Journal
/// service has already asserted postability of the same date, and re-checking would poison the consumer into
/// a permanent retry loop while the GL already holds the entry.</para>
/// </summary>
public interface IPaymentPeriodGuard
{
    /// <summary>
    /// Determines whether a payment with the supplied date may be confirmed or reversed into its fiscal
    /// period. Returns <see cref="Result.Success"/> when the period is open, or <see cref="Result.Failure"/>
    /// with <c>PAYMENT_PERIOD_CLOSED</c> when the period is closed, when no period covers the date, or when
    /// the Periods service cannot confirm it is open (fail closed).
    /// </summary>
    /// <param name="paymentDate">The payment date whose period is being checked.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>A success result when the period is open; otherwise a <c>PAYMENT_PERIOD_CLOSED</c> failure.</returns>
    Task<Result> EnsureOpenAsync(DateTimeOffset paymentDate, CancellationToken cancellationToken);
}
