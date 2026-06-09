using Finance.Common.Abstractions;

namespace Finance.Invoices.API.Tests.Fixtures;

/// <summary>
/// A fixed-value <see cref="ICorrelationIdAccessor"/> used by the Invoices unit tests so emitted events,
/// audit rows, and status-history rows carry a deterministic correlation id (SDD-INV-001 §2.12,
/// SDD-INFRA-001).
/// </summary>
public sealed class StubCorrelationIdAccessor : ICorrelationIdAccessor
{
    /// <summary>The deterministic correlation id returned by every call.</summary>
    public const string CorrelationId = "test-correlation-id";

    /// <inheritdoc />
    public string Get() => CorrelationId;
}
