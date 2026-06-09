using Finance.Common.Enums;
using Finance.Common.Results;
using Finance.Common.Validation;
using Finance.Common.Workflow;
using Finance.Invoices.API.Interfaces;
using Finance.Invoices.DBModel.Models;

namespace Finance.Invoices.API.Workflow;

/// <summary>
/// Workflow guard that consults <see cref="IInvoicePeriodGuard"/> on the <c>Draft → Confirmed</c>
/// transition (SDD-INV-001 §2.2). With the default always-open guard this never fails; SDD-FIN-004 supplies
/// the real period-status lookup that rejects with <c>INVOICE_PERIOD_CLOSED</c>. The guard is inert on any
/// transition other than <c>→ Confirmed</c> so the engine can run it on every move safely.
/// </summary>
public sealed class InvoicePeriodWorkflowGuard : IChainValidator<WorkflowContext<Invoice>>
{
    private readonly IInvoicePeriodGuard _periodGuard;

    /// <summary>Creates a new <see cref="InvoicePeriodWorkflowGuard"/>.</summary>
    /// <param name="periodGuard">The deferred fiscal-period guard seam (SDD-INV-001 §2.2).</param>
    public InvoicePeriodWorkflowGuard(IInvoicePeriodGuard periodGuard)
    {
        _periodGuard = periodGuard;
    }

    /// <inheritdoc />
    public async Task<ChainValidationResult> ValidateAsync(
        WorkflowContext<Invoice> request,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!string.Equals(request.TargetState, nameof(InvoiceStatus.Confirmed), StringComparison.Ordinal))
        {
            return ChainValidationResult.Success();
        }

        Result periodResult =
            await _periodGuard.EnsureOpenAsync(request.Aggregate.IssueDate, ct).ConfigureAwait(false);
        if (!periodResult.IsSuccess)
        {
            return ChainValidationResult.Failure(periodResult.ErrorCode!, periodResult.Detail);
        }

        return ChainValidationResult.Success();
    }
}
