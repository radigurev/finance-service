using Finance.Common.Enums;
using Finance.Common.Results;
using Finance.Common.Validation;
using Finance.Common.Workflow;
using Finance.Journal.API.Interfaces;
using Finance.Journal.DBModel.Models;

namespace Finance.Journal.API.Workflow;

/// <summary>
/// Workflow guard that consults <see cref="IPostingPeriodGuard"/> on the <c>Draft → Posted</c> transition
/// (SDD-FIN-002 §2.2, §2.7). With the Batch-10 always-open default this never fails; SDD-FIN-004 supplies
/// the real period-status lookup that rejects with <c>POSTING_PERIOD_CLOSED</c>. The guard is inert on any
/// transition other than <c>→ Posted</c> so the engine can run it on every move safely.
/// </summary>
public sealed class PostingPeriodWorkflowGuard : IChainValidator<WorkflowContext<JournalEntry>>
{
    private readonly IPostingPeriodGuard _periodGuard;

    /// <summary>Creates a new <see cref="PostingPeriodWorkflowGuard"/>.</summary>
    /// <param name="periodGuard">The deferred fiscal-period guard seam (SDD-FIN-002 §2.7).</param>
    public PostingPeriodWorkflowGuard(IPostingPeriodGuard periodGuard)
    {
        _periodGuard = periodGuard;
    }

    /// <inheritdoc />
    public async Task<ChainValidationResult> ValidateAsync(
        WorkflowContext<JournalEntry> request,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!string.Equals(request.TargetState, nameof(JournalEntryStatus.Posted), StringComparison.Ordinal))
        {
            return ChainValidationResult.Success();
        }

        Result periodResult =
            await _periodGuard.EnsurePostableAsync(request.Aggregate.EntryDate, ct).ConfigureAwait(false);
        if (!periodResult.IsSuccess)
        {
            return ChainValidationResult.Failure(periodResult.ErrorCode!, periodResult.Detail);
        }

        return ChainValidationResult.Success();
    }
}
