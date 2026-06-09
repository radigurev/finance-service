using Finance.Common.Results;
using Finance.Invoices.API.Interfaces;

namespace Finance.Invoices.API.Services;

/// <summary>
/// Default <see cref="IInvoicePeriodGuard"/> that treats every period as open (SDD-INV-001 §2.2). It is the
/// active guard until SDD-FIN-004's period-status lookup is reachable from this service; at that point a
/// gateway-backed guard replaces this registration and returns <c>INVOICE_PERIOD_CLOSED</c> for
/// closed/locked periods. Mirrors the Journal service's <c>AlwaysOpenPostingPeriodGuard</c> seam.
/// </summary>
public sealed class AlwaysOpenInvoicePeriodGuard : IInvoicePeriodGuard
{
    /// <inheritdoc />
    public Task<Result> EnsureOpenAsync(DateTimeOffset issueDate, CancellationToken cancellationToken)
    {
        return Task.FromResult(Result.Success());
    }
}
