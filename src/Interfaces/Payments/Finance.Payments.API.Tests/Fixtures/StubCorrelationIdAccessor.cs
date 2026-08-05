using Finance.Common.Abstractions;

namespace Finance.Payments.API.Tests.Fixtures;

/// <summary>
/// A settable <see cref="ICorrelationIdAccessor"/> used by the Payments unit tests so emitted events, audit
/// rows, and status-history rows carry a deterministic correlation id (SDD-PAY-001 §2.17, SDD-INFRA-001). The
/// value is mutable so a test can distinguish the payment's STORED correlation id from the AMBIENT request one —
/// the distinction the SDD-PAY-001 §2.5 confirm-event re-enqueue depends on.
/// </summary>
public sealed class StubCorrelationIdAccessor : ICorrelationIdAccessor
{
    /// <summary>The default deterministic correlation id.</summary>
    public const string DefaultCorrelationId = "test-correlation-id";

    /// <summary>The correlation id returned by <see cref="Get"/>.</summary>
    public string CorrelationId { get; set; } = DefaultCorrelationId;

    /// <inheritdoc />
    public string Get() => CorrelationId;
}
