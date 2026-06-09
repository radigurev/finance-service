using Finance.Common.ErrorCodes;
using Finance.Common.Results;
using Finance.Invoices.API.Interfaces;

namespace Finance.Invoices.API.Tests.Fixtures;

/// <summary>
/// Configurable <see cref="IInvoicePeriodGuard"/> for the Invoices unit tests (SDD-INV-001 §2.2, §2.13).
/// Allows every period by default (mirroring the production <c>AlwaysOpenInvoicePeriodGuard</c>); a test sets
/// <see cref="IsOpen"/> to <c>false</c> to exercise the deferred SDD-FIN-004 closed-period seam, which
/// short-circuits the <c>Draft → Confirmed</c> transition with <c>INVOICE_PERIOD_CLOSED</c>.
/// </summary>
public sealed class FakeInvoicePeriodGuard : IInvoicePeriodGuard
{
    /// <summary>Whether the guard reports the issue date's period as open. Defaults to <c>true</c>.</summary>
    public bool IsOpen { get; set; } = true;

    /// <inheritdoc />
    public Task<Result> EnsureOpenAsync(DateTimeOffset issueDate, CancellationToken cancellationToken)
    {
        return Task.FromResult(IsOpen
            ? Result.Success()
            : Result.Failure(InvoiceErrorCodes.INVOICE_PERIOD_CLOSED));
    }
}
