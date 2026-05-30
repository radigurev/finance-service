using Finance.Common.Abstractions;

namespace Finance.EventLog.API.Tests.Fixtures;

/// <summary>
/// A fixed-value <see cref="ICorrelationIdAccessor"/> used by the EventLog query-service unit tests so the
/// service runs without an HTTP context (SDD-EVTLOG-001 §6, SDD-INFRA-001).
/// </summary>
public sealed class StubCorrelationIdAccessor : ICorrelationIdAccessor
{
    /// <summary>The deterministic correlation id returned by every call.</summary>
    public const string CorrelationId = "test-correlation-id";

    /// <inheritdoc />
    public string Get() => CorrelationId;
}
