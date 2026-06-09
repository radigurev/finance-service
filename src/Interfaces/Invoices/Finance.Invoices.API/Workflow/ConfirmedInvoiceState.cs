using Finance.Common.Enums;
using Finance.Common.Workflow;
using Finance.Invoices.DBModel.Models;

namespace Finance.Invoices.API.Workflow;

/// <summary>
/// The <c>Confirmed</c> workflow state of an <see cref="Invoice"/> (SDD-INV-001 §2.1). A confirmed invoice
/// may transition to <c>Posted</c> (via the posting handshake) or <c>Cancelled</c> (voided before posting).
/// State entry/exit run no side effects: posting/cancel side effects are owned by the calling service or
/// the back-event consumer inside the outbox transaction (SDD-INFRA-008 §2.2).
/// </summary>
public sealed class ConfirmedInvoiceState : IWorkflowState<Invoice>
{
    /// <inheritdoc />
    public string StateName => nameof(InvoiceStatus.Confirmed);

    /// <inheritdoc />
    public IReadOnlySet<string> AllowedNextStates { get; } =
        new HashSet<string>(StringComparer.Ordinal)
        {
            nameof(InvoiceStatus.Posted),
            nameof(InvoiceStatus.Cancelled)
        };

    /// <inheritdoc />
    public Task OnEnterAsync(WorkflowContext<Invoice> context, CancellationToken ct) => Task.CompletedTask;

    /// <inheritdoc />
    public Task OnExitAsync(WorkflowContext<Invoice> context, CancellationToken ct) => Task.CompletedTask;
}
