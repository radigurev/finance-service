using Finance.Common.Enums;
using Finance.Common.Workflow;
using Finance.Invoices.DBModel.Models;

namespace Finance.Invoices.API.Workflow;

/// <summary>
/// The terminal <c>Reversed</c> workflow state of an <see cref="Invoice"/> (SDD-INV-001 §2.1, §2.7). A
/// posted invoice fully offset by a credit note is final: it has no allowed next states. Both the original
/// (with its <c>Reversed</c> flag) and the correcting note persist; nothing is overwritten.
/// </summary>
public sealed class ReversedInvoiceState : IWorkflowState<Invoice>
{
    /// <inheritdoc />
    public string StateName => nameof(InvoiceStatus.Reversed);

    /// <inheritdoc />
    public IReadOnlySet<string> AllowedNextStates { get; } = new HashSet<string>(StringComparer.Ordinal);

    /// <inheritdoc />
    public Task OnEnterAsync(WorkflowContext<Invoice> context, CancellationToken ct) => Task.CompletedTask;

    /// <inheritdoc />
    public Task OnExitAsync(WorkflowContext<Invoice> context, CancellationToken ct) => Task.CompletedTask;
}
