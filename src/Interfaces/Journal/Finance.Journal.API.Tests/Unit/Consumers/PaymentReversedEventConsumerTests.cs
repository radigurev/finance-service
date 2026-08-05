using Finance.Common.Enums;
using Finance.Common.ErrorCodes;
using Finance.Common.Results;
using Finance.Journal.API.Consumers;
using Finance.Journal.API.Interfaces;
using Finance.ServiceModel.Events.Payments;
using Finance.ServiceModel.Journal;
using MassTransit;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;

namespace Finance.Journal.API.Tests.Unit.Consumers;

/// <summary>
/// Unit tests for <see cref="PaymentReversedEventConsumer"/> — the Journal side of the payment reversal
/// (SDD-PAY-001 §2.7, SDD-FIN-002 §2.6). The consumer is driven over a mocked
/// <see cref="IJournalEntryService"/> through a mocked <see cref="ConsumeContext{T}"/>.
/// <para>Two rules are load-bearing. First, the entry MUST be READ before it is reversed: the shipped
/// <c>ReverseJournalEntryRequest.RowVersion</c> is a required base64 concurrency token the event does not carry,
/// and a null or blank token fails with <c>CONCURRENT_MODIFICATION</c>. Second, a linked entry already in
/// <c>Reversed</c> MUST be a success NO-OP and MUST NEVER reach <c>ReverseAsync</c>, which rejects any
/// non-<c>Posted</c> entry — otherwise a redelivery past the 7-day dedupe window or a DLQ replay would
/// permanently self-renew the dead letter.</para>
/// </summary>
[TestFixture]
[Category("SDD-PAY-001")]
public sealed class PaymentReversedEventConsumerTests
{
    private static readonly DateTimeOffset EntryDate = new(2026, 7, 15, 0, 0, 0, TimeSpan.Zero);
    private static readonly byte[] RowVersionBytes = [9, 8, 7, 6, 5, 4, 3, 2];

    private Mock<IJournalEntryService> _journalEntries = null!;
    private PaymentReversedEventConsumer _consumer = null!;
    private ReverseJournalEntryRequest? _capturedRequest;

    /// <summary>Creates a fresh mock and consumer before each test.</summary>
    [SetUp]
    public void SetUp()
    {
        _capturedRequest = null;
        _journalEntries = new Mock<IJournalEntryService>();
        _consumer = new PaymentReversedEventConsumer(
            _journalEntries.Object,
            NullLogger<PaymentReversedEventConsumer>.Instance);
    }

    /// <summary>
    /// The consumer LOADS the linked entry and forwards the base64 RowVersion the DTO exposes, together with the
    /// event's reason, so the shipped concurrency check accepts the reversal (§2.7).
    /// </summary>
    [Test]
    public async Task PaymentReversedConsumer_LoadsEntryAndPassesItsBase64RowVersion_ToReverseAsync()
    {
        // Arrange
        Guid journalEntryId = Guid.NewGuid();
        PaymentReversedEvent @event = BuildEvent(journalEntryId, "Duplicate cash receipt");
        ArrangeLoad(journalEntryId, JournalEntryStatus.Posted);
        ArrangeReverseSuccess(journalEntryId);

        // Act
        await _consumer.Consume(ContextFor(@event));

        // Assert
        Assert.That(_capturedRequest, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(_capturedRequest!.RowVersion, Is.EqualTo(Convert.ToBase64String(RowVersionBytes)));
            Assert.That(_capturedRequest.RowVersion, Is.Not.Null.And.Not.Empty);
            Assert.That(_capturedRequest.Reason, Is.EqualTo("Duplicate cash receipt"));
        });
        _journalEntries.Verify(
            s => s.GetAsync(journalEntryId, It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// The reversal is delegated to the shipped IJournalEntryService.ReverseAsync path for the linked entry id —
    /// the consumer reimplements no reversal arithmetic and never UPDATEs the original (§2.7).
    /// </summary>
    [Test]
    public async Task PaymentReversedConsumer_PostedLinkedEntry_DelegatesReversalToJournalEntryService()
    {
        // Arrange
        Guid journalEntryId = Guid.NewGuid();
        PaymentReversedEvent @event = BuildEvent(journalEntryId, "Wrong counterparty");
        ArrangeLoad(journalEntryId, JournalEntryStatus.Posted);
        ArrangeReverseSuccess(journalEntryId);

        // Act
        await _consumer.Consume(ContextFor(@event));

        // Assert
        _journalEntries.Verify(
            s => s.ReverseAsync(
                journalEntryId,
                It.IsAny<ReverseJournalEntryRequest>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// Aggregate-level idempotency (§2.7, §2.18): a linked entry already in Reversed is a success NO-OP and is
    /// never passed to ReverseAsync, so a redelivery past the dedupe window cannot self-renew a dead letter.
    /// </summary>
    [Test]
    public async Task PaymentReversedConsumer_LinkedEntryAlreadyReversed_IsSuccessNoOp_DoesNotCallReverseAsync()
    {
        // Arrange
        Guid journalEntryId = Guid.NewGuid();
        PaymentReversedEvent @event = BuildEvent(journalEntryId, "Replayed reversal");
        ArrangeLoad(journalEntryId, JournalEntryStatus.Reversed);

        // Act
        await _consumer.Consume(ContextFor(@event));

        // Assert
        _journalEntries.Verify(
            s => s.ReverseAsync(
                It.IsAny<Guid>(),
                It.IsAny<ReverseJournalEntryRequest>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// An entry that cannot be loaded (an unknown linked id) is rethrown so MassTransit retries and finally
    /// dead-letters — never swallowed (§2.7).
    /// </summary>
    [Test]
    public void PaymentReversedConsumer_LinkedEntryNotFound_Throws_ForRetryAndDlq()
    {
        // Arrange
        Guid journalEntryId = Guid.NewGuid();
        PaymentReversedEvent @event = BuildEvent(journalEntryId, "Unknown entry");
        _journalEntries
            .Setup(s => s.GetAsync(journalEntryId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<JournalEntryDto>.Failure(JournalErrorCodes.JOURNAL_ENTRY_NOT_FOUND));

        // Act & Assert
        Assert.That(
            async () => await _consumer.Consume(ContextFor(@event)),
            Throws.TypeOf<InvalidOperationException>());
    }

    /// <summary>
    /// A failed reversal is rethrown so MassTransit retries and finally dead-letters, rather than reporting a
    /// silent success for a GL correction that never landed (§2.7).
    /// </summary>
    [Test]
    public void PaymentReversedConsumer_ReversalFailure_Throws_ForRetryAndDlq()
    {
        // Arrange
        Guid journalEntryId = Guid.NewGuid();
        PaymentReversedEvent @event = BuildEvent(journalEntryId, "Stale token");
        ArrangeLoad(journalEntryId, JournalEntryStatus.Posted);
        _journalEntries
            .Setup(s => s.ReverseAsync(
                journalEntryId,
                It.IsAny<ReverseJournalEntryRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<JournalEntryDto>.Failure(CommonErrorCodes.CONCURRENT_MODIFICATION));

        // Act & Assert
        Assert.That(
            async () => await _consumer.Consume(ContextFor(@event)),
            Throws.TypeOf<InvalidOperationException>());
    }

    /// <summary>
    /// Arranges the linked-entry load to return an entry in the supplied status carrying the fixed base64
    /// RowVersion the consumer must forward.
    /// </summary>
    /// <param name="journalEntryId">The linked entry identifier.</param>
    /// <param name="status">The status the loaded entry reports.</param>
    private void ArrangeLoad(Guid journalEntryId, JournalEntryStatus status)
    {
        _journalEntries
            .Setup(s => s.GetAsync(journalEntryId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<JournalEntryDto>.Success(BuildEntry(journalEntryId, status)));
    }

    /// <summary>Arranges a successful reversal and captures the request the consumer built.</summary>
    /// <param name="journalEntryId">The linked entry identifier expected on the reversal call.</param>
    private void ArrangeReverseSuccess(Guid journalEntryId)
    {
        _journalEntries
            .Setup(s => s.ReverseAsync(
                journalEntryId,
                It.IsAny<ReverseJournalEntryRequest>(),
                It.IsAny<CancellationToken>()))
            .Callback<Guid, ReverseJournalEntryRequest, CancellationToken>(
                (_, request, _) => _capturedRequest = request)
            .ReturnsAsync(Result<JournalEntryDto>.Success(
                BuildEntry(Guid.NewGuid(), JournalEntryStatus.Posted)));
    }

    private static PaymentReversedEvent BuildEvent(Guid journalEntryId, string reason)
    {
        return new PaymentReversedEvent
        {
            MessageId = Guid.NewGuid(),
            CorrelationId = "corr-pay-reversed-1",
            OccurredAt = EntryDate,
            PaymentId = Guid.NewGuid(),
            DocumentNumber = "RCT-2026-000001",
            JournalEntryId = journalEntryId,
            Reason = reason
        };
    }

    private static JournalEntryDto BuildEntry(Guid id, JournalEntryStatus status)
    {
        return new JournalEntryDto
        {
            Id = id,
            EntryNumber = "JE-2026-000041",
            EntryDate = EntryDate,
            Description = "Payment RCT-2026-000001",
            BaseCurrencyCode = "BGN",
            Status = status,
            CreatedAt = EntryDate,
            PostedAt = EntryDate,
            Lines = [],
            RowVersion = Convert.ToBase64String(RowVersionBytes)
        };
    }

    private static ConsumeContext<TEvent> ContextFor<TEvent>(TEvent message)
        where TEvent : class
    {
        Mock<ConsumeContext<TEvent>> context = new();
        context.SetupGet(c => c.Message).Returns(message);
        context.SetupGet(c => c.CancellationToken).Returns(CancellationToken.None);
        return context.Object;
    }
}
