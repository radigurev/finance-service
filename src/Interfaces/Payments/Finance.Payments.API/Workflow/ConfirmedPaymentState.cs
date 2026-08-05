using Finance.Common.Enums;
using Finance.Common.Workflow;
using Finance.Payments.DBModel.Models;

namespace Finance.Payments.API.Workflow;

/// <summary>
/// The <c>Confirmed</c> workflow state of a <see cref="Payment"/> (SDD-PAY-001 §2.1). A confirmed payment may
/// transition ONLY to <c>Posted</c>, via the posting handshake.
/// <para><b><c>Cancelled</c> is DELIBERATELY absent and MUST NOT be added without a <c>CHG-ENH-*</c>.</b> By
/// the time a payment is <c>Confirmed</c>, <c>PaymentConfirmedEvent</c> is already in flight through the
/// outbox and the Journal-side consumer posts regardless of the payment's later state. A
/// <c>Confirmed → Cancelled</c> transition could therefore leave a posted entry in the general ledger with no
/// supporting document and no in-service correction path, because <c>Cancelled</c> is terminal and
/// <c>Reversed</c> is reachable only from <c>Posted</c>. A document whose entry may already be in the ledger is
/// corrected by REVERSAL, never by cancellation.</para>
/// <para>State entry/exit run no side effects: posting side effects are owned by the calling service or the
/// back-event consumer inside the outbox transaction (SDD-INFRA-008 §2.2).</para>
/// </summary>
public sealed class ConfirmedPaymentState : IWorkflowState<Payment>
{
    /// <inheritdoc />
    public string StateName => nameof(PaymentStatus.Confirmed);

    /// <inheritdoc />
    public IReadOnlySet<string> AllowedNextStates { get; } =
        new HashSet<string>(StringComparer.Ordinal) { nameof(PaymentStatus.Posted) };

    /// <inheritdoc />
    public Task OnEnterAsync(WorkflowContext<Payment> context, CancellationToken ct) => Task.CompletedTask;

    /// <inheritdoc />
    public Task OnExitAsync(WorkflowContext<Payment> context, CancellationToken ct) => Task.CompletedTask;
}
