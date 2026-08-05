using Finance.Common.Enums;
using Finance.Common.Workflow;
using Finance.Payments.DBModel.Models;

namespace Finance.Payments.API.Workflow;

/// <summary>
/// The <c>Posted</c> workflow state of a <see cref="Payment"/> (SDD-PAY-001 §2.1, §2.7). A posted payment is
/// immutable except for the single transition to <c>Reversed</c>, whose GL correction is a sign-flipped new
/// journal entry — never an UPDATE. State entry/exit run no side effects (SDD-INFRA-008 §2.2).
/// </summary>
public sealed class PostedPaymentState : IWorkflowState<Payment>
{
    /// <inheritdoc />
    public string StateName => nameof(PaymentStatus.Posted);

    /// <inheritdoc />
    public IReadOnlySet<string> AllowedNextStates { get; } =
        new HashSet<string>(StringComparer.Ordinal) { nameof(PaymentStatus.Reversed) };

    /// <inheritdoc />
    public Task OnEnterAsync(WorkflowContext<Payment> context, CancellationToken ct) => Task.CompletedTask;

    /// <inheritdoc />
    public Task OnExitAsync(WorkflowContext<Payment> context, CancellationToken ct) => Task.CompletedTask;
}
