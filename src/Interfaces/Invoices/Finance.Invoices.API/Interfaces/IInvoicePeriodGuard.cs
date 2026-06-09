using Finance.Common.Results;

namespace Finance.Invoices.API.Interfaces;

/// <summary>
/// Extension seam for the fiscal-period lock at confirm (SDD-INV-001 §2.2; SDD-FIN-004). The
/// <c>Draft → Confirmed</c> transition consults this guard to ask whether the period for an invoice's
/// issue date is open. The default <c>AlwaysOpenInvoicePeriodGuard</c> always allows; SDD-FIN-004 supplies
/// the real period-status lookup that rejects closed/locked periods with <c>INVOICE_PERIOD_CLOSED</c>. The
/// only difference between environments is the DI registration of this guard, mirroring SDD-FIN-002 §2.7.
/// </summary>
public interface IInvoicePeriodGuard
{
    /// <summary>
    /// Determines whether an invoice with the supplied issue date may be confirmed into its fiscal period.
    /// Returns <see cref="Result.Success"/> when postable, or <see cref="Result.Failure"/> with
    /// <c>INVOICE_PERIOD_CLOSED</c> when the period is closed/locked.
    /// </summary>
    /// <param name="issueDate">The issue date whose period is being checked.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>A success result when confirmable; otherwise an <c>INVOICE_PERIOD_CLOSED</c> failure.</returns>
    Task<Result> EnsureOpenAsync(DateTimeOffset issueDate, CancellationToken cancellationToken);
}
