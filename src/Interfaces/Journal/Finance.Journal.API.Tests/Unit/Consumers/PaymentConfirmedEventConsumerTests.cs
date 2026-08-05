using Finance.Common.Enums;
using Finance.Common.ErrorCodes;
using Finance.Common.Results;
using Finance.Country.Abstractions;
using Finance.Journal.API.Consumers;
using Finance.Journal.API.Interfaces;
using Finance.ServiceModel.Events.Payments;
using Finance.ServiceModel.Journal;
using Finance.ServiceModel.Posting;
using MassTransit;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;

namespace Finance.Journal.API.Tests.Unit.Consumers;

/// <summary>
/// Unit tests for <see cref="PaymentConfirmedEventConsumer"/> — the Journal side of the payment posting
/// handshake (SDD-PAY-001 §2.5, SDD-FIN-006). The consumer is driven over a mocked
/// <see cref="IJournalEntryService"/> (supplying the source-document dedupe lookup), a mocked
/// <see cref="IPostingEngine"/>, and a Moq-captured <see cref="IPublishEndpoint"/> through a mocked
/// <see cref="ConsumeContext{T}"/> — the repository's established consumer-test pattern, with no MassTransit
/// test harness.
/// <para>The load-bearing assertion is the CONTROL-ACCOUNT TIE: the request handed to
/// <see cref="IPostingEngine.ApplyAsync"/> MUST carry the TRANSACTIONAL
/// <see cref="PaymentConfirmedEvent.Amount"/> in <see cref="PaymentConfirmedEvent.CurrencyCode"/> — never
/// <see cref="PaymentConfirmedEvent.BaseAmount"/> in <see cref="PaymentConfirmedEvent.BaseCurrencyCode"/> — so
/// the payment leg of a control account nets against the invoice leg the shipped
/// <see cref="InvoiceConfirmedEventConsumer"/> books (§2.5).</para>
/// </summary>
[TestFixture]
[Category("SDD-PAY-001")]
public sealed class PaymentConfirmedEventConsumerTests
{
    private const string CustomerReceiptRuleKey = "PAYMENT_CUSTOMER_RECEIPT";
    private const string SupplierPaymentRuleKey = "PAYMENT_SUPPLIER_PAYMENT";

    private static readonly DateTimeOffset PaymentDate = new(2026, 7, 15, 0, 0, 0, TimeSpan.Zero);

    private Mock<IJournalEntryService> _journalEntries = null!;
    private Mock<IPostingEngine> _postingEngine = null!;
    private Mock<IPublishEndpoint> _publishEndpoint = null!;
    private List<PaymentPostedEvent> _publishedEvents = null!;
    private PaymentConfirmedEventConsumer _consumer = null!;
    private ApplyPostingRuleRequest? _capturedRequest;

    /// <summary>Creates fresh mocks and a consumer before each test; no posted entry exists by default.</summary>
    [SetUp]
    public void SetUp()
    {
        _capturedRequest = null;
        _journalEntries = new Mock<IJournalEntryService>();
        _journalEntries
            .Setup(s => s.FindPostedBySourceDocumentAsync(
                It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((JournalEntryDto?)null);

        _postingEngine = new Mock<IPostingEngine>();
        _publishEndpoint = new Mock<IPublishEndpoint>();
        _publishedEvents = [];
        _publishEndpoint
            .Setup(p => p.Publish(It.IsAny<PaymentPostedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<PaymentPostedEvent, CancellationToken>((message, _) => _publishedEvents.Add(message))
            .Returns(Task.CompletedTask);

        _consumer = new PaymentConfirmedEventConsumer(
            _journalEntries.Object,
            _postingEngine.Object,
            _publishEndpoint.Object,
            NullLogger<PaymentConfirmedEventConsumer>.Instance);
    }

    /// <summary>
    /// A successful consume applies the payment's posting rule with the Gross amount context and the payment
    /// date, then publishes PaymentPostedEvent carrying the payment id, the resulting entry id and its number,
    /// with the source event's correlation id propagated (§2.5).
    /// </summary>
    [Test]
    public async Task PaymentConfirmedConsumer_AppliesPostingRule_AndPublishesPaymentPostedEvent()
    {
        // Arrange
        Guid paymentId = Guid.NewGuid();
        Guid journalEntryId = Guid.NewGuid();
        PaymentConfirmedEvent @event = BuildEvent(paymentId, amount: 250.00m, currencyCode: "BGN");
        CaptureApplyRequest(journalEntryId, "JE-2026-000011");

        // Act
        await _consumer.Consume(ContextFor(@event));

        // Assert
        Assert.That(_capturedRequest, Is.Not.Null);
        PaymentPostedEvent published = _publishedEvents.Single();
        Assert.Multiple(() =>
        {
            Assert.That(_capturedRequest!.RuleKey, Is.EqualTo(CustomerReceiptRuleKey));
            Assert.That(_capturedRequest.Amounts[PostingAmountSource.Gross], Is.EqualTo(250.00m));
            Assert.That(_capturedRequest.EntryDate, Is.EqualTo(PaymentDate));
            Assert.That(_capturedRequest.PostImmediately, Is.True);
            Assert.That(published.PaymentId, Is.EqualTo(paymentId));
            Assert.That(published.JournalEntryId, Is.EqualTo(journalEntryId));
            Assert.That(published.JournalEntryNumber, Is.EqualTo("JE-2026-000011"));
            Assert.That(published.CorrelationId, Is.EqualTo(@event.CorrelationId));
            Assert.That(published.MessageId, Is.Not.EqualTo(Guid.Empty));
        });
    }

    /// <summary>
    /// THE CONTROL-ACCOUNT TIE (§2.5): the posting request carries the TRANSACTIONAL amount in the payment's own
    /// currency, not the base amount in the base currency, so a EUR receipt books Cr 411 = 1000.00 against the
    /// EUR invoice's Dr 411 = 1000.00 instead of leaving a permanent BGN residual on the AR control account.
    /// </summary>
    [Test]
    public async Task PaymentConfirmedConsumer_SendsTransactionalAmountInPaymentCurrency_NotBaseAmount()
    {
        // Arrange
        PaymentConfirmedEvent @event = BuildEvent(
            Guid.NewGuid(),
            amount: 1000.00m,
            currencyCode: "EUR",
            exchangeRate: 1.955830m,
            baseAmount: 1955.83m);
        CaptureApplyRequest(Guid.NewGuid(), "JE-2026-000012");

        // Act
        await _consumer.Consume(ContextFor(@event));

        // Assert
        Assert.That(_capturedRequest, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(_capturedRequest!.CurrencyCode, Is.EqualTo("EUR"));
            Assert.That(_capturedRequest.CurrencyCode, Is.Not.EqualTo(@event.BaseCurrencyCode));
            Assert.That(_capturedRequest.Amounts[PostingAmountSource.Gross], Is.EqualTo(1000.00m));
            Assert.That(_capturedRequest.Amounts[PostingAmountSource.Gross], Is.Not.EqualTo(@event.BaseAmount));
        });
    }

    /// <summary>
    /// v1 uses the EXISTING Gross amount source only — the payment context never grows the
    /// <see cref="PostingAmountSource"/> enum with Net/Tax keys (§2.5).
    /// </summary>
    [Test]
    public async Task PaymentConfirmedConsumer_SuppliesGrossAmountSourceOnly()
    {
        // Arrange
        PaymentConfirmedEvent @event = BuildEvent(Guid.NewGuid(), amount: 75.50m, currencyCode: "BGN");
        CaptureApplyRequest(Guid.NewGuid(), "JE-2026-000013");

        // Act
        await _consumer.Consume(ContextFor(@event));

        // Assert
        Assert.That(_capturedRequest, Is.Not.Null);
        Assert.That(_capturedRequest!.Amounts.Keys, Is.EquivalentTo(new[] { PostingAmountSource.Gross }));
    }

    /// <summary>
    /// Every entry the consumer creates is stamped with the ("Payment", PaymentId) source-document pair, which
    /// reaches the entry through ApplyPostingRuleRequest → CreateJournalEntryRequest and is backstopped by the
    /// unique filtered index (§2.5).
    /// </summary>
    [Test]
    public async Task PaymentConfirmedConsumer_StampsSourceDocumentTypeAndId_OnThePostedEntry()
    {
        // Arrange
        Guid paymentId = Guid.NewGuid();
        PaymentConfirmedEvent @event = BuildEvent(paymentId, amount: 40.00m, currencyCode: "BGN");
        CaptureApplyRequest(Guid.NewGuid(), "JE-2026-000014");

        // Act
        await _consumer.Consume(ContextFor(@event));

        // Assert
        Assert.That(_capturedRequest, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(_capturedRequest!.SourceDocumentType, Is.EqualTo(JournalSourceDocumentTypes.Payment));
            Assert.That(_capturedRequest.SourceDocumentType, Is.EqualTo("Payment"));
            Assert.That(_capturedRequest.SourceDocumentId, Is.EqualTo(paymentId));
        });
    }

    /// <summary>
    /// The consumer consults the source-document dedupe lookup for ("Payment", PaymentId) BEFORE applying the
    /// rule, so the guard cannot be bypassed by a posting that has already happened (§2.5).
    /// </summary>
    [Test]
    public async Task PaymentConfirmedConsumer_ChecksSourceDocumentDedupeLookup_ForPaymentAndPaymentId()
    {
        // Arrange
        Guid paymentId = Guid.NewGuid();
        PaymentConfirmedEvent @event = BuildEvent(paymentId, amount: 10.00m, currencyCode: "BGN");
        CaptureApplyRequest(Guid.NewGuid(), "JE-2026-000015");

        // Act
        await _consumer.Consume(ContextFor(@event));

        // Assert
        _journalEntries.Verify(
            s => s.FindPostedBySourceDocumentAsync(
                JournalSourceDocumentTypes.Payment, paymentId, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// A redelivery past the 7-day Redis dedupe window (or a DLQ replay) whose entry is already Posted for
    /// ("Payment", PaymentId) posts NOTHING and succeeds — the aggregate-level duplicate-post guard that keeps
    /// cash 503 from being overstated (§2.5, §2.18).
    /// </summary>
    [Test]
    public async Task PaymentConfirmedConsumer_RedeliveryPastDedupeWindow_DoesNotPostSecondEntry()
    {
        // Arrange
        Guid paymentId = Guid.NewGuid();
        PaymentConfirmedEvent @event = BuildEvent(paymentId, amount: 250.00m, currencyCode: "BGN");
        _journalEntries
            .Setup(s => s.FindPostedBySourceDocumentAsync(
                JournalSourceDocumentTypes.Payment, paymentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildEntry(Guid.NewGuid(), "JE-2026-000021"));

        // Act
        await _consumer.Consume(ContextFor(@event));

        // Assert
        _postingEngine.Verify(
            e => e.ApplyAsync(It.IsAny<ApplyPostingRuleRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// The recovery half of the no-op: the already-posted entry's id and number are re-published on a fresh
    /// PaymentPostedEvent carrying the source event's correlation id, which is what unsticks a payment whose
    /// back-event was lost or dead-lettered (§2.5).
    /// </summary>
    [Test]
    public async Task PaymentConfirmedConsumer_PostedEntryExists_RepublishesPaymentPostedEvent_ForTheExistingEntry()
    {
        // Arrange
        Guid paymentId = Guid.NewGuid();
        Guid existingEntryId = Guid.NewGuid();
        PaymentConfirmedEvent @event = BuildEvent(paymentId, amount: 250.00m, currencyCode: "BGN");
        _journalEntries
            .Setup(s => s.FindPostedBySourceDocumentAsync(
                JournalSourceDocumentTypes.Payment, paymentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildEntry(existingEntryId, "JE-2026-000022"));

        // Act
        await _consumer.Consume(ContextFor(@event));

        // Assert
        PaymentPostedEvent published = _publishedEvents.Single();
        Assert.Multiple(() =>
        {
            Assert.That(published.PaymentId, Is.EqualTo(paymentId));
            Assert.That(published.JournalEntryId, Is.EqualTo(existingEntryId));
            Assert.That(published.JournalEntryNumber, Is.EqualTo("JE-2026-000022"));
            Assert.That(published.CorrelationId, Is.EqualTo(@event.CorrelationId));
            Assert.That(published.MessageId, Is.Not.EqualTo(@event.MessageId));
            Assert.That(published.MessageId, Is.Not.EqualTo(Guid.Empty));
        });
    }

    /// <summary>
    /// A posting failure is rethrown so MassTransit retries (1s/5s/15s) and finally dead-letters, and no
    /// back-event is published — no compensating event, the payment simply stays Confirmed (§2.5).
    /// </summary>
    [Test]
    public void PaymentConfirmedConsumer_PostingFailure_Throws_ForRetryAndDlq()
    {
        // Arrange
        PaymentConfirmedEvent @event = BuildEvent(Guid.NewGuid(), amount: 250.00m, currencyCode: "BGN");
        _postingEngine
            .Setup(e => e.ApplyAsync(It.IsAny<ApplyPostingRuleRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<JournalEntryDto>.Failure(PostingErrorCodes.POSTING_RULE_NOT_FOUND));

        // Act & Assert
        Assert.That(
            async () => await _consumer.Consume(ContextFor(@event)),
            Throws.TypeOf<InvalidOperationException>());
        Assert.That(_publishedEvents, Is.Empty);
    }

    /// <summary>
    /// When the event omits the posting-rule key the consumer resolves it from the document type, mirroring the
    /// invoice consumer's local fallback switch (§2.13).
    /// </summary>
    [TestCase(PaymentDocumentType.CustomerReceipt, CustomerReceiptRuleKey)]
    [TestCase(PaymentDocumentType.SupplierPayment, SupplierPaymentRuleKey)]
    public async Task PaymentConfirmedConsumer_ResolvesRuleKeyFromDocumentType_WhenKeyOmitted(
        PaymentDocumentType documentType,
        string expectedRuleKey)
    {
        // Arrange
        PaymentConfirmedEvent @event = BuildEvent(
            Guid.NewGuid(),
            amount: 60.00m,
            currencyCode: "BGN",
            documentType: documentType,
            postingRuleKey: string.Empty);
        CaptureApplyRequest(Guid.NewGuid(), "JE-2026-000031");

        // Act
        await _consumer.Consume(ContextFor(@event));

        // Assert
        Assert.That(_capturedRequest, Is.Not.Null);
        Assert.That(_capturedRequest!.RuleKey, Is.EqualTo(expectedRuleKey));
    }

    /// <summary>
    /// The entry description identifies the payment by its gapless document number so the GL is traceable back
    /// to the cash document (§2.5).
    /// </summary>
    [Test]
    public async Task PaymentConfirmedConsumer_DescribesEntry_WithThePaymentDocumentNumber()
    {
        // Arrange
        PaymentConfirmedEvent @event = BuildEvent(Guid.NewGuid(), amount: 12.00m, currencyCode: "BGN");
        CaptureApplyRequest(Guid.NewGuid(), "JE-2026-000032");

        // Act
        await _consumer.Consume(ContextFor(@event));

        // Assert
        Assert.That(_capturedRequest, Is.Not.Null);
        Assert.That(_capturedRequest!.Description, Is.EqualTo($"Payment {@event.DocumentNumber}"));
    }

    /// <summary>
    /// Arranges a successful <see cref="IPostingEngine.ApplyAsync"/> returning a posted entry and captures the
    /// request the consumer hands to it into <c>_capturedRequest</c>.
    /// </summary>
    /// <param name="journalEntryId">The identifier of the entry the engine reports as posted.</param>
    /// <param name="entryNumber">The gapless entry number the engine reports.</param>
    private void CaptureApplyRequest(Guid journalEntryId, string entryNumber)
    {
        _postingEngine
            .Setup(e => e.ApplyAsync(It.IsAny<ApplyPostingRuleRequest>(), It.IsAny<CancellationToken>()))
            .Callback<ApplyPostingRuleRequest, CancellationToken>((request, _) => _capturedRequest = request)
            .ReturnsAsync(Result<JournalEntryDto>.Success(BuildEntry(journalEntryId, entryNumber)));
    }

    private static PaymentConfirmedEvent BuildEvent(
        Guid paymentId,
        decimal amount,
        string currencyCode,
        decimal exchangeRate = 1.000000m,
        decimal? baseAmount = null,
        PaymentDocumentType documentType = PaymentDocumentType.CustomerReceipt,
        string? postingRuleKey = null)
    {
        return new PaymentConfirmedEvent
        {
            MessageId = Guid.NewGuid(),
            CorrelationId = "corr-pay-confirmed-1",
            OccurredAt = PaymentDate,
            PaymentId = paymentId,
            DocumentNumber = "RCT-2026-000001",
            DocumentType = documentType,
            Direction = documentType == PaymentDocumentType.SupplierPayment
                ? PaymentDirection.AP
                : PaymentDirection.AR,
            Method = PaymentMethod.BankTransfer,
            CounterpartyId = Guid.NewGuid(),
            SettlementAccountId = 503,
            CurrencyCode = currencyCode,
            BaseCurrencyCode = "BGN",
            Amount = amount,
            ExchangeRate = exchangeRate,
            BaseAmount = baseAmount ?? amount,
            PaymentDate = PaymentDate,
            PostingRuleKey = postingRuleKey ?? RuleKeyFor(documentType)
        };
    }

    private static string RuleKeyFor(PaymentDocumentType documentType) => documentType switch
    {
        PaymentDocumentType.SupplierPayment => SupplierPaymentRuleKey,
        _ => CustomerReceiptRuleKey
    };

    private static JournalEntryDto BuildEntry(Guid id, string entryNumber)
    {
        return new JournalEntryDto
        {
            Id = id,
            EntryNumber = entryNumber,
            EntryDate = PaymentDate,
            Description = "Payment RCT-2026-000001",
            BaseCurrencyCode = "BGN",
            Status = JournalEntryStatus.Posted,
            CreatedAt = PaymentDate,
            PostedAt = PaymentDate,
            Lines = [],
            RowVersion = Convert.ToBase64String(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 })
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
