using Finance.Common.Enums;
using Finance.Common.ErrorCodes;
using Finance.Common.Results;
using Finance.Infrastructure.Audit;
using Finance.Infrastructure.Audit.Models;
using Finance.Payments.API.Auditing;
using Finance.Payments.API.Tests.Builders;
using Finance.Payments.API.Tests.Fixtures;
using Finance.Payments.DBModel.Models;
using Finance.ServiceModel.Events.Payments;
using Finance.ServiceModel.Payments;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;

namespace Finance.Payments.API.Tests.Unit.Services;

/// <summary>
/// Unit tests for reversal — the immutability-preserving correction — and for posted immutability
/// (SDD-PAY-001 §2.7, §2.10, §6.5): reverse is legal from <c>Posted</c> ONLY, requires a reason, is hard-blocked by
/// allocations, PRE-CHECKS the fiscal period over the LINKED entry's date (which equals <c>PaymentDate</c> by
/// construction), mutates nothing but the state flag / <c>ReversedAt</c> / row version / history row, and publishes
/// <c>PaymentReversedEvent</c>.
/// </summary>
[TestFixture]
[Category("SDD-PAY-001")]
public sealed class PaymentServiceReversalTests
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
    public async Task Reverse_PostedPayment_TransitionsToReversed_AndStampsReversedAt()
    {
        // Arrange
        Payment posted = await SeedPostedAsync();
        _harness.Clock.UtcNow = new DateTimeOffset(2026, 6, 20, 12, 0, 0, TimeSpan.Zero);

        // Act
        Result<PaymentDto> result = await _harness.Service.ReverseAsync(
            posted.Id,
            new ReversePaymentRequest { Reason = "Duplicate receipt", RowVersion = TokenOf(posted) },
            CancellationToken.None);

        // Assert
        Assert.That(result.IsSuccess, Is.True, result.ErrorCode);
        Payment stored = await LoadAsync(posted.Id);
        Assert.Multiple(() =>
        {
            Assert.That(stored.Status, Is.EqualTo(PaymentStatus.Reversed));
            Assert.That(stored.ReversedAt, Is.EqualTo(new DateTimeOffset(2026, 6, 20, 12, 0, 0, TimeSpan.Zero)));
            Assert.That(
                _scope.Context.PaymentStatusHistory.Count(row =>
                    row.PaymentId == posted.Id && row.ToStatus == nameof(PaymentStatus.Reversed)),
                Is.EqualTo(1));
        });
    }

    [Test]
    public async Task Reverse_WithoutReason_ReturnsPaymentReverseReasonRequired()
    {
        // Arrange
        Payment posted = await SeedPostedAsync();

        // Act
        Result<PaymentDto> result = await _harness.Service.ReverseAsync(
            posted.Id,
            new ReversePaymentRequest { Reason = "  ", RowVersion = TokenOf(posted) },
            CancellationToken.None);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo(PaymentErrorCodes.PAYMENT_REVERSE_REASON_REQUIRED));
            Assert.That(_harness.PublishedEvents, Is.Empty);
            Assert.That(_harness.PeriodGuard.RequestedDates, Is.Empty, "the reason is asserted first");
        });
    }

    [Test]
    public async Task Reverse_NonPostedPayment_ReturnsInvalidPaymentStateTransition()
    {
        // Arrange
        Payment confirmed = await SeedAsync(PaymentBuilder.Create()
            .WithStatus(PaymentStatus.Confirmed)
            .Build());

        // Act
        Result<PaymentDto> result = await _harness.Service.ReverseAsync(
            confirmed.Id,
            new ReversePaymentRequest { Reason = "Wrong amount", RowVersion = TokenOf(confirmed) },
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
    public async Task Reverse_AllocatedPayment_ReturnsPaymentHasAllocations()
    {
        // Arrange
        Payment posted = await SeedAsync(PaymentBuilder.Create()
            .WithStatus(PaymentStatus.Posted)
            .WithJournalEntryId(Guid.NewGuid())
            .WithAllocatedAmount(400.00m)
            .Build());

        // Act
        Result<PaymentDto> result = await _harness.Service.ReverseAsync(
            posted.Id,
            new ReversePaymentRequest { Reason = "Wrong invoice", RowVersion = TokenOf(posted) },
            CancellationToken.None);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo(PaymentErrorCodes.PAYMENT_HAS_ALLOCATIONS));
            Assert.That(_harness.PublishedEvents, Is.Empty);
            Assert.That(
                _harness.PeriodGuard.RequestedDates,
                Is.Empty,
                "the allocation block short-circuits before the period pre-check");
        });
    }

    [Test]
    public async Task Reverse_ClosedPeriodOnLinkedEntryDate_ReturnsPaymentPeriodClosed_NoTransitionNoEvent()
    {
        // Arrange
        Payment posted = await SeedPostedAsync();
        _harness.PeriodGuard.IsOpen = false;

        // Act
        Result<PaymentDto> result = await _harness.Service.ReverseAsync(
            posted.Id,
            new ReversePaymentRequest { Reason = "Bank returned the transfer", RowVersion = TokenOf(posted) },
            CancellationToken.None);

        // Assert
        Payment stored = await LoadAsync(posted.Id);
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo(PaymentErrorCodes.PAYMENT_PERIOD_CLOSED));
            Assert.That(stored.Status, Is.EqualTo(PaymentStatus.Posted), "no transition");
            Assert.That(stored.ReversedAt, Is.Null);
            Assert.That(_harness.PublishedEvents, Is.Empty, "no event");
            Assert.That(_harness.RecordedAudits, Is.Empty);
        });
    }

    [Test]
    public async Task Reverse_EvaluatesPeriodGuardOverPaymentDate_WhichEqualsLinkedEntryDate()
    {
        // Arrange
        DateTimeOffset paymentDate = new(2026, 3, 15, 0, 0, 0, TimeSpan.Zero);
        Payment posted = await SeedAsync(PaymentBuilder.Create()
            .WithStatus(PaymentStatus.Posted)
            .WithJournalEntryId(Guid.NewGuid())
            .WithPaymentDate(paymentDate)
            .Build());
        _harness.PeriodGuard.RequestedDates.Clear();

        // Act
        Result<PaymentDto> result = await _harness.Service.ReverseAsync(
            posted.Id,
            new ReversePaymentRequest { Reason = "Correction", RowVersion = TokenOf(posted) },
            CancellationToken.None);

        // Assert
        Assert.That(result.IsSuccess, Is.True, result.ErrorCode);
        Assert.That(
            _harness.PeriodGuard.RequestedDates,
            Is.EqualTo(new[] { paymentDate }),
            "the Journal consumer builds the entry with EntryDate = PaymentDate, so the two dates are identical");
    }

    [Test]
    public async Task Reverse_PublishesPaymentReversedEvent_WithJournalEntryIdAndReason()
    {
        // Arrange
        Guid journalEntryId = Guid.NewGuid();
        Payment posted = await SeedAsync(PaymentBuilder.Create()
            .WithStatus(PaymentStatus.Posted)
            .WithJournalEntryId(journalEntryId)
            .Build());

        // Act
        Result<PaymentDto> result = await _harness.Service.ReverseAsync(
            posted.Id,
            new ReversePaymentRequest { Reason = "Cash never arrived", RowVersion = TokenOf(posted) },
            CancellationToken.None);

        // Assert
        Assert.That(result.IsSuccess, Is.True, result.ErrorCode);
        PaymentReversedEvent published = _harness.EventsOf<PaymentReversedEvent>().Single();
        Assert.Multiple(() =>
        {
            Assert.That(published.PaymentId, Is.EqualTo(posted.Id));
            Assert.That(published.JournalEntryId, Is.EqualTo(journalEntryId));
            Assert.That(published.DocumentNumber, Is.EqualTo(posted.DocumentNumber));
            Assert.That(published.Reason, Is.EqualTo("Cash never arrived"));
            Assert.That(published.MessageId, Is.Not.EqualTo(Guid.Empty));
        });
    }

    [Test]
    public async Task Reverse_RecordsSensitiveAuditStateChange_BeforeOutboxPublish()
    {
        // Arrange
        Payment posted = await SeedPostedAsync();
        _harness.Timeline.Clear();

        // Act
        Result<PaymentDto> result = await _harness.Service.ReverseAsync(
            posted.Id,
            new ReversePaymentRequest { Reason = "Reversal reason", RowVersion = TokenOf(posted) },
            CancellationToken.None);

        // Assert
        Assert.That(result.IsSuccess, Is.True, result.ErrorCode);
        Assert.That(_harness.Timeline, Has.Count.EqualTo(2));
        AuditEntry audit = (AuditEntry)_harness.Timeline[0];
        Assert.Multiple(() =>
        {
            Assert.That(_harness.Timeline[1], Is.InstanceOf<PaymentReversedEvent>());
            Assert.That(audit.EventType, Is.EqualTo(PaymentAuditEventTypes.PaymentReversed));
            Assert.That(audit.Operation, Is.EqualTo(AuditOperation.StateChange));
            Assert.That(audit.Reason, Is.EqualTo("Reversal reason"));
            Assert.That(audit.BeforeJson, Is.Not.Null.And.Not.Empty);
        });
    }

    [Test]
    public async Task Reverse_DoesNotMutateAmountsOrDocumentNumberOrJournalEntryId()
    {
        // Arrange
        Guid journalEntryId = Guid.NewGuid();
        Payment posted = await SeedAsync(PaymentBuilder.Create()
            .WithStatus(PaymentStatus.Posted)
            .WithDocumentNumber("RCT-2026-000042")
            .WithJournalEntryId(journalEntryId)
            .WithAmount(1234.56m)
            .Build());

        // Act
        Result<PaymentDto> result = await _harness.Service.ReverseAsync(
            posted.Id,
            new ReversePaymentRequest { Reason = "Correction", RowVersion = TokenOf(posted) },
            CancellationToken.None);

        // Assert
        Assert.That(result.IsSuccess, Is.True, result.ErrorCode);
        Payment stored = await LoadAsync(posted.Id);
        Assert.Multiple(() =>
        {
            Assert.That(stored.DocumentNumber, Is.EqualTo("RCT-2026-000042"));
            Assert.That(stored.JournalEntryId, Is.EqualTo(journalEntryId));
            Assert.That(stored.Amount, Is.EqualTo(1234.56m));
            Assert.That(stored.BaseAmount, Is.EqualTo(1234.56m));
            Assert.That(stored.CancellationReason, Is.Null);
            Assert.That(_harness.TotalSequenceValuesConsumed(), Is.Zero, "no new document number");
        });
    }

    [Test]
    public async Task Reverse_AlreadyReversedPayment_ReturnsInvalidPaymentStateTransition_NoSecondEvent()
    {
        // Arrange
        Payment posted = await SeedPostedAsync();
        Result<PaymentDto> first = await _harness.Service.ReverseAsync(
            posted.Id,
            new ReversePaymentRequest { Reason = "First reversal", RowVersion = TokenOf(posted) },
            CancellationToken.None);
        Assert.That(first.IsSuccess, Is.True, first.ErrorCode);
        _harness.Timeline.Clear();

        // Act
        Result<PaymentDto> second = await _harness.Service.ReverseAsync(
            posted.Id,
            new ReversePaymentRequest { Reason = "Second reversal", RowVersion = first.Value!.RowVersion },
            CancellationToken.None);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(second.IsSuccess, Is.False);
            Assert.That(second.ErrorCode, Is.EqualTo(PaymentErrorCodes.INVALID_PAYMENT_STATE_TRANSITION));
            Assert.That(_harness.PublishedEvents, Is.Empty, "no second offsetting entry is ever requested");
            Assert.That(_harness.RecordedAudits, Is.Empty);
        });
    }

    [Test]
    [Category("SDD-AUDIT-001")]
    public void SensitiveAuditEventTypes_RequiresReason_IncludesPaymentCancelledAndPaymentReversed()
    {
        // Arrange
        string cancelled = PaymentAuditEventTypes.PaymentCancelled;
        string reversed = PaymentAuditEventTypes.PaymentReversed;

        // Act
        bool cancelRequiresReason = SensitiveAuditEventTypes.RequiresReason(cancelled);
        bool reverseRequiresReason = SensitiveAuditEventTypes.RequiresReason(reversed);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(cancelRequiresReason, Is.True);
            Assert.That(reverseRequiresReason, Is.True);
            Assert.That(SensitiveAuditEventTypes.PaymentCancelled, Is.EqualTo(cancelled));
            Assert.That(SensitiveAuditEventTypes.PaymentReversed, Is.EqualTo(reversed));
            Assert.That(
                SensitiveAuditEventTypes.RequiresReason(PaymentAuditEventTypes.PaymentConfirmed),
                Is.False,
                "confirm is a routine issuance and needs no reason");
        });
    }

    /// <summary>Seeds a posted, linked, unallocated payment so the reversal path can be exercised.</summary>
    /// <returns>The persisted payment.</returns>
    private Task<Payment> SeedPostedAsync() => SeedAsync(PaymentBuilder.Create()
        .WithStatus(PaymentStatus.Posted)
        .WithJournalEntryId(Guid.NewGuid())
        .Build());

    /// <summary>Persists a directly-built payment.</summary>
    /// <param name="payment">The payment to persist.</param>
    /// <returns>The persisted payment.</returns>
    private async Task<Payment> SeedAsync(Payment payment)
    {
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

    /// <summary>Renders the payment's current concurrency token in base64 form.</summary>
    /// <param name="payment">The payment whose token is read.</param>
    /// <returns>The base64 row version.</returns>
    private static string TokenOf(Payment payment) => Convert.ToBase64String(payment.RowVersion);
}
