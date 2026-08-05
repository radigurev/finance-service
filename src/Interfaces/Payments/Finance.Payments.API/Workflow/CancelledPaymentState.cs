using Finance.Common.Enums;
using Finance.Common.Workflow;
using Finance.Payments.DBModel.Models;

namespace Finance.Payments.API.Workflow;

/// <summary>
/// The terminal <c>Cancelled</c> workflow state of a <see cref="Payment"/> (SDD-PAY-001 §2.1). A cancelled
/// payment is final: it has no allowed next states. Cancel is legal from <c>Draft</c> ONLY, so a cancelled
/// payment never held a gapless document number — nothing is ever released, recycled, or reassigned.
/// </summary>
public sealed class CancelledPaymentState : IWorkflowState<Payment>
{
    /// <inheritdoc />
    public string StateName => nameof(PaymentStatus.Cancelled);

    /// <inheritdoc />
    public IReadOnlySet<string> AllowedNextStates { get; } = new HashSet<string>(StringComparer.Ordinal);

    /// <inheritdoc />
    public Task OnEnterAsync(WorkflowContext<Payment> context, CancellationToken ct) => Task.CompletedTask;

    /// <inheritdoc />
    public Task OnExitAsync(WorkflowContext<Payment> context, CancellationToken ct) => Task.CompletedTask;
}
