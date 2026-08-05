using Finance.ServiceModel.Events.Payments;
using MassTransit;
using Moq;

namespace Finance.Payments.API.Tests.Fixtures;

/// <summary>
/// Builds the recording <see cref="IPublishEndpoint"/> the Payments unit tests share. Every published payment
/// event is appended onto a shared, call-ordered timeline together with the audit entries, so the audit-first
/// ordering (SDD-AUDIT-001) and the one-event-per-allocation-row rule (SDD-PAY-002 §2.10) are assertable without a
/// broker.
/// </summary>
public static class PaymentTestPublishEndpoint
{
    /// <summary>Creates a mocked publish endpoint recording onto the supplied shared timeline.</summary>
    /// <param name="timeline">The shared, ordered list of audit entries and published events.</param>
    /// <returns>The configured mock.</returns>
    public static Mock<IPublishEndpoint> Create(List<object> timeline)
    {
        ArgumentNullException.ThrowIfNull(timeline);

        Mock<IPublishEndpoint> publishMock = new();
        Record<PaymentConfirmedEvent>(publishMock, timeline);
        Record<PaymentCancelledEvent>(publishMock, timeline);
        Record<PaymentReversedEvent>(publishMock, timeline);
        Record<PaymentAllocatedEvent>(publishMock, timeline);
        Record<PaymentDeallocatedEvent>(publishMock, timeline);
        return publishMock;
    }

    private static void Record<TEvent>(Mock<IPublishEndpoint> publishMock, List<object> timeline)
        where TEvent : class
    {
        publishMock
            .Setup(endpoint => endpoint.Publish(It.IsAny<TEvent>(), It.IsAny<CancellationToken>()))
            .Callback<TEvent, CancellationToken>((message, _) => timeline.Add(message))
            .Returns(Task.CompletedTask);
    }
}
