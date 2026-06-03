using Finance.Common.Results;

namespace Finance.Journal.API.Interfaces;

/// <summary>
/// Extension seam for the fiscal-period lock (SDD-FIN-002 §2.7; SDD-FIN-004). The <c>Draft → Posted</c>
/// transition consults this guard to ask whether the period for an entry's accounting date is open.
/// Production registers <c>GatewayPostingPeriodGuard</c>, which performs the real period-status lookup and
/// activates <c>POSTING_PERIOD_CLOSED</c> for closed/locked periods; <c>AlwaysOpenPostingPeriodGuard</c> is
/// a test-only fallback that always allows posting. The only difference between environments is the DI
/// registration of this guard.
/// </summary>
public interface IPostingPeriodGuard
{
    /// <summary>
    /// Determines whether a journal entry with the supplied accounting date may be posted into its
    /// fiscal period. Returns <see cref="Result.Success"/> when postable, or
    /// <see cref="Result.Failure"/> with <c>POSTING_PERIOD_CLOSED</c> when the period is closed/locked.
    /// </summary>
    /// <param name="entryDate">The accounting date whose period is being checked.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>A success result when postable; otherwise a <c>POSTING_PERIOD_CLOSED</c> failure.</returns>
    Task<Result> EnsurePostableAsync(DateTimeOffset entryDate, CancellationToken cancellationToken);
}
