using Finance.Common.Abstractions;

namespace Finance.Nomenclature.API.Tests.Fixtures;

/// <summary>
/// A fixed-value <see cref="ICorrelationIdAccessor"/> used by the Nomenclature unit tests so emitted
/// events and audit rows carry a deterministic correlation id (SDD-NOM-001 §2.1, SDD-INFRA-001).
/// </summary>
public sealed class StubCorrelationIdAccessor : ICorrelationIdAccessor
{
    /// <summary>The deterministic correlation id returned by every call.</summary>
    public const string CorrelationId = "test-correlation-id";

    /// <inheritdoc />
    public string Get() => CorrelationId;
}
