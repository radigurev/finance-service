using Finance.Common.Enums;
using Finance.Common.ErrorCodes;
using Finance.Common.Validation;
using Finance.Common.Workflow;
using Finance.Periods.DBModel;
using Finance.Periods.DBModel.Models;
using Microsoft.EntityFrameworkCore;

namespace Finance.Periods.API.Workflow;

/// <summary>
/// Workflow guard enforcing the period-ordering invariant on close and reopen transitions
/// (SDD-FIN-004 §2.4, §2.5; SDD-INFRA-007/-008). A period MUST NOT be closed while an earlier <c>Open</c>
/// period exists in the same fiscal year, and MUST NOT be reopened while a later <c>Closed</c> period
/// exists in the same fiscal year. Both violations surface as <c>CANNOT_CLOSE_OUT_OF_ORDER</c>. The guard
/// is inert on any transition that is neither a close nor a reopen.
/// </summary>
public sealed class PeriodOrderingWorkflowGuard : IChainValidator<WorkflowContext<FiscalPeriod>>
{
    private readonly PeriodsDbContext _db;

    /// <summary>Creates a new <see cref="PeriodOrderingWorkflowGuard"/>.</summary>
    /// <param name="db">The periods database context used to inspect sibling periods.</param>
    public PeriodOrderingWorkflowGuard(PeriodsDbContext db)
    {
        _db = db;
    }

    /// <inheritdoc />
    public async Task<ChainValidationResult> ValidateAsync(
        WorkflowContext<FiscalPeriod> request,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        FiscalPeriod period = request.Aggregate;

        if (IsClose(request))
        {
            return await EnsureNoEarlierOpenAsync(period, ct).ConfigureAwait(false);
        }

        if (IsReopen(request))
        {
            return await EnsureNoLaterClosedAsync(period, ct).ConfigureAwait(false);
        }

        return ChainValidationResult.Success();
    }

    private static bool IsClose(WorkflowContext<FiscalPeriod> request) =>
        string.Equals(request.TargetState, nameof(FiscalPeriodStatus.Closed), StringComparison.Ordinal);

    private static bool IsReopen(WorkflowContext<FiscalPeriod> request) =>
        string.Equals(request.TargetState, nameof(FiscalPeriodStatus.Open), StringComparison.Ordinal);

    private async Task<ChainValidationResult> EnsureNoEarlierOpenAsync(FiscalPeriod period, CancellationToken ct)
    {
        bool earlierOpenExists = await _db.FiscalPeriods
            .AsNoTracking()
            .AnyAsync(
                candidate => candidate.FiscalYear == period.FiscalYear
                    && candidate.PeriodNumber < period.PeriodNumber
                    && candidate.Status == FiscalPeriodStatus.Open,
                ct)
            .ConfigureAwait(false);

        if (earlierOpenExists)
        {
            return ChainValidationResult.Failure(
                PeriodErrorCodes.CANNOT_CLOSE_OUT_OF_ORDER,
                "An earlier period in the same fiscal year is still open; close periods in order.");
        }

        return ChainValidationResult.Success();
    }

    private async Task<ChainValidationResult> EnsureNoLaterClosedAsync(FiscalPeriod period, CancellationToken ct)
    {
        bool laterClosedExists = await _db.FiscalPeriods
            .AsNoTracking()
            .AnyAsync(
                candidate => candidate.FiscalYear == period.FiscalYear
                    && candidate.PeriodNumber > period.PeriodNumber
                    && candidate.Status == FiscalPeriodStatus.Closed,
                ct)
            .ConfigureAwait(false);

        if (laterClosedExists)
        {
            return ChainValidationResult.Failure(
                PeriodErrorCodes.CANNOT_CLOSE_OUT_OF_ORDER,
                "A later period in the same fiscal year is still closed; reopen periods in order.");
        }

        return ChainValidationResult.Success();
    }
}
