using Finance.Common.Enums;
using Finance.Common.Workflow;
using Finance.Invoices.DBModel.Models;

namespace Finance.Invoices.API.Workflow;

/// <summary>
/// The <c>Draft</c> workflow state of an <see cref="Invoice"/> (SDD-INV-001 §2.1). A draft may transition
/// to <c>Confirmed</c> or <c>Cancelled</c> (an update/delete is a removal/edit, not a transition). State
/// entry/exit run no side effects: confirm side effects (number, stamps, audit, outbox, history) are owned
/// by the calling service inside the outbox transaction (SDD-INFRA-008 §2.2).
/// </summary>
public sealed class DraftInvoiceState : IWorkflowState<Invoice>
{
    /// <inheritdoc />
    public string StateName => nameof(InvoiceStatus.Draft);

    /// <inheritdoc />
    public IReadOnlySet<string> AllowedNextStates { get; } =
        new HashSet<string>(StringComparer.Ordinal)
        {
            nameof(InvoiceStatus.Confirmed),
            nameof(InvoiceStatus.Cancelled)
        };

    /// <inheritdoc />
    public Task OnEnterAsync(WorkflowContext<Invoice> context, CancellationToken ct) => Task.CompletedTask;

    /// <inheritdoc />
    public Task OnExitAsync(WorkflowContext<Invoice> context, CancellationToken ct) => Task.CompletedTask;
}
