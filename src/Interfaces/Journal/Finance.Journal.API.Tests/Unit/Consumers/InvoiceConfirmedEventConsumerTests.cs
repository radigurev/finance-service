using Finance.Common.Enums;
using Finance.Common.ErrorCodes;
using Finance.Common.Results;
using Finance.Country.Abstractions;
using Finance.Journal.API.Consumers;
using Finance.Journal.API.Interfaces;
using Finance.ServiceModel.Events.Invoices;
using Finance.ServiceModel.Journal;
using Finance.ServiceModel.Posting;
using MassTransit;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;

namespace Finance.Journal.API.Tests.Unit.Consumers;

/// <summary>
/// Unit tests for <see cref="InvoiceConfirmedEventConsumer"/> — the Journal-side handshake that posts a
/// journal entry for a confirmed invoice and publishes the dedicated <see cref="InvoicePostedEvent"/>
/// back-event (SDD-INV-001 §2.5, SDD-FIN-006). They drive the consumer over a mocked
/// <see cref="IPostingEngine"/> and a Moq-captured <see cref="IPublishEndpoint"/> through a mocked
/// <see cref="ConsumeContext{T}"/> (matching the repository's existing consumer-test pattern — no
/// MassTransit.TestFramework), asserting the posting request carries the mapped rule key, the net/tax/gross
/// amounts, the currency and entry date; that the published back-event carries the source invoice id, the
/// returned journal-entry id and number; and that a posting failure propagates so MassTransit retries.
/// </summary>
[TestFixture]
[Category("SDD-INV-001")]
public sealed class InvoiceConfirmedEventConsumerTests
{
    private static readonly DateTimeOffset IssueDate = new(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);

    private Mock<IJournalEntryService> _journalEntries = null!;
    private Mock<IPostingEngine> _postingEngine = null!;
    private Mock<IPublishEndpoint> _publishEndpoint = null!;
    private List<object> _publishedEvents = null!;
    private InvoiceConfirmedEventConsumer _consumer = null!;

    /// <summary>Creates fresh mocks and a consumer before each test.</summary>
    [SetUp]
    public void SetUp()
    {
        _journalEntries = new Mock<IJournalEntryService>();
        _journalEntries
            .Setup(s => s.FindPostedBySourceDocumentAsync(
                It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((JournalEntryDto?)null);

        _postingEngine = new Mock<IPostingEngine>();
        _publishEndpoint = new Mock<IPublishEndpoint>();
        _publishedEvents = [];
        _publishEndpoint
            .Setup(p => p.Publish(It.IsAny<InvoicePostedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<InvoicePostedEvent, CancellationToken>((message, _) => _publishedEvents.Add(message))
            .Returns(Task.CompletedTask);

        _consumer = new InvoiceConfirmedEventConsumer(
            _journalEntries.Object,
            _postingEngine.Object,
            _publishEndpoint.Object,
            NullLogger<InvoiceConfirmedEventConsumer>.Instance);
    }

    /// <summary>
    /// On a successful posting, the consumer applies the rule with the mapped rule key, the net/tax/gross
    /// amounts, the currency and entry date, then publishes an InvoicePostedEvent carrying the source invoice
    /// id and the returned journal-entry id and number (§2.5).
    /// </summary>
    [Test]
    public async Task InvoiceConfirmedConsumer_PostsJournalEntry_AndPublishesInvoicePostedEvent()
    {
        // Arrange
        Guid invoiceId = Guid.NewGuid();
        Guid journalEntryId = Guid.NewGuid();
        InvoiceConfirmedEvent @event = BuildEvent(
            invoiceId,
            postingRuleKey: "SALE_INVOICE",
            net: 100.00m,
            tax: 20.00m,
            gross: 120.00m,
            currencyCode: "BGN");
        ApplyPostingRuleRequest? captured = null;
        _postingEngine
            .Setup(e => e.ApplyAsync(It.IsAny<ApplyPostingRuleRequest>(), It.IsAny<CancellationToken>()))
            .Callback<ApplyPostingRuleRequest, CancellationToken>((request, _) => captured = request)
            .ReturnsAsync(Result<JournalEntryDto>.Success(
                BuildEntry(journalEntryId, "JE-2026-000007")));

        // Act
        await _consumer.Consume(ContextFor(@event));

        // Assert
        Assert.That(captured, Is.Not.Null);
        InvoicePostedEvent published = _publishedEvents.OfType<InvoicePostedEvent>().Single();
        Assert.Multiple(() =>
        {
            Assert.That(captured!.RuleKey, Is.EqualTo("SALE_INVOICE"));
            Assert.That(captured.Amounts[PostingAmountSource.Net], Is.EqualTo(100.00m));
            Assert.That(captured.Amounts[PostingAmountSource.Tax], Is.EqualTo(20.00m));
            Assert.That(captured.Amounts[PostingAmountSource.Gross], Is.EqualTo(120.00m));
            Assert.That(captured.CurrencyCode, Is.EqualTo("BGN"));
            Assert.That(captured.EntryDate, Is.EqualTo(IssueDate));
            Assert.That(published.InvoiceId, Is.EqualTo(invoiceId));
            Assert.That(published.JournalEntryId, Is.EqualTo(journalEntryId));
            Assert.That(published.JournalEntryNumber, Is.EqualTo("JE-2026-000007"));
            Assert.That(published.CorrelationId, Is.EqualTo(@event.CorrelationId));
        });
    }

    /// <summary>
    /// When the event carries no explicit posting-rule key, the consumer resolves it from the document type
    /// (PurchaseInvoice → PURCHASE_INVOICE) before applying the rule (§2.5).
    /// </summary>
    [Test]
    public async Task InvoiceConfirmedConsumer_ResolvesRuleKeyFromDocumentType_WhenKeyOmitted()
    {
        // Arrange
        InvoiceConfirmedEvent @event = BuildEvent(
            Guid.NewGuid(),
            postingRuleKey: string.Empty,
            net: 50.00m,
            tax: 10.00m,
            gross: 60.00m,
            currencyCode: "BGN",
            documentType: InvoiceDocumentType.PurchaseInvoice);
        ApplyPostingRuleRequest? captured = null;
        _postingEngine
            .Setup(e => e.ApplyAsync(It.IsAny<ApplyPostingRuleRequest>(), It.IsAny<CancellationToken>()))
            .Callback<ApplyPostingRuleRequest, CancellationToken>((request, _) => captured = request)
            .ReturnsAsync(Result<JournalEntryDto>.Success(BuildEntry(Guid.NewGuid(), "JE-2026-000008")));

        // Act
        await _consumer.Consume(ContextFor(@event));

        // Assert
        Assert.That(captured, Is.Not.Null);
        Assert.That(captured!.RuleKey, Is.EqualTo("PURCHASE_INVOICE"));
    }

    /// <summary>
    /// A posting failure propagates as an exception so MassTransit retries / dead-letters the message, and no
    /// back-event is published (§2.5).
    /// </summary>
    [Test]
    public void InvoiceConfirmedConsumer_WhenPostingFails_Throws_ForRetry()
    {
        // Arrange
        InvoiceConfirmedEvent @event = BuildEvent(
            Guid.NewGuid(),
            postingRuleKey: "SALE_INVOICE",
            net: 100.00m,
            tax: 20.00m,
            gross: 120.00m,
            currencyCode: "BGN");
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
    /// Every entry the shipped consumer creates is stamped with the ("Invoice", InvoiceId) source-document pair,
    /// which reaches the entry through ApplyPostingRuleRequest → CreateJournalEntryRequest and is backstopped by
    /// the unique filtered index (SDD-PAY-001 §2.5 hardening, SDD-INV-001 §2.5).
    /// </summary>
    [Test]
    [Category("SDD-PAY-001")]
    public async Task InvoiceConfirmedConsumer_StampsSourceDocumentTypeAndId_OnPostedJournalEntry()
    {
        // Arrange
        Guid invoiceId = Guid.NewGuid();
        InvoiceConfirmedEvent @event = BuildEvent(
            invoiceId,
            postingRuleKey: "SALE_INVOICE",
            net: 100.00m,
            tax: 20.00m,
            gross: 120.00m,
            currencyCode: "BGN");
        ApplyPostingRuleRequest? captured = null;
        _postingEngine
            .Setup(e => e.ApplyAsync(It.IsAny<ApplyPostingRuleRequest>(), It.IsAny<CancellationToken>()))
            .Callback<ApplyPostingRuleRequest, CancellationToken>((request, _) => captured = request)
            .ReturnsAsync(Result<JournalEntryDto>.Success(BuildEntry(Guid.NewGuid(), "JE-2026-000051")));

        // Act
        await _consumer.Consume(ContextFor(@event));

        // Assert
        Assert.That(captured, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(captured!.SourceDocumentType, Is.EqualTo(JournalSourceDocumentTypes.Invoice));
            Assert.That(captured.SourceDocumentType, Is.EqualTo("Invoice"));
            Assert.That(captured.SourceDocumentId, Is.EqualTo(invoiceId));
        });
    }

    /// <summary>
    /// A redelivery past the 7-day Redis dedupe window (or a DLQ replay) whose entry is already Posted for
    /// ("Invoice", InvoiceId) posts NOTHING and succeeds, so the AR/AP control account can never be silently
    /// overstated by the invoice gross (SDD-PAY-001 §2.5 hardening, SDD-INV-001 §2.5).
    /// </summary>
    [Test]
    [Category("SDD-PAY-001")]
    public async Task InvoiceConfirmedConsumer_RedeliveryPastDedupeWindow_DoesNotPostSecondEntry()
    {
        // Arrange
        Guid invoiceId = Guid.NewGuid();
        InvoiceConfirmedEvent @event = BuildEvent(
            invoiceId,
            postingRuleKey: "SALE_INVOICE",
            net: 100.00m,
            tax: 20.00m,
            gross: 120.00m,
            currencyCode: "BGN");
        _journalEntries
            .Setup(s => s.FindPostedBySourceDocumentAsync(
                JournalSourceDocumentTypes.Invoice, invoiceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildEntry(Guid.NewGuid(), "JE-2026-000052"));

        // Act
        await _consumer.Consume(ContextFor(@event));

        // Assert
        _postingEngine.Verify(
            e => e.ApplyAsync(It.IsAny<ApplyPostingRuleRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// The recovery half of the invoice-side no-op: the already-posted entry's id and number are re-published on
    /// a fresh InvoicePostedEvent carrying the source event's correlation id, so a lost back-event is recoverable
    /// without a second entry (SDD-PAY-001 §2.5 hardening).
    /// </summary>
    [Test]
    [Category("SDD-PAY-001")]
    public async Task InvoiceConfirmedConsumer_PostedEntryExists_RepublishesInvoicePostedEvent_ForTheExistingEntry()
    {
        // Arrange
        Guid invoiceId = Guid.NewGuid();
        Guid existingEntryId = Guid.NewGuid();
        InvoiceConfirmedEvent @event = BuildEvent(
            invoiceId,
            postingRuleKey: "SALE_INVOICE",
            net: 100.00m,
            tax: 20.00m,
            gross: 120.00m,
            currencyCode: "BGN");
        _journalEntries
            .Setup(s => s.FindPostedBySourceDocumentAsync(
                JournalSourceDocumentTypes.Invoice, invoiceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildEntry(existingEntryId, "JE-2026-000053"));

        // Act
        await _consumer.Consume(ContextFor(@event));

        // Assert
        InvoicePostedEvent published = _publishedEvents.OfType<InvoicePostedEvent>().Single();
        Assert.Multiple(() =>
        {
            Assert.That(published.InvoiceId, Is.EqualTo(invoiceId));
            Assert.That(published.JournalEntryId, Is.EqualTo(existingEntryId));
            Assert.That(published.JournalEntryNumber, Is.EqualTo("JE-2026-000053"));
            Assert.That(published.CorrelationId, Is.EqualTo(@event.CorrelationId));
            Assert.That(published.MessageId, Is.Not.EqualTo(@event.MessageId));
        });
    }

    private static InvoiceConfirmedEvent BuildEvent(
        Guid invoiceId,
        string postingRuleKey,
        decimal net,
        decimal tax,
        decimal gross,
        string currencyCode,
        InvoiceDocumentType documentType = InvoiceDocumentType.SaleInvoice)
    {
        return new InvoiceConfirmedEvent
        {
            MessageId = Guid.NewGuid(),
            CorrelationId = "corr-inv-confirmed-1",
            OccurredAt = IssueDate,
            InvoiceId = invoiceId,
            DocumentNumber = "SINV-2026-000001",
            DocumentType = documentType,
            Direction = documentType == InvoiceDocumentType.PurchaseInvoice
                ? InvoiceDirection.AP
                : InvoiceDirection.AR,
            CounterpartyId = Guid.NewGuid(),
            CurrencyCode = currencyCode,
            BaseCurrencyCode = "BGN",
            IssueDate = IssueDate,
            PostingRuleKey = postingRuleKey,
            NetTotal = net,
            TaxTotal = tax,
            GrossTotal = gross
        };
    }

    private static JournalEntryDto BuildEntry(Guid id, string entryNumber)
    {
        return new JournalEntryDto
        {
            Id = id,
            EntryNumber = entryNumber,
            EntryDate = IssueDate,
            Description = "Invoice SINV-2026-000001",
            BaseCurrencyCode = "BGN",
            Status = JournalEntryStatus.Posted,
            CreatedAt = IssueDate,
            PostedAt = IssueDate,
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
