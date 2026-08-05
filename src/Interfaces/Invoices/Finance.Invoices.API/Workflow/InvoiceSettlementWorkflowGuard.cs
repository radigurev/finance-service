using Finance.Common.Enums;
using Finance.Common.ErrorCodes;
using Finance.Common.Validation;
using Finance.Common.Workflow;
using Finance.Invoices.DBModel.Models;

namespace Finance.Invoices.API.Workflow;

/// <summary>
/// Workflow guard rejecting the cancellation of an invoice that already carries payment allocations
/// (SDD-INV-001 §2.6/§2.14): a <c>Draft</c>/<c>Confirmed</c> invoice whose <c>SettledAmount</c> is above
/// <c>0.00</c> fails with <c>INVOICE_HAS_SETTLEMENTS</c> BEFORE the transition, so no state changes, no audit
/// row is written, and no event is published. Voiding a document a counterparty has already paid against would
/// orphan allocation rows that SDD-PAY-002 releases only through an explicit deallocation, so the operator must
/// deallocate in the Payments service first.
/// <para>It is stateful, which is why it is a guard on the transition rather than a FluentValidation rule
/// (SDD-INFRA-007/-008), and it is inert on every transition other than <c>→ Cancelled</c> so the engine can
/// run it on each move safely.</para>
/// <para><b>BEST-EFFORT, not a hard invariant.</b> <c>SettledAmount</c> is an asynchronously-fed mirror of the
/// Payments-side allocation rows and the handshake is deliberately one-way with no synchronous cross-service
/// read, so a cancel that RACES an in-flight allocation still passes this guard and can orphan an allocation
/// row. SDD-PAY-002's cancellation consumer performs the authoritative detection. The guard exists to stop the
/// ORDINARY operator mistake, not to make the race impossible — closing it would require the cross-service read
/// the database-per-service boundary forbids.</para>
/// </summary>
public sealed class InvoiceSettlementWorkflowGuard : IChainValidator<WorkflowContext<Invoice>>
{
    /// <inheritdoc />
    public Task<ChainValidationResult> ValidateAsync(WorkflowContext<Invoice> request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!string.Equals(request.TargetState, nameof(InvoiceStatus.Cancelled), StringComparison.Ordinal))
        {
            return Task.FromResult(ChainValidationResult.Success());
        }

        if (request.Aggregate.SettledAmount <= 0m)
        {
            return Task.FromResult(ChainValidationResult.Success());
        }

        return Task.FromResult(ChainValidationResult.Failure(
            InvoiceErrorCodes.INVOICE_HAS_SETTLEMENTS,
            "The invoice already carries payment allocations; release them in the Payments service before "
            + "cancelling it."));
    }
}
