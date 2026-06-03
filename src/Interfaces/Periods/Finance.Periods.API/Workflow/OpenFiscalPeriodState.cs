using Finance.Common.Enums;
using Finance.Common.Workflow;
using Finance.Periods.DBModel.Models;

namespace Finance.Periods.API.Workflow;

/// <summary>
/// The <c>Open</c> workflow state of a <see cref="FiscalPeriod"/> (SDD-FIN-004 §2.1). An open period may
/// transition only to <c>Closed</c>. State entry/exit run no side effects: close side effects (stamps,
/// audit, outbox, history) are owned by the calling service inside the outbox transaction (SDD-INFRA-008 §2.2).
/// </summary>
public sealed class OpenFiscalPeriodState : IWorkflowState<FiscalPeriod>
{
    /// <inheritdoc />
    public string StateName => nameof(FiscalPeriodStatus.Open);

    /// <inheritdoc />
    public IReadOnlySet<string> AllowedNextStates { get; } =
        new HashSet<string>(StringComparer.Ordinal) { nameof(FiscalPeriodStatus.Closed) };

    /// <inheritdoc />
    public Task OnEnterAsync(WorkflowContext<FiscalPeriod> context, CancellationToken ct) => Task.CompletedTask;

    /// <inheritdoc />
    public Task OnExitAsync(WorkflowContext<FiscalPeriod> context, CancellationToken ct) => Task.CompletedTask;
}
