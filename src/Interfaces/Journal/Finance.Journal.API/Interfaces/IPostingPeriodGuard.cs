using Finance.Common.Results;

namespace Finance.Journal.API.Interfaces;

/// <summary>
/// Extension seam for the deferred fiscal-period lock (SDD-FIN-002 §2.7; dependency on SDD-FIN-004).
/// The <c>Draft → Posted</c> transition consults this guard to ask whether the period for an entry's
/// accounting date is open. Batch 10 ships <c>AlwaysOpenPostingPeriodGuard</c> as the default so posting
/// works end-to-end; SDD-FIN-004 supplies the real period-status lookup that activates
/// <c>POSTING_PERIOD_CLOSED</c> — the only change required is the DI registration of this guard.
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
