using Finance.Common.Validation;
using Finance.Common.Workflow;

namespace Finance.Infrastructure.Tests.Services.Fixtures.Workflow;

/// <summary>
/// A test transition guard that always rejects with a fixed error code, used to verify guard
/// short-circuiting in <see cref="Finance.Infrastructure.Services.Workflow.WorkflowEngine{TAggregate}"/>.
/// </summary>
public sealed class RejectingGuard : IChainValidator<WorkflowContext<SampleAggregate>>
{
    /// <summary>The error code returned by this guard.</summary>
    public const string GuardErrorCode = "PERIOD_CLOSED";

    /// <inheritdoc />
    public Task<ChainValidationResult> ValidateAsync(WorkflowContext<SampleAggregate> request, CancellationToken ct)
    {
        return Task.FromResult(ChainValidationResult.Failure(GuardErrorCode, "The fiscal period is closed."));
    }
}
