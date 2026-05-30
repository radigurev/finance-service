using Finance.ServiceModel.Events;

namespace Finance.Infrastructure.Stateful.Tests.Messaging.Fixtures;

/// <summary>
/// A representative concrete <see cref="IFinanceEvent"/> following the convention of SDD-INFRA-006
/// §2.2: a <c>sealed record</c> with <c>required</c> init-only <see cref="MessageId"/>,
/// <see cref="CorrelationId"/>, and <see cref="OccurredAt"/>. Used to verify the marker shape and to
/// drive the idempotency filter in unit tests.
/// </summary>
public sealed record SampleFinanceEvent : IFinanceEvent
{
    /// <inheritdoc />
    public required Guid MessageId { get; init; }

    /// <inheritdoc />
    public required string CorrelationId { get; init; }

    /// <inheritdoc />
    public required DateTimeOffset OccurredAt { get; init; }

    /// <summary>A representative domain payload field for a later-batch event.</summary>
    public required string EntryNumber { get; init; }
}
