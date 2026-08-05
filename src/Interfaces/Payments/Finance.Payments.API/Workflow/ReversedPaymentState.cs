using Finance.Common.Enums;
using Finance.Common.Workflow;
using Finance.Payments.DBModel.Models;

namespace Finance.Payments.API.Workflow;

/// <summary>
/// The terminal <c>Reversed</c> workflow state of a <see cref="Payment"/> (SDD-PAY-001 §2.1, §2.7). A posted
/// payment corrected by a sign-flipped journal entry is final: it has no allowed next states. Both the
/// original entry (with its <c>Reversed</c> flag) and the offsetting entry persist; nothing is overwritten and
/// the payment keeps its document number.
/// </summary>
public sealed class ReversedPaymentState : IWorkflowState<Payment>
{
    /// <inheritdoc />
    public string StateName => nameof(PaymentStatus.Reversed);

    /// <inheritdoc />
    public IReadOnlySet<string> AllowedNextStates { get; } = new HashSet<string>(StringComparer.Ordinal);

    /// <inheritdoc />
    public Task OnEnterAsync(WorkflowContext<Payment> context, CancellationToken ct) => Task.CompletedTask;

    /// <inheritdoc />
    public Task OnExitAsync(WorkflowContext<Payment> context, CancellationToken ct) => Task.CompletedTask;
}
