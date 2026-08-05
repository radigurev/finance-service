using Finance.Common.Enums;
using Finance.Common.ErrorCodes;
using Finance.Common.Results;
using Finance.Infrastructure.Audit.Models;
using Finance.Infrastructure.Sequences;
using Finance.Payments.API.Auditing;
using Finance.Payments.API.Services;
using Finance.Payments.API.Tests.Builders;
using Finance.Payments.API.Tests.Fixtures;
using Finance.Payments.DBModel.Models;
using Finance.ServiceModel.Events.Payments;
using Finance.ServiceModel.Payments;
using Microsoft.EntityFrameworkCore;
using Moq;
using NUnit.Framework;

namespace Finance.Payments.API.Tests.Unit.Services;

/// <summary>
/// Unit tests for the confirm side effects and the Payments half of the posting handshake (SDD-PAY-001 §6.2):
/// gapless per-document-type numbering, the country-formatted number, the confirm stamps, audit-BEFORE-outbox
/// ordering, the <c>PaymentConfirmedEvent</c> payload, the status-history row, the guard-failure no-side-effects
/// contract, and every resolution branch of <c>POST /{id}/post</c> including the confirm-event RE-ENQUEUE recovery
/// path. The Journal-side consumer assertions belong to the Journal test project.
/// </summary>
[TestFixture]
[Category("SDD-PAY-001")]
public sealed class PaymentServiceConfirmTests
{
    private SqlitePaymentsDbContextScope _scope = null!;
    private PaymentServiceTestHarness _harness = null!;

    /// <summary>Creates a fresh SQLite-backed harness before each test.</summary>
    [SetUp]
    public void SetUp()
    {
        _scope = SqlitePaymentsDbContextFactory.Create();
        _harness = PaymentServiceTestHarness.Build(_scope.Context);
    }

    /// <summary>Disposes the SQLite scope after each test.</summary>
    [TearDown]
    public void TearDown()
    {
        _scope.Dispose();
    }

    [Test]
    public async Task Confirm_AssignsGaplessDocumentNumber_FromSequenceGenerator_PerDocumentType()
    {
        // Arrange
        PaymentDto receipt = await CreateDraftAsync(PaymentDocumentType.CustomerReceipt);
        PaymentDto secondReceipt = await CreateDraftAsync(PaymentDocumentType.CustomerReceipt);
        PaymentDto supplierPayment = await CreateDraftAsync(PaymentDocumentType.SupplierPayment);

        // Act
        PaymentDto confirmedReceipt = await ConfirmAsync(receipt);
        PaymentDto confirmedSecondReceipt = await ConfirmAsync(secondReceipt);
        PaymentDto confirmedSupplierPayment = await ConfirmAsync(supplierPayment);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(confirmedReceipt.DocumentNumber, Is.EqualTo("RCT-2026-000001"));
            Assert.That(confirmedSecondReceipt.DocumentNumber, Is.EqualTo("RCT-2026-000002"));
            Assert.That(confirmedSupplierPayment.DocumentNumber, Is.EqualTo("PAY-2026-000001"));
            Assert.That(_harness.SequenceCounters[SequenceKeys.Receipt], Is.EqualTo(2));
            Assert.That(_harness.SequenceCounters[SequenceKeys.Payment], Is.EqualTo(1));
        });
    }

    [Test]
    public async Task Confirm_FormatsDocumentNumber_ViaCountryStrategy()
    {
        // Arrange
        PaymentDto draft = await CreateDraftAsync(PaymentDocumentType.CustomerReceipt);

        // Act
        PaymentDto confirmed = await ConfirmAsync(draft);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(_harness.Country.GenerateDocumentNumberCallCount, Is.EqualTo(1));
            Assert.That(_harness.Country.RequestedSequenceValues, Is.EqualTo(new long[] { 1 }));
            Assert.That(confirmed.DocumentNumber, Is.EqualTo("RCT-2026-000001"));
        });
    }

    [Test]
    public async Task Confirm_StampsConfirmedAtAndConfirmedBy()
    {
        // Arrange
        PaymentDto draft = await CreateDraftAsync(PaymentDocumentType.CustomerReceipt);
        _harness.Clock.UtcNow = new DateTimeOffset(2026, 6, 20, 8, 30, 0, TimeSpan.Zero);

        // Act
        await ConfirmAsync(draft);

        // Assert
        Payment stored = await LoadAsync(draft.Id);
        Assert.Multiple(() =>
        {
            Assert.That(stored.ConfirmedAt, Is.EqualTo(new DateTimeOffset(2026, 6, 20, 8, 30, 0, TimeSpan.Zero)));
            Assert.That(stored.ConfirmedBy, Is.EqualTo(StubCurrentUserAccessor.TestUserId));
        });
    }

    [Test]
    public async Task Confirm_RecordsAuditStateChange_BeforeOutboxPublish()
    {
        // Arrange
        PaymentDto draft = await CreateDraftAsync(PaymentDocumentType.CustomerReceipt);
        _harness.Timeline.Clear();

        // Act
        await ConfirmAsync(draft);

        // Assert
        Assert.That(_harness.Timeline, Has.Count.EqualTo(2));
        AuditEntry audit = (AuditEntry)_harness.Timeline[0];
        Assert.Multiple(() =>
        {
            Assert.That(_harness.Timeline[1], Is.InstanceOf<PaymentConfirmedEvent>());
            Assert.That(audit.Operation, Is.EqualTo(AuditOperation.StateChange));
            Assert.That(audit.EventType, Is.EqualTo(PaymentAuditEventTypes.PaymentConfirmed));
            Assert.That(audit.EntityType, Is.EqualTo(PaymentAuditEventTypes.EntityType));
            Assert.That(audit.EntityId, Is.EqualTo(draft.Id.ToString()));
            Assert.That(audit.BeforeJson, Is.Not.Null.And.Not.Empty);
            Assert.That(audit.BeforeJson, Does.Contain("\"Status\":\"Draft\""));
            Assert.That(audit.AfterJson, Does.Contain("\"Status\":\"Confirmed\""));
        });
    }

    [Test]
    public async Task Confirm_PublishesPaymentConfirmedEvent_WithPostingRuleKeyAndBaseAmount()
    {
        // Arrange
        PaymentDto draft = await CreateDraftAsync(PaymentDocumentType.SupplierPayment);

        // Act
        PaymentDto confirmed = await ConfirmAsync(draft);

        // Assert
        PaymentConfirmedEvent published = _harness.EventsOf<PaymentConfirmedEvent>().Single();
        Assert.Multiple(() =>
        {
            Assert.That(published.PaymentId, Is.EqualTo(confirmed.Id));
            Assert.That(published.DocumentNumber, Is.EqualTo(confirmed.DocumentNumber));
            Assert.That(published.DocumentType, Is.EqualTo(PaymentDocumentType.SupplierPayment));
            Assert.That(published.Direction, Is.EqualTo(PaymentDirection.AP));
            Assert.That(published.PostingRuleKey, Is.EqualTo(PaymentDocumentTypeMap.SupplierPaymentRuleKey));
            Assert.That(published.Amount, Is.EqualTo(1000.00m));
            Assert.That(published.BaseAmount, Is.EqualTo(1000.00m));
            Assert.That(published.CurrencyCode, Is.EqualTo(FakePaymentCountryStrategy.BaseCurrency));
            Assert.That(published.BaseCurrencyCode, Is.EqualTo(FakePaymentCountryStrategy.BaseCurrency));
            Assert.That(published.PaymentDate, Is.EqualTo(CreatePaymentRequestBuilder.DefaultPaymentDate));
            Assert.That(published.SettlementAccountId, Is.EqualTo(503));
            Assert.That(published.MessageId, Is.Not.EqualTo(Guid.Empty));
        });
    }

    [Test]
    public async Task Confirm_AppendsStatusHistoryRow_DraftToConfirmed()
    {
        // Arrange
        PaymentDto draft = await CreateDraftAsync(PaymentDocumentType.CustomerReceipt);

        // Act
        await ConfirmAsync(draft);

        // Assert
        PaymentStatusHistory history = await _scope.Context.PaymentStatusHistory
            .SingleAsync(row => row.PaymentId == draft.Id, CancellationToken.None);
        Assert.Multiple(() =>
        {
            Assert.That(history.FromStatus, Is.EqualTo(nameof(PaymentStatus.Draft)));
            Assert.That(history.ToStatus, Is.EqualTo(nameof(PaymentStatus.Confirmed)));
            Assert.That(history.ChangedBy, Is.EqualTo(StubCurrentUserAccessor.TestUserId));
            Assert.That(history.Reason, Is.Null);
        });
    }

    [Test]
    public async Task Confirm_GuardFailure_PublishesNothing_AndConsumesNoSequenceValue()
    {
        // Arrange
        PaymentDto draft = await CreateDraftAsync(PaymentDocumentType.CustomerReceipt);
        _harness.Timeline.Clear();
        _harness.SettlementAccounts.Outcome = FakeSettlementAccountReader.ReaderOutcome.NotFound;

        // Act
        Result<PaymentDto> result = await _harness.Service.ConfirmAsync(
            draft.Id,
            new ConfirmPaymentRequest { RowVersion = draft.RowVersion },
            CancellationToken.None);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo(PaymentErrorCodes.PAYMENT_SETTLEMENT_ACCOUNT_NOT_FOUND));
            Assert.That(_harness.PublishedEvents, Is.Empty);
            Assert.That(_harness.RecordedAudits, Is.Empty);
            Assert.That(_harness.TotalSequenceValuesConsumed(), Is.Zero);
            Assert.That(_scope.Context.PaymentStatusHistory.Count(), Is.Zero);
        });
        _harness.SequenceMock.Verify(
            generator => generator.NextValueAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Test]
    public async Task Confirm_AssertsThePeriodOverThePaymentDate_NotAnUnrelatedToday()
    {
        // Arrange
        DateTimeOffset paymentDate = new(2026, 2, 20, 0, 0, 0, TimeSpan.Zero);
        Result<PaymentDto> created = await _harness.Service.CreateDraftAsync(
            CreatePaymentRequestBuilder.Create().WithPaymentDate(paymentDate).Build(),
            CancellationToken.None);
        Assert.That(created.IsSuccess, Is.True, created.ErrorCode);
        _harness.PeriodGuard.RequestedDates.Clear();

        // Act
        await ConfirmAsync(created.Value!);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(_harness.PeriodGuard.RequestedDates, Is.Not.Empty);
            Assert.That(
                _harness.PeriodGuard.RequestedDates,
                Is.All.EqualTo(paymentDate),
                "the guard is evaluated over PaymentDate, which drives period assignment");
        });
    }

    [Test]
    public async Task Confirm_StampsCorrelationId_OnAuditRow_StatusHistoryRow_AndEvent()
    {
        // Arrange
        _harness.Correlation.CorrelationId = "confirm-correlation";
        PaymentDto draft = await CreateDraftAsync(PaymentDocumentType.CustomerReceipt);
        _harness.Timeline.Clear();

        // Act
        await ConfirmAsync(draft);

        // Assert
        AuditEntry audit = _harness.RecordedAudits.Single();
        PaymentConfirmedEvent published = _harness.EventsOf<PaymentConfirmedEvent>().Single();
        PaymentStatusHistory history = await _scope.Context.PaymentStatusHistory
            .SingleAsync(row => row.PaymentId == draft.Id, CancellationToken.None);
        Assert.Multiple(() =>
        {
            Assert.That(audit.CorrelationId, Is.EqualTo("confirm-correlation"));
            Assert.That(history.CorrelationId, Is.EqualTo("confirm-correlation"));
            Assert.That(published.CorrelationId, Is.EqualTo("confirm-correlation"));
        });
    }

    [Test]
    public async Task LinkPostedJournalEntry_ConfirmedPayment_LinksEntryAndTransitionsToPosted()
    {
        // Arrange
        PaymentDto confirmed = await ConfirmAsync(await CreateDraftAsync(PaymentDocumentType.CustomerReceipt));
        Guid journalEntryId = Guid.NewGuid();
        _harness.Timeline.Clear();

        // Act
        Result result = await _harness.Service.LinkPostedJournalEntryAsync(
            confirmed.Id,
            journalEntryId,
            CancellationToken.None);

        // Assert
        Assert.That(result.IsSuccess, Is.True, result.ErrorCode);
        Payment stored = await LoadAsync(confirmed.Id);
        AuditEntry audit = _harness.RecordedAudits.Single();
        Assert.Multiple(() =>
        {
            Assert.That(stored.Status, Is.EqualTo(PaymentStatus.Posted));
            Assert.That(stored.JournalEntryId, Is.EqualTo(journalEntryId));
            Assert.That(stored.PostedAt, Is.Not.Null);
            Assert.That(audit.EventType, Is.EqualTo(PaymentAuditEventTypes.PaymentPosted));
            Assert.That(audit.Operation, Is.EqualTo(AuditOperation.StateChange));
        });
    }

    [Test]
    public async Task LinkPostedJournalEntry_AlreadyPostedPayment_IsSuccessNoOp()
    {
        // Arrange
        PaymentDto confirmed = await ConfirmAsync(await CreateDraftAsync(PaymentDocumentType.CustomerReceipt));
        Guid journalEntryId = Guid.NewGuid();
        await _harness.Service.LinkPostedJournalEntryAsync(
            confirmed.Id, journalEntryId, CancellationToken.None);
        _harness.Timeline.Clear();

        // Act
        Result replay = await _harness.Service.LinkPostedJournalEntryAsync(
            confirmed.Id,
            Guid.NewGuid(),
            CancellationToken.None);

        // Assert
        Assert.That(replay.IsSuccess, Is.True, replay.ErrorCode);
        Payment stored = await LoadAsync(confirmed.Id);
        Assert.Multiple(() =>
        {
            Assert.That(stored.JournalEntryId, Is.EqualTo(journalEntryId), "the link is never overwritten");
            Assert.That(_harness.RecordedAudits, Is.Empty, "no second audit row");
            Assert.That(
                _scope.Context.PaymentStatusHistory.Count(row => row.ToStatus == nameof(PaymentStatus.Posted)),
                Is.EqualTo(1),
                "no second history row");
        });
    }

    [Test]
    public async Task LinkPostedJournalEntry_UnknownPayment_ReturnsPaymentNotFound()
    {
        // Arrange
        Guid unknownPaymentId = Guid.NewGuid();

        // Act
        Result result = await _harness.Service.LinkPostedJournalEntryAsync(
            unknownPaymentId,
            Guid.NewGuid(),
            CancellationToken.None);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo(PaymentErrorCodes.PAYMENT_NOT_FOUND));
        });
    }

    [Test]
    public async Task LinkPostedJournalEntry_ClosedPeriodAfterConfirm_DoesNotReRunPeriodGuard()
    {
        // Arrange
        PaymentDto confirmed = await ConfirmAsync(await CreateDraftAsync(PaymentDocumentType.CustomerReceipt));
        _harness.PeriodGuard.RequestedDates.Clear();
        _harness.PeriodGuard.IsOpen = false;

        // Act
        Result result = await _harness.Service.LinkPostedJournalEntryAsync(
            confirmed.Id,
            Guid.NewGuid(),
            CancellationToken.None);

        // Assert
        Payment stored = await LoadAsync(confirmed.Id);
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True, result.ErrorCode);
            Assert.That(stored.Status, Is.EqualTo(PaymentStatus.Posted));
            Assert.That(_harness.PeriodGuard.RequestedDates, Is.Empty, "the link path is exempt by design");
        });
    }

    [Test]
    public async Task LinkPostedJournalEntry_KeepsTheAggregatesStoredCorrelationId()
    {
        // Arrange
        _harness.Correlation.CorrelationId = "chain-correlation";
        PaymentDto confirmed = await ConfirmAsync(await CreateDraftAsync(PaymentDocumentType.CustomerReceipt));
        PaymentConfirmedEvent sourceEvent = _harness.EventsOf<PaymentConfirmedEvent>().Single();
        _harness.Correlation.CorrelationId = "unrelated-consumer-scope";

        // Act
        Result result = await _harness.Service.LinkPostedJournalEntryAsync(
            confirmed.Id,
            Guid.NewGuid(),
            CancellationToken.None);

        // Assert
        Payment stored = await LoadAsync(confirmed.Id);
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True, result.ErrorCode);
            Assert.That(sourceEvent.CorrelationId, Is.EqualTo("chain-correlation"));
            Assert.That(
                stored.CorrelationId,
                Is.EqualTo("chain-correlation"),
                "the aggregate keeps the confirm→post chain's single correlation id");
        });
    }

    [Test]
    public async Task Post_ConfirmedButUnlinked_ReturnsPaymentPostingPending()
    {
        // Arrange
        PaymentDto confirmed = await ConfirmAsync(await CreateDraftAsync(PaymentDocumentType.CustomerReceipt));

        // Act
        Result<PaymentDto> result = await _harness.Service.PostAsync(
            confirmed.Id,
            new PostPaymentRequest { RowVersion = confirmed.RowVersion },
            CancellationToken.None);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo(PaymentErrorCodes.PAYMENT_POSTING_PENDING));
        });
    }

    [Test]
    public async Task Post_ConfirmedUnlinkedPayment_ReEnqueuesConfirmedEvent_ReturnsPostingPending()
    {
        // Arrange
        _harness.Correlation.CorrelationId = "stored-correlation";
        PaymentDto confirmed = await ConfirmAsync(await CreateDraftAsync(PaymentDocumentType.CustomerReceipt));
        PaymentConfirmedEvent original = _harness.EventsOf<PaymentConfirmedEvent>().Single();
        byte[] rowVersionBeforePost = (await LoadAsync(confirmed.Id)).RowVersion;
        _harness.Timeline.Clear();
        _harness.Correlation.CorrelationId = "ambient-request-correlation";

        // Act
        Result<PaymentDto> result = await _harness.Service.PostAsync(
            confirmed.Id,
            new PostPaymentRequest { RowVersion = confirmed.RowVersion },
            CancellationToken.None);

        // Assert
        PaymentConfirmedEvent reEnqueued = _harness.EventsOf<PaymentConfirmedEvent>().Single();
        Payment stored = await LoadAsync(confirmed.Id);
        Assert.Multiple(() =>
        {
            Assert.That(result.ErrorCode, Is.EqualTo(PaymentErrorCodes.PAYMENT_POSTING_PENDING));
            Assert.That(reEnqueued.MessageId, Is.Not.EqualTo(original.MessageId), "a FRESH MessageId");
            Assert.That(reEnqueued.CorrelationId, Is.EqualTo("stored-correlation"), "the STORED correlation id");
            Assert.That(reEnqueued.PaymentId, Is.EqualTo(confirmed.Id));
            Assert.That(reEnqueued.DocumentNumber, Is.EqualTo(confirmed.DocumentNumber));
            Assert.That(reEnqueued.Amount, Is.EqualTo(original.Amount));
            Assert.That(stored.Status, Is.EqualTo(PaymentStatus.Confirmed), "no state transition");
            Assert.That(stored.RowVersion, Is.EqualTo(rowVersionBeforePost), "no RowVersion bump");
            Assert.That(_harness.RecordedAudits, Is.Empty, "no second audit StateChange row");
            Assert.That(
                _scope.Context.PaymentStatusHistory.Count(row => row.PaymentId == confirmed.Id),
                Is.EqualTo(1),
                "no second status-history row");
        });
    }

    [Test]
    public async Task Post_ConfirmedUnlinkedPayment_RepublishDoesNotProduceASecondEntry()
    {
        // Arrange
        PaymentDto confirmed = await ConfirmAsync(await CreateDraftAsync(PaymentDocumentType.CustomerReceipt));
        _harness.Timeline.Clear();

        // Act
        await _harness.Service.PostAsync(
            confirmed.Id,
            new PostPaymentRequest { RowVersion = confirmed.RowVersion },
            CancellationToken.None);
        Result<PaymentDto> second = await _harness.Service.PostAsync(
            confirmed.Id,
            new PostPaymentRequest { RowVersion = confirmed.RowVersion },
            CancellationToken.None);

        // Assert
        Payment stored = await LoadAsync(confirmed.Id);
        Assert.Multiple(() =>
        {
            Assert.That(second.ErrorCode, Is.EqualTo(PaymentErrorCodes.PAYMENT_POSTING_PENDING));
            Assert.That(
                _harness.EventsOf<PaymentConfirmedEvent>(),
                Has.Count.EqualTo(2),
                "each retry adds exactly one outbox message");
            Assert.That(
                _harness.EventsOf<PaymentConfirmedEvent>().Select(message => message.MessageId).Distinct().Count(),
                Is.EqualTo(2),
                "each carries its own fresh MessageId so the Redis claim cannot short-circuit it");
            Assert.That(stored.JournalEntryId, Is.Null, "no entry is linked by the retry itself");
            Assert.That(stored.Status, Is.EqualTo(PaymentStatus.Confirmed));
            Assert.That(_harness.RecordedAudits, Is.Empty);
            Assert.That(_harness.TotalSequenceValuesConsumed(), Is.EqualTo(1), "no second gapless number");
        });
    }

    [Test]
    public async Task Post_AlreadyPosted_IsIdempotentSuccess()
    {
        // Arrange
        Payment posted = await SeedAsync(PaymentBuilder.Create()
            .WithStatus(PaymentStatus.Posted)
            .WithJournalEntryId(Guid.NewGuid())
            .Build());

        // Act
        Result<PaymentDto> result = await _harness.Service.PostAsync(
            posted.Id,
            new PostPaymentRequest { RowVersion = Convert.ToBase64String(posted.RowVersion) },
            CancellationToken.None);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True, result.ErrorCode);
            Assert.That(result.Value!.Status, Is.EqualTo(PaymentStatus.Posted));
            Assert.That(_harness.PublishedEvents, Is.Empty);
            Assert.That(_harness.RecordedAudits, Is.Empty);
        });
    }

    [Test]
    public async Task Post_NonConfirmedPayment_ReturnsPaymentNotConfirmed()
    {
        // Arrange
        PaymentDto draft = await CreateDraftAsync(PaymentDocumentType.CustomerReceipt);

        // Act
        Result<PaymentDto> result = await _harness.Service.PostAsync(
            draft.Id,
            new PostPaymentRequest { RowVersion = draft.RowVersion },
            CancellationToken.None);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo(PaymentErrorCodes.PAYMENT_NOT_CONFIRMED));
            Assert.That(_harness.PublishedEvents, Is.Empty);
        });
    }

    [Test]
    public async Task Post_LinkedButClosedPeriod_ReturnsPaymentPeriodClosed()
    {
        // Arrange — Confirmed AND linked is never a persisted state through the v1 paths, so it is built directly.
        Payment linkedConfirmed = await SeedAsync(PaymentBuilder.Create()
            .WithStatus(PaymentStatus.Confirmed)
            .WithJournalEntryId(Guid.NewGuid())
            .Build());
        _harness.PeriodGuard.IsOpen = false;

        // Act
        Result<PaymentDto> result = await _harness.Service.PostAsync(
            linkedConfirmed.Id,
            new PostPaymentRequest { RowVersion = Convert.ToBase64String(linkedConfirmed.RowVersion) },
            CancellationToken.None);

        // Assert
        Payment stored = await LoadAsync(linkedConfirmed.Id);
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo(PaymentErrorCodes.PAYMENT_PERIOD_CLOSED));
            Assert.That(stored.Status, Is.EqualTo(PaymentStatus.Confirmed));
            Assert.That(_harness.RecordedAudits, Is.Empty);
        });
    }

    /// <summary>Creates a valid draft payment of the requested document type through the production path.</summary>
    /// <param name="documentType">The document type to create.</param>
    /// <returns>The created payment DTO.</returns>
    private async Task<PaymentDto> CreateDraftAsync(PaymentDocumentType documentType)
    {
        Result<PaymentDto> created = await _harness.Service.CreateDraftAsync(
            CreatePaymentRequestBuilder.Create().WithDocumentType(documentType).Build(),
            CancellationToken.None);

        Assert.That(created.IsSuccess, Is.True, created.ErrorCode);
        return created.Value!;
    }

    /// <summary>Confirms the supplied draft and asserts the transition succeeded.</summary>
    /// <param name="draft">The draft to confirm.</param>
    /// <returns>The confirmed payment DTO.</returns>
    private async Task<PaymentDto> ConfirmAsync(PaymentDto draft)
    {
        Result<PaymentDto> confirmed = await _harness.Service.ConfirmAsync(
            draft.Id,
            new ConfirmPaymentRequest { RowVersion = draft.RowVersion },
            CancellationToken.None);

        Assert.That(confirmed.IsSuccess, Is.True, confirmed.ErrorCode);
        return confirmed.Value!;
    }

    /// <summary>Reads the persisted payment without tracking so the stored column values are asserted.</summary>
    /// <param name="id">The payment identifier.</param>
    /// <returns>The persisted payment.</returns>
    private Task<Payment> LoadAsync(Guid id) => _scope.Context.Payments
        .AsNoTracking()
        .SingleAsync(payment => payment.Id == id, CancellationToken.None);

    /// <summary>Persists a directly-built payment so unreachable states can be exercised.</summary>
    /// <param name="payment">The payment to persist.</param>
    /// <returns>The persisted payment.</returns>
    private async Task<Payment> SeedAsync(Payment payment)
    {
        _scope.Context.Payments.Add(payment);
        await _scope.Context.SaveChangesAsync(CancellationToken.None);
        return payment;
    }
}
