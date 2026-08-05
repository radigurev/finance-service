using Finance.Common.Results;
using Finance.Payments.API.Interfaces;

namespace Finance.Payments.API.Tests.Fixtures;

/// <summary>
/// A recording <see cref="IRealizedFxHandler"/> for the Payments unit tests (SDD-PAY-002 §2.9). It captures every
/// <see cref="RealizedFxContext"/> handed to the dormant seam so a test can assert the seam is invoked ONCE PER
/// ALLOCATION ROW inside the transaction — the invocation is the contract, not the value — and it can be switched
/// to fail so the whole allocation is rejected.
/// </summary>
public sealed class RecordingRealizedFxHandler : IRealizedFxHandler
{
    /// <summary>The error code returned when <see cref="ShouldFail"/> is set.</summary>
    public const string FailureCode = "TEST_REALIZED_FX_FAILED";

    /// <summary>Every context handed to the seam, in call order.</summary>
    public List<RealizedFxContext> Invocations { get; } = [];

    /// <summary>When <c>true</c>, the seam returns a failure so the whole allocation must be rejected.</summary>
    public bool ShouldFail { get; set; }

    /// <inheritdoc />
    public Task<Result> HandleAsync(RealizedFxContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        Invocations.Add(context);

        return Task.FromResult(ShouldFail
            ? Result.Failure(FailureCode, "The realized-FX seam rejected the allocation.")
            : Result.Success());
    }
}
