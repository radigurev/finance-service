using Finance.Common.Enums;
using Finance.Common.Workflow;
using Finance.Periods.DBModel.Models;

namespace Finance.Periods.API.Workflow;

/// <summary>
/// The <c>Closed</c> workflow state of a <see cref="FiscalPeriod"/> (SDD-FIN-004 §2.1). A closed period may
/// transition only back to <c>Open</c> (reopen). State entry/exit run no side effects: reopen side effects
/// (stamps, audit, outbox, history) are owned by the calling service inside the outbox transaction.
/// </summary>
public sealed class ClosedFiscalPeriodState : IWorkflowState<FiscalPeriod>
{
    /// <inheritdoc />
    public string StateName => nameof(FiscalPeriodStatus.Closed);

    /// <inheritdoc />
    public IReadOnlySet<string> AllowedNextStates { get; } =
        new HashSet<string>(StringComparer.Ordinal) { nameof(FiscalPeriodStatus.Open) };

    /// <inheritdoc />
    public Task OnEnterAsync(WorkflowContext<FiscalPeriod> context, CancellationToken ct) => Task.CompletedTask;

    /// <inheritdoc />
    public Task OnExitAsync(WorkflowContext<FiscalPeriod> context, CancellationToken ct) => Task.CompletedTask;
}
