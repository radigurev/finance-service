using Finance.Common.Enums;
using Finance.Common.Workflow;
using Finance.Invoices.DBModel.Models;

namespace Finance.Invoices.API.Workflow;

/// <summary>
/// The terminal <c>Cancelled</c> workflow state of an <see cref="Invoice"/> (SDD-INV-001 §2.1). A cancelled
/// invoice is final: it has no allowed next states. A cancelled confirmed invoice keeps (never recycles)
/// its document number.
/// </summary>
public sealed class CancelledInvoiceState : IWorkflowState<Invoice>
{
    /// <inheritdoc />
    public string StateName => nameof(InvoiceStatus.Cancelled);

    /// <inheritdoc />
    public IReadOnlySet<string> AllowedNextStates { get; } = new HashSet<string>(StringComparer.Ordinal);

    /// <inheritdoc />
    public Task OnEnterAsync(WorkflowContext<Invoice> context, CancellationToken ct) => Task.CompletedTask;

    /// <inheritdoc />
    public Task OnExitAsync(WorkflowContext<Invoice> context, CancellationToken ct) => Task.CompletedTask;
}
