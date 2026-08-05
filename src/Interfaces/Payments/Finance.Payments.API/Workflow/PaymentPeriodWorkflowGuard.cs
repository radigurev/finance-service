using Finance.Common.Enums;
using Finance.Common.Results;
using Finance.Common.Validation;
using Finance.Common.Workflow;
using Finance.Payments.API.Interfaces;
using Finance.Payments.DBModel.Models;

namespace Finance.Payments.API.Workflow;

/// <summary>
/// Workflow guard that consults <see cref="IPaymentPeriodGuard"/> on the <c>Draft → Confirmed</c> transition
/// (SDD-PAY-001 §2.2, §2.9). A closed period, a date with no period, or an unreachable Periods service
/// short-circuits the transition with <c>PAYMENT_PERIOD_CLOSED</c> before any sequence value is consumed.
/// <para>The guard is INERT on every other transition — in particular on <c>→ Posted</c>, because by the time
/// the back-event arrives the Journal service has already asserted postability of the same date, and
/// re-checking would poison the consumer into a permanent retry loop while the GL already holds the entry
/// (SDD-PAY-001 §2.9).</para>
/// </summary>
public sealed class PaymentPeriodWorkflowGuard : IChainValidator<WorkflowContext<Payment>>
{
    private readonly IPaymentPeriodGuard _periodGuard;

    /// <summary>Creates a new <see cref="PaymentPeriodWorkflowGuard"/>.</summary>
    /// <param name="periodGuard">The fiscal-period guard seam (SDD-PAY-001 §2.9).</param>
    public PaymentPeriodWorkflowGuard(IPaymentPeriodGuard periodGuard)
    {
        _periodGuard = periodGuard;
    }

    /// <inheritdoc />
    public async Task<ChainValidationResult> ValidateAsync(
        WorkflowContext<Payment> request,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!string.Equals(request.TargetState, nameof(PaymentStatus.Confirmed), StringComparison.Ordinal))
        {
            return ChainValidationResult.Success();
        }

        Result periodResult =
            await _periodGuard.EnsureOpenAsync(request.Aggregate.PaymentDate, ct).ConfigureAwait(false);
        if (!periodResult.IsSuccess)
        {
            return ChainValidationResult.Failure(periodResult.ErrorCode!, periodResult.Detail);
        }

        return ChainValidationResult.Success();
    }
}
