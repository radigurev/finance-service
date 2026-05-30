using Finance.Common.Abstractions;

namespace Finance.Accounts.API.Tests.Fixtures;

/// <summary>
/// A fixed-value <see cref="ICorrelationIdAccessor"/> used by the Accounts unit tests so emitted
/// events and audit rows carry a deterministic correlation id (SDD-ACCT-001 §2.11, SDD-INFRA-001).
/// </summary>
public sealed class StubCorrelationIdAccessor : ICorrelationIdAccessor
{
    /// <summary>The deterministic correlation id returned by every call.</summary>
    public const string CorrelationId = "test-correlation-id";

    /// <inheritdoc />
    public string Get() => CorrelationId;
}
