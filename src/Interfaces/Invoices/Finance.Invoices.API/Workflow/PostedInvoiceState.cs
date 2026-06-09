using Finance.Common.Enums;
using Finance.Common.Workflow;
using Finance.Invoices.DBModel.Models;

namespace Finance.Invoices.API.Workflow;

/// <summary>
/// The <c>Posted</c> workflow state of an <see cref="Invoice"/> (SDD-INV-001 §2.1, §2.7). A posted invoice
/// is immutable except for the single transition to <c>Reversed</c> when a fully-offsetting credit note is
/// posted. State entry/exit run no side effects (SDD-INFRA-008 §2.2).
/// </summary>
public sealed class PostedInvoiceState : IWorkflowState<Invoice>
{
    /// <inheritdoc />
    public string StateName => nameof(InvoiceStatus.Posted);

    /// <inheritdoc />
    public IReadOnlySet<string> AllowedNextStates { get; } =
        new HashSet<string>(StringComparer.Ordinal) { nameof(InvoiceStatus.Reversed) };

    /// <inheritdoc />
    public Task OnEnterAsync(WorkflowContext<Invoice> context, CancellationToken ct) => Task.CompletedTask;

    /// <inheritdoc />
    public Task OnExitAsync(WorkflowContext<Invoice> context, CancellationToken ct) => Task.CompletedTask;
}
