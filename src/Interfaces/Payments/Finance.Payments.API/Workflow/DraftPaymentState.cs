using Finance.Common.Enums;
using Finance.Common.Workflow;
using Finance.Payments.DBModel.Models;

namespace Finance.Payments.API.Workflow;

/// <summary>
/// The <c>Draft</c> workflow state of a <see cref="Payment"/> (SDD-PAY-001 §2.1). A draft may transition to
/// <c>Confirmed</c> or <c>Cancelled</c> (an update/delete is a removal/edit, not a transition). State
/// entry/exit run no side effects: confirm side effects (number, stamps, audit, outbox, history) are owned by
/// the calling service inside the outbox transaction (SDD-INFRA-008 §2.2).
/// </summary>
public sealed class DraftPaymentState : IWorkflowState<Payment>
{
    /// <inheritdoc />
    public string StateName => nameof(PaymentStatus.Draft);

    /// <inheritdoc />
    public IReadOnlySet<string> AllowedNextStates { get; } =
        new HashSet<string>(StringComparer.Ordinal)
        {
            nameof(PaymentStatus.Confirmed),
            nameof(PaymentStatus.Cancelled)
        };

    /// <inheritdoc />
    public Task OnEnterAsync(WorkflowContext<Payment> context, CancellationToken ct) => Task.CompletedTask;

    /// <inheritdoc />
    public Task OnExitAsync(WorkflowContext<Payment> context, CancellationToken ct) => Task.CompletedTask;
}
