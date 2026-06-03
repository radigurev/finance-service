using Finance.Common.Results;
using Finance.Journal.API.Interfaces;

namespace Finance.Journal.API.Services;

/// <summary>
/// Batch-10 default <see cref="IPostingPeriodGuard"/> that treats every period as open (SDD-FIN-002 §2.7).
/// It exists so posting works end-to-end while SDD-FIN-004 (Fiscal Period Management) is unbuilt. When
/// SDD-FIN-004 ships it replaces this registration with a real period-status lookup that returns
/// <c>POSTING_PERIOD_CLOSED</c> for closed/locked periods — no change to the posting code is required.
/// </summary>
public sealed class AlwaysOpenPostingPeriodGuard : IPostingPeriodGuard
{
    /// <inheritdoc />
    public Task<Result> EnsurePostableAsync(DateTimeOffset entryDate, CancellationToken cancellationToken)
    {
        return Task.FromResult(Result.Success());
    }
}
