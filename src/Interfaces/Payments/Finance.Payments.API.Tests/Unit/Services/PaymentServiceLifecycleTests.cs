using Finance.Common.Enums;
using Finance.Common.ErrorCodes;
using Finance.Common.Results;
using Finance.Infrastructure.Audit.Models;
using Finance.Payments.API.Auditing;
using Finance.Payments.API.Tests.Builders;
using Finance.Payments.API.Tests.Fixtures;
using Finance.Payments.DBModel.Models;
using Finance.ServiceModel.Payments;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;

namespace Finance.Payments.API.Tests.Unit.Services;

/// <summary>
/// Unit tests for the <c>PaymentService</c> state machine and its workflow guards (SDD-PAY-001 §6.1): confirm from
/// <c>Draft</c> only, the settlement-account / period / confirm-clock-year guards short-circuiting with NO side
/// effects, cancel legal from <c>Draft</c> ONLY, and the immutability of a confirmed-or-later payment. Runs fully
/// offline against a SQLite in-memory <c>PaymentsDbContext</c> with the REAL workflow engine.
/// </summary>
[TestFixture]
[Category("SDD-PAY-001")]
public sealed class PaymentServiceLifecycleTests
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
    public async Task Confirm_DraftPayment_TransitionsToConfirmed()
    {
        // Arrange
        PaymentDto draft = await CreateDraftAsync();

        // Act
        Result<PaymentDto> result = await _harness.Service.ConfirmAsync(
            draft.Id,
            new ConfirmPaymentRequest { RowVersion = draft.RowVersion },
            CancellationToken.None);

        // Assert
        Assert.That(result.IsSuccess, Is.True, result.ErrorCode);
        Assert.Multiple(() =>
        {
            Assert.That(result.Value!.Status, Is.EqualTo(PaymentStatus.Confirmed));
            Assert.That(result.Value.DocumentNumber, Is.Not.Null);
            Assert.That(result.Value.JournalEntryId, Is.Null);
        });
    }

    [Test]
    public async Task Confirm_NonDraftPayment_ReturnsPaymentNotDraft()
    {
        // Arrange
        Payment confirmed = await SeedAsync(PaymentBuilder.Create()
            .WithStatus(PaymentStatus.Confirmed)
            .Build());

        // Act
        Result<PaymentDto> result = await _harness.Service.ConfirmAsync(
            confirmed.Id,
            new ConfirmPaymentRequest { RowVersion = TokenOf(confirmed) },
            CancellationToken.None);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo(PaymentErrorCodes.PAYMENT_NOT_DRAFT));
            Assert.That(_harness.TotalSequenceValuesConsumed(), Is.Zero);
            Assert.That(_harness.PublishedEvents, Is.Empty);
        });
    }

    [Test]
    public async Task Confirm_DraftWithExistingDocumentNumber_ReturnsPaymentDuplicateDocumentNumber()
    {
        // Arrange
        Payment draft = await SeedAsync(PaymentBuilder.Create()
            .WithStatus(PaymentStatus.Draft)
            .WithDocumentNumber("RCT-2026-000009")
            .Build());

        // Act
        Result<PaymentDto> result = await _harness.Service.ConfirmAsync(
            draft.Id,
            new ConfirmPaymentRequest { RowVersion = TokenOf(draft) },
            CancellationToken.None);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo(PaymentErrorCodes.PAYMENT_DUPLICATE_DOCUMENT_NUMBER));
            Assert.That(_harness.TotalSequenceValuesConsumed(), Is.Zero);
        });
    }

    [Test]
    public async Task Confirm_PaymentDateYearDiffersFromConfirmClockYear_ReturnsPaymentDateYearMismatch_NoNumberAllocated()
    {
        // Arrange
        PaymentDto draft = await CreateDraftAsync();
        _harness.Clock.UtcNow = new DateTimeOffset(2027, 1, 3, 9, 0, 0, TimeSpan.Zero);

        // Act
        Result<PaymentDto> result = await _harness.Service.ConfirmAsync(
            draft.Id,
            new ConfirmPaymentRequest { RowVersion = draft.RowVersion },
            CancellationToken.None);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo(PaymentErrorCodes.PAYMENT_DATE_YEAR_MISMATCH));
            Assert.That(_harness.TotalSequenceValuesConsumed(), Is.Zero);
            Assert.That(_harness.PublishedEvents, Is.Empty);
        });
    }

    [Test]
    public async Task Confirm_ClosedPeriod_ReturnsPaymentPeriodClosed_NoNumberAllocated()
    {
        // Arrange
        PaymentDto draft = await CreateDraftAsync();
        _harness.Timeline.Clear();
        _harness.PeriodGuard.IsOpen = false;

        // Act
        Result<PaymentDto> result = await _harness.Service.ConfirmAsync(
            draft.Id,
            new ConfirmPaymentRequest { RowVersion = draft.RowVersion },
            CancellationToken.None);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo(PaymentErrorCodes.PAYMENT_PERIOD_CLOSED));
            Assert.That(_harness.TotalSequenceValuesConsumed(), Is.Zero);
            Assert.That(_harness.RecordedAudits, Is.Empty, "the guard runs before the transaction");
            Assert.That(_harness.PublishedEvents, Is.Empty, "no outbox message is enqueued");
        });
    }

    [Test]
    public async Task Confirm_NoPeriodForDate_ReturnsPaymentPeriodClosed()
    {
        // Arrange
        PaymentDto draft = await CreateDraftAsync();
        _harness.PeriodGuard.IsOpen = false;

        // Act
        Result<PaymentDto> result = await _harness.Service.ConfirmAsync(
            draft.Id,
            new ConfirmPaymentRequest { RowVersion = draft.RowVersion },
            CancellationToken.None);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo(PaymentErrorCodes.PAYMENT_PERIOD_CLOSED));
            Assert.That(_harness.PeriodGuard.RequestedDates, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public async Task Confirm_PeriodsServiceUnreachable_FailsClosed_ReturnsPaymentPeriodClosed()
    {
        // Arrange
        PaymentDto draft = await CreateDraftAsync();
        _harness.PeriodGuard.IsOpen = false;

        // Act
        Result<PaymentDto> result = await _harness.Service.ConfirmAsync(
            draft.Id,
            new ConfirmPaymentRequest { RowVersion = draft.RowVersion },
            CancellationToken.None);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo(PaymentErrorCodes.PAYMENT_PERIOD_CLOSED));
            Assert.That(_harness.TotalSequenceValuesConsumed(), Is.Zero);
        });
    }

    [Test]
    public async Task Confirm_InactiveSettlementAccount_ReturnsPaymentSettlementAccountInactive()
    {
        // Arrange
        PaymentDto draft = await CreateDraftAsync();
        _harness.SettlementAccounts.Outcome = FakeSettlementAccountReader.ReaderOutcome.Inactive;

        // Act
        Result<PaymentDto> result = await _harness.Service.ConfirmAsync(
            draft.Id,
            new ConfirmPaymentRequest { RowVersion = draft.RowVersion },
            CancellationToken.None);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo(PaymentErrorCodes.PAYMENT_SETTLEMENT_ACCOUNT_INACTIVE));
            Assert.That(_harness.TotalSequenceValuesConsumed(), Is.Zero);
        });
    }

    [Test]
    public async Task Cancel_DraftPayment_TransitionsToCancelled()
    {
        // Arrange
        PaymentDto draft = await CreateDraftAsync();

        // Act
        Result<PaymentDto> result = await _harness.Service.CancelAsync(
            draft.Id,
            new CancelPaymentRequest { Reason = "Entered twice", RowVersion = draft.RowVersion },
            CancellationToken.None);

        // Assert
        Assert.That(result.IsSuccess, Is.True, result.ErrorCode);
        Assert.Multiple(() =>
        {
            Assert.That(result.Value!.Status, Is.EqualTo(PaymentStatus.Cancelled));
            Assert.That(result.Value.CancellationReason, Is.EqualTo("Entered twice"));
            Assert.That(result.Value.DocumentNumber, Is.Null, "a draft never held a gapless number");
        });
    }

    [Test]
    public async Task Cancel_ConfirmedPayment_ReturnsInvalidPaymentStateTransition()
    {
        // Arrange
        Payment confirmed = await SeedAsync(PaymentBuilder.Create()
            .WithStatus(PaymentStatus.Confirmed)
            .Build());

        // Act
        Result<PaymentDto> result = await _harness.Service.CancelAsync(
            confirmed.Id,
            new CancelPaymentRequest { Reason = "Operator error", RowVersion = TokenOf(confirmed) },
            CancellationToken.None);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo(PaymentErrorCodes.INVALID_PAYMENT_STATE_TRANSITION));
            Assert.That(_harness.PublishedEvents, Is.Empty);
            Assert.That(
                _scope.Context.Payments.Single(payment => payment.Id == confirmed.Id).Status,
                Is.EqualTo(PaymentStatus.Confirmed));
        });
    }

    [Test]
    public async Task Cancel_PostedPayment_ReturnsInvalidPaymentStateTransition()
    {
        // Arrange
        Payment posted = await SeedAsync(PaymentBuilder.Create()
            .WithStatus(PaymentStatus.Posted)
            .WithJournalEntryId(Guid.NewGuid())
            .Build());

        // Act
        Result<PaymentDto> result = await _harness.Service.CancelAsync(
            posted.Id,
            new CancelPaymentRequest { Reason = "Operator error", RowVersion = TokenOf(posted) },
            CancellationToken.None);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo(PaymentErrorCodes.INVALID_PAYMENT_STATE_TRANSITION));
            Assert.That(_harness.PublishedEvents, Is.Empty);
        });
    }

    [Test]
    public async Task Cancel_WithoutReason_ReturnsPaymentCancelReasonRequired()
    {
        // Arrange
        PaymentDto draft = await CreateDraftAsync();

        // Act
        Result<PaymentDto> result = await _harness.Service.CancelAsync(
            draft.Id,
            new CancelPaymentRequest { Reason = "   ", RowVersion = draft.RowVersion },
            CancellationToken.None);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo(PaymentErrorCodes.PAYMENT_CANCEL_REASON_REQUIRED));
            Assert.That(_harness.PublishedEvents, Is.Empty);
        });
    }

    [Test]
    public async Task Cancel_AllocatedPayment_ReturnsPaymentHasAllocations()
    {
        // Arrange
        Payment allocatedDraft = await SeedAsync(PaymentBuilder.Create()
            .WithStatus(PaymentStatus.Draft)
            .WithDocumentNumber(null)
            .WithAllocatedAmount(250.00m)
            .Build());

        // Act
        Result<PaymentDto> result = await _harness.Service.CancelAsync(
            allocatedDraft.Id,
            new CancelPaymentRequest { Reason = "Void", RowVersion = TokenOf(allocatedDraft) },
            CancellationToken.None);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo(PaymentErrorCodes.PAYMENT_HAS_ALLOCATIONS));
            Assert.That(_harness.PublishedEvents, Is.Empty);
        });
    }

    [Test]
    public async Task Update_ConfirmedPayment_ReturnsPaymentPostedImmutable()
    {
        // Arrange
        Payment confirmed = await SeedAsync(PaymentBuilder.Create()
            .WithStatus(PaymentStatus.Confirmed)
            .Build());

        // Act
        Result<PaymentDto> result = await _harness.Service.UpdateDraftAsync(
            confirmed.Id,
            UpdateRequestFor(confirmed),
            CancellationToken.None);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo(PaymentErrorCodes.PAYMENT_POSTED_IMMUTABLE));
            Assert.That(_harness.RecordedAudits, Is.Empty);
        });
    }

    [Test]
    public async Task Delete_ConfirmedPayment_ReturnsPaymentPostedImmutable()
    {
        // Arrange
        Payment confirmed = await SeedAsync(PaymentBuilder.Create()
            .WithStatus(PaymentStatus.Confirmed)
            .Build());

        // Act
        Result result = await _harness.Service.DeleteDraftAsync(confirmed.Id, CancellationToken.None);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo(PaymentErrorCodes.PAYMENT_POSTED_IMMUTABLE));
            Assert.That(_scope.Context.Payments.Count(), Is.EqualTo(1));
        });
    }

    [Test]
    public async Task Delete_DraftPayment_RemovesPayment_AndRecordsAuditDelete()
    {
        // Arrange
        PaymentDto draft = await CreateDraftAsync();
        _harness.Timeline.Clear();

        // Act
        Result result = await _harness.Service.DeleteDraftAsync(draft.Id, CancellationToken.None);

        // Assert
        Assert.That(result.IsSuccess, Is.True, result.ErrorCode);
        AuditEntry recorded = _harness.RecordedAudits.Single();
        Assert.Multiple(() =>
        {
            Assert.That(_scope.Context.Payments.Count(), Is.Zero);
            Assert.That(recorded.Operation, Is.EqualTo(AuditOperation.Delete));
            Assert.That(recorded.EventType, Is.EqualTo(PaymentAuditEventTypes.PaymentDeleted));
            Assert.That(recorded.AfterJson, Is.EqualTo("{\"deleted\":true}"));
            Assert.That(recorded.BeforeJson, Is.Not.Null.And.Not.Empty);
        });
    }

    /// <summary>Creates a valid draft payment through the production service path.</summary>
    /// <returns>The created payment DTO, carrying its current base64 row version.</returns>
    private async Task<PaymentDto> CreateDraftAsync()
    {
        Result<PaymentDto> created = await _harness.Service.CreateDraftAsync(
            CreatePaymentRequestBuilder.Create().Build(),
            CancellationToken.None);

        Assert.That(created.IsSuccess, Is.True, created.ErrorCode);
        return created.Value!;
    }

    /// <summary>Persists a directly-built payment so unreachable states can be exercised.</summary>
    /// <param name="payment">The payment to persist.</param>
    /// <returns>The persisted payment.</returns>
    private async Task<Payment> SeedAsync(Payment payment)
    {
        _scope.Context.Payments.Add(payment);
        await _scope.Context.SaveChangesAsync(CancellationToken.None);
        return payment;
    }

    /// <summary>Renders the payment's current concurrency token in the base64 form the requests carry.</summary>
    /// <param name="payment">The payment whose token is read.</param>
    /// <returns>The base64 row version.</returns>
    private static string TokenOf(Payment payment) => Convert.ToBase64String(payment.RowVersion);

    /// <summary>Builds a same-shape update request so only the state guard can reject it.</summary>
    /// <param name="payment">The payment to echo.</param>
    /// <returns>The update request.</returns>
    private static UpdatePaymentRequest UpdateRequestFor(Payment payment) => new()
    {
        DocumentType = payment.DocumentType,
        Method = payment.Method,
        CounterpartyId = payment.CounterpartyId,
        CurrencyCode = payment.CurrencyCode,
        Amount = payment.Amount,
        ExchangeRate = payment.ExchangeRate,
        SettlementAccountId = payment.SettlementAccountId,
        PaymentDate = payment.PaymentDate,
        BankReference = payment.BankReference,
        RowVersion = TokenOf(payment)
    };
}
