using Finance.Common.Abstractions;

namespace Finance.Journal.API.Tests.Fixtures;

/// <summary>
/// A fixed-value <see cref="ICorrelationIdAccessor"/> used by the Journal unit tests so emitted events,
/// audit rows, and status-history rows carry a deterministic correlation id (SDD-FIN-002 §2.10,
/// SDD-INFRA-001).
/// </summary>
public sealed class StubCorrelationIdAccessor : ICorrelationIdAccessor
{
    /// <summary>The deterministic correlation id returned by every call.</summary>
    public const string CorrelationId = "test-correlation-id";

    /// <inheritdoc />
    public string Get() => CorrelationId;
}
