using Finance.Common.Enums;
using Finance.Common.Results;
using Finance.Infrastructure.Audit.Models;
using Finance.Payments.API.Consumers;
using Finance.Payments.API.Auditing;
using Finance.Payments.API.Tests.Builders;
using Finance.Payments.API.Tests.Fixtures;
using Finance.Payments.DBModel.Models;
using Finance.ServiceModel.Events.Payments;
using Finance.ServiceModel.Payments;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;

namespace Finance.Payments.API.Tests.Unit.Consumers;

/// <summary>
/// Unit tests for <see cref="PaymentPostedEventConsumer"/> — the Payments half of the SDD-PAY-001 §2.5 posting
/// handshake (§6.2). The consumer links the journal entry and moves <c>Confirmed → Posted</c>; aggregate-level
/// idempotency makes a replay against an already-<c>Posted</c> payment a success no-op; and any genuine failure is
/// RETHROWN so MassTransit retries and finally dead-letters rather than silently mutating the payment.
/// </summary>
[TestFixture]
[Category("SDD-PAY-001")]
public sealed class PaymentPostedEventConsumerTests
{
    private SqlitePaymentsDbContextScope _scope = null!;
    private PaymentServiceTestHarness _harness = null!;
    private PaymentPostedEventConsumer _sut = null!;

    /// <summary>Creates a fresh SQLite-backed harness and consumer before each test.</summary>
    [SetUp]
    public void SetUp()
    {
        _scope = SqlitePaymentsDbContextFactory.Create();
        _harness = PaymentServiceTestHarness.Build(_scope.Context);
        _sut = new PaymentPostedEventConsumer(
            _harness.Service,
            NullLogger<PaymentPostedEventConsumer>.Instance);
    }

    /// <summary>Disposes the SQLite scope after each test.</summary>
    [TearDown]
    public void TearDown()
    {
        _scope.Dispose();
    }

    [Test]
    public async Task PaymentPostedConsumer_LinksJournalEntryId_AndTransitionsConfirmedToPosted()
    {
        // Arrange
        Payment confirmed = await SeedConfirmedAsync();
        Guid journalEntryId = Guid.NewGuid();
        _harness.Timeline.Clear();

        // Act
        await _sut.Consume(ContextFor(BackEventFor(confirmed.Id, journalEntryId)));

        // Assert
        Payment stored = await LoadAsync(confirmed.Id);
        AuditEntry audit = _harness.RecordedAudits.Single();
        Assert.Multiple(() =>
        {
            Assert.That(stored.Status, Is.EqualTo(PaymentStatus.Posted));
            Assert.That(stored.JournalEntryId, Is.EqualTo(journalEntryId));
            Assert.That(stored.PostedAt, Is.EqualTo(FixedTimeProvider.DefaultNow));
            Assert.That(audit.EventType, Is.EqualTo(PaymentAuditEventTypes.PaymentPosted));
            Assert.That(
                _scope.Context.PaymentStatusHistory.Count(row =>
                    row.PaymentId == confirmed.Id && row.ToStatus == nameof(PaymentStatus.Posted)),
                Is.EqualTo(1));
        });
    }

    [Test]
    public async Task PaymentPostedConsumer_DuplicateEvent_IsNoOp_WhenAlreadyPosted()
    {
        // Arrange
        Payment confirmed = await SeedConfirmedAsync();
        Guid journalEntryId = Guid.NewGuid();
        await _sut.Consume(ContextFor(BackEventFor(confirmed.Id, journalEntryId)));
        _harness.Timeline.Clear();

        // Act
        await _sut.Consume(ContextFor(BackEventFor(confirmed.Id, Guid.NewGuid())));

        // Assert
        Payment stored = await LoadAsync(confirmed.Id);
        Assert.Multiple(() =>
        {
            Assert.That(stored.JournalEntryId, Is.EqualTo(journalEntryId), "never an overwritten link");
            Assert.That(_harness.RecordedAudits, Is.Empty, "never a second audit row");
            Assert.That(
                _scope.Context.PaymentStatusHistory.Count(row => row.ToStatus == nameof(PaymentStatus.Posted)),
                Is.EqualTo(1),
                "never a second Posted transition");
        });
    }

    [Test]
    public void PaymentPostedConsumer_UnknownPayment_ReturnsPaymentNotFound_AndThrows()
    {
        // Arrange
        PaymentPostedEvent message = BackEventFor(Guid.NewGuid(), Guid.NewGuid());

        // Act & Assert
        Assert.That(
            async () => await _sut.Consume(ContextFor(message)),
            Throws.InstanceOf<InvalidOperationException>()
                .With.Message.Contains("PAYMENT_NOT_FOUND"));
    }

    [Test]
    public async Task PaymentPostedConsumer_CancelledPayment_Throws_ForRetryAndDlq()
    {
        // Arrange
        Payment cancelled = await SeedAsync(PaymentBuilder.Create()
            .WithStatus(PaymentStatus.Cancelled)
            .WithDocumentNumber(null));

        // Act & Assert
        Assert.That(
            async () => await _sut.Consume(ContextFor(BackEventFor(cancelled.Id, Guid.NewGuid()))),
            Throws.InstanceOf<InvalidOperationException>()
                .With.Message.Contains("PAYMENT_NOT_CONFIRMED"));
        Payment stored = await LoadAsync(cancelled.Id);
        Assert.Multiple(() =>
        {
            Assert.That(stored.Status, Is.EqualTo(PaymentStatus.Cancelled), "never silently mutated to Posted");
            Assert.That(stored.JournalEntryId, Is.Null);
        });
    }

    [Test]
    public async Task PaymentPostedConsumer_ReversedPayment_Throws_ForRetryAndDlq()
    {
        // Arrange
        Guid originalJournalEntryId = Guid.NewGuid();
        Payment reversed = await SeedAsync(PaymentBuilder.Create()
            .WithStatus(PaymentStatus.Reversed)
            .WithJournalEntryId(originalJournalEntryId));

        // Act & Assert
        Assert.That(
            async () => await _sut.Consume(ContextFor(BackEventFor(reversed.Id, Guid.NewGuid()))),
            Throws.InstanceOf<InvalidOperationException>()
                .With.Message.Contains("PAYMENT_NOT_CONFIRMED"));
        Payment stored = await LoadAsync(reversed.Id);
        Assert.Multiple(() =>
        {
            Assert.That(stored.Status, Is.EqualTo(PaymentStatus.Reversed), "never mutated back to Posted");
            Assert.That(
                stored.JournalEntryId,
                Is.EqualTo(originalJournalEntryId),
                "the reversed entry's link is never overwritten by the late back-event");
            Assert.That(_harness.RecordedAudits, Is.Empty);
        });
    }

    [Test]
    public async Task PaymentPostedConsumer_DoesNotReRunPeriodGuard_WhenPeriodClosedAfterConfirm()
    {
        // Arrange
        Payment confirmed = await SeedConfirmedAsync();
        _harness.PeriodGuard.RequestedDates.Clear();
        _harness.PeriodGuard.IsOpen = false;

        // Act
        await _sut.Consume(ContextFor(BackEventFor(confirmed.Id, Guid.NewGuid())));

        // Assert
        Payment stored = await LoadAsync(confirmed.Id);
        Assert.Multiple(() =>
        {
            Assert.That(stored.Status, Is.EqualTo(PaymentStatus.Posted));
            Assert.That(
                _harness.PeriodGuard.RequestedDates,
                Is.Empty,
                "re-checking would poison the consumer into a permanent retry loop while the GL holds the entry");
        });
    }

    [Test]
    public async Task PaymentPostedConsumer_PropagatesCorrelationIdFromSourceEvent()
    {
        // Arrange
        _harness.Correlation.CorrelationId = "chain-correlation";
        PaymentDto draft = await CreateDraftAsync();
        Result<PaymentDto> confirmed = await _harness.Service.ConfirmAsync(
            draft.Id,
            new ConfirmPaymentRequest { RowVersion = draft.RowVersion },
            CancellationToken.None);
        Assert.That(confirmed.IsSuccess, Is.True, confirmed.ErrorCode);
        PaymentConfirmedEvent sourceChain = _harness.EventsOf<PaymentConfirmedEvent>().Single();
        _harness.Correlation.CorrelationId = "unrelated-consumer-scope";

        // Act
        await _sut.Consume(ContextFor(BackEventFor(draft.Id, Guid.NewGuid(), "chain-correlation")));

        // Assert
        Payment stored = await LoadAsync(draft.Id);
        Assert.Multiple(() =>
        {
            Assert.That(sourceChain.CorrelationId, Is.EqualTo("chain-correlation"));
            Assert.That(
                stored.CorrelationId,
                Is.EqualTo("chain-correlation"),
                "the aggregate keeps the ONE confirm→post chain correlation id");
            Assert.That(stored.Status, Is.EqualTo(PaymentStatus.Posted));
        });
    }

    /// <summary>Creates a valid draft payment through the production service path.</summary>
    /// <returns>The created payment DTO.</returns>
    private async Task<PaymentDto> CreateDraftAsync()
    {
        Result<PaymentDto> created = await _harness.Service.CreateDraftAsync(
            CreatePaymentRequestBuilder.Create().Build(),
            CancellationToken.None);

        Assert.That(created.IsSuccess, Is.True, created.ErrorCode);
        return created.Value!;
    }

    /// <summary>Seeds a confirmed, unlinked payment directly.</summary>
    /// <returns>The persisted payment.</returns>
    private Task<Payment> SeedConfirmedAsync() =>
        SeedAsync(PaymentBuilder.Create().WithStatus(PaymentStatus.Confirmed));

    /// <summary>Persists a directly-built payment.</summary>
    /// <param name="builder">The configured builder.</param>
    /// <returns>The persisted payment.</returns>
    private async Task<Payment> SeedAsync(PaymentBuilder builder)
    {
        Payment payment = builder.Build();
        _scope.Context.Payments.Add(payment);
        await _scope.Context.SaveChangesAsync(CancellationToken.None);
        return payment;
    }

    /// <summary>Reads the persisted payment without tracking.</summary>
    /// <param name="id">The payment identifier.</param>
    /// <returns>The persisted payment.</returns>
    private Task<Payment> LoadAsync(Guid id) => _scope.Context.Payments
        .AsNoTracking()
        .SingleAsync(payment => payment.Id == id, CancellationToken.None);

    /// <summary>Builds the Journal back-event for the supplied payment.</summary>
    /// <param name="paymentId">The source payment identifier.</param>
    /// <param name="journalEntryId">The posted journal entry identifier.</param>
    /// <param name="correlationId">The correlation identifier the Journal side propagated.</param>
    /// <returns>The back-event.</returns>
    private static PaymentPostedEvent BackEventFor(
        Guid paymentId,
        Guid journalEntryId,
        string correlationId = StubCorrelationIdAccessor.DefaultCorrelationId) => new()
    {
        MessageId = Guid.NewGuid(),
        CorrelationId = correlationId,
        OccurredAt = FixedTimeProvider.DefaultNow,
        PaymentId = paymentId,
        JournalEntryId = journalEntryId,
        JournalEntryNumber = "JE-2026-000001"
    };

    /// <summary>Wraps a message in a minimal Moq'd consume context.</summary>
    /// <typeparam name="TEvent">The event type.</typeparam>
    /// <param name="message">The message to deliver.</param>
    /// <returns>The consume context.</returns>
    private static ConsumeContext<TEvent> ContextFor<TEvent>(TEvent message)
        where TEvent : class
    {
        Mock<ConsumeContext<TEvent>> context = new();
        context.SetupGet(consume => consume.Message).Returns(message);
        context.SetupGet(consume => consume.CancellationToken).Returns(CancellationToken.None);
        return context.Object;
    }
}
