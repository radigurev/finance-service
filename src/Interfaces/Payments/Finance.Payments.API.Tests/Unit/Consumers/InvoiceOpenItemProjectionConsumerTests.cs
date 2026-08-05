using Finance.Common.Enums;
using Finance.Common.ErrorCodes;
using Finance.Common.Results;
using Finance.Payments.API.Tests.Builders;
using Finance.Payments.API.Tests.Fixtures;
using Finance.Payments.DBModel.Models;
using Finance.ServiceModel.Events.Invoices;
using Finance.ServiceModel.Payments;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NUnit.Framework;

namespace Finance.Payments.API.Tests.Unit.Consumers;

/// <summary>
/// Unit tests for the FOUR invoice projection consumers and the real <c>InvoiceOpenItemProjection</c> they delegate
/// to (SDD-PAY-002 §2.3, §2.14, §6.4): the convergent upsert, the silent skip for a document type no payment can
/// settle, the no-downgrade rule, the TERMINAL statuses never being left, the cancellation TOMBSTONE that must never
/// dead-letter a draft cancel, the missing-row failure that must retry, and the locally-owned settled amount never
/// being overwritten.
/// </summary>
[TestFixture]
[Category("SDD-PAY-002")]
public sealed class InvoiceOpenItemProjectionConsumerTests
{
    private SqlitePaymentsDbContextScope _scope = null!;
    private ProjectionConsumerTestHarness _harness = null!;

    /// <summary>Creates a fresh SQLite-backed harness before each test.</summary>
    [SetUp]
    public void SetUp()
    {
        _scope = SqlitePaymentsDbContextFactory.Create();
        _harness = ProjectionConsumerTestHarness.Build(_scope.Context);
    }

    /// <summary>Disposes the SQLite scope after each test.</summary>
    [TearDown]
    public void TearDown()
    {
        _scope.Dispose();
    }

    [Test]
    public async Task InvoiceConfirmedConsumer_NewInvoice_InsertsOpenItem_WithZeroSettledAmount()
    {
        // Arrange
        InvoiceConfirmedEvent message = InvoiceConfirmedEventBuilder.Create()
            .WithGrossTotal(1200.00m)
            .Build();

        // Act
        await _harness.ConsumeAsync(message);

        // Assert
        InvoiceOpenItem stored = await LoadAsync(message.InvoiceId);
        Assert.Multiple(() =>
        {
            Assert.That(stored.DocumentNumber, Is.EqualTo(message.DocumentNumber));
            Assert.That(stored.DocumentType, Is.EqualTo(nameof(InvoiceDocumentType.SaleInvoice)));
            Assert.That(stored.Direction, Is.EqualTo(nameof(InvoiceDirection.AR)));
            Assert.That(stored.InvoiceStatus, Is.EqualTo(nameof(InvoiceStatus.Confirmed)));
            Assert.That(stored.GrossTotal, Is.EqualTo(1200.00m));
            Assert.That(stored.SettledAmount, Is.EqualTo(0.00m));
            Assert.That(stored.Outstanding, Is.EqualTo(1200.00m));
            Assert.That(stored.LastAppliedAt, Is.EqualTo(FixedTimeProvider.DefaultNow));
        });
    }

    [Test]
    public async Task InvoiceConfirmedConsumer_DuplicateEvent_IsNoOp_NoSecondOpenItem()
    {
        // Arrange
        InvoiceConfirmedEvent message = InvoiceConfirmedEventBuilder.Create().Build();
        await _harness.ConsumeAsync(message);

        // Act
        await _harness.ConsumeAsync(message);

        // Assert
        Assert.That(await _scope.Context.InvoiceOpenItems.CountAsync(CancellationToken.None), Is.EqualTo(1));
    }

    [Test]
    public async Task InvoiceConfirmedConsumer_ArrivingAfterPosted_DoesNotDowngradeInvoiceStatus()
    {
        // Arrange
        InvoiceConfirmedEvent confirmed = InvoiceConfirmedEventBuilder.Create().Build();
        await _harness.ConsumeAsync(confirmed);
        await _harness.ConsumeAsync(PostedEventFor(confirmed.InvoiceId));

        // Act
        await _harness.ConsumeAsync(confirmed);

        // Assert
        InvoiceOpenItem stored = await LoadAsync(confirmed.InvoiceId);
        Assert.That(stored.InvoiceStatus, Is.EqualTo(nameof(InvoiceStatus.Posted)));
    }

    [Test]
    public async Task InvoiceConfirmedConsumer_WithoutDueDateOrBookingRate_FallsBackToIssueDateAndRateOne()
    {
        // Arrange
        InvoiceConfirmedEvent message = InvoiceConfirmedEventBuilder.Create()
            .WithDueDate(null)
            .WithBookingExchangeRate(null)
            .Build();

        // Act
        await _harness.ConsumeAsync(message);

        // Assert
        InvoiceOpenItem stored = await LoadAsync(message.InvoiceId);
        Assert.Multiple(() =>
        {
            Assert.That(stored.DueDate, Is.EqualTo(message.IssueDate));
            Assert.That(stored.BookingExchangeRate, Is.EqualTo(1.000000m));
        });
    }

    [Test]
    public async Task InvoiceConfirmedConsumer_CreditNote_DoesNotCreateOpenItem()
    {
        // Arrange
        InvoiceConfirmedEvent creditNote = InvoiceConfirmedEventBuilder.Create()
            .WithDocumentType(InvoiceDocumentType.CreditNote)
            .WithDocumentNumber("CN-2026-000001")
            .WithGrossTotal(200.00m)
            .Build();

        // Act & Assert
        Assert.That(async () => await _harness.ConsumeAsync(creditNote), Throws.Nothing);
        Assert.That(
            await _scope.Context.InvoiceOpenItems.CountAsync(CancellationToken.None),
            Is.Zero,
            "a credit note no payment can settle would otherwise age as a phantom balance forever");
    }

    [Test]
    public async Task InvoiceConfirmedConsumer_SettleableDocumentTypes_AllCreateOpenItems()
    {
        // Arrange
        InvoiceDocumentType[] settleable =
        [
            InvoiceDocumentType.SaleInvoice,
            InvoiceDocumentType.DebitNote,
            InvoiceDocumentType.PurchaseInvoice
        ];

        // Act
        foreach (InvoiceDocumentType documentType in settleable)
        {
            await _harness.ConsumeAsync(InvoiceConfirmedEventBuilder.Create()
                .WithDocumentType(documentType)
                .WithDocumentNumber($"DOC-{documentType}")
                .Build());
        }

        // Assert
        List<string> storedTypes = await _scope.Context.InvoiceOpenItems
            .AsNoTracking()
            .Select(item => item.DocumentType)
            .ToListAsync(CancellationToken.None);
        Assert.That(storedTypes, Is.EquivalentTo(settleable.Select(type => type.ToString())));
    }

    [Test]
    public async Task InvoicePostedConsumer_ExistingOpenItem_SetsPosted_AndStampsLastAppliedAt()
    {
        // Arrange
        InvoiceConfirmedEvent confirmed = InvoiceConfirmedEventBuilder.Create().Build();
        await _harness.ConsumeAsync(confirmed);
        _harness.Clock.UtcNow = FixedTimeProvider.DefaultNow.AddHours(3);

        // Act
        await _harness.ConsumeAsync(PostedEventFor(confirmed.InvoiceId));

        // Assert
        InvoiceOpenItem stored = await LoadAsync(confirmed.InvoiceId);
        Assert.Multiple(() =>
        {
            Assert.That(stored.InvoiceStatus, Is.EqualTo(nameof(InvoiceStatus.Posted)));
            Assert.That(stored.LastAppliedAt, Is.EqualTo(FixedTimeProvider.DefaultNow.AddHours(3)));
        });
    }

    [Test]
    public void InvoicePostedConsumer_MissingOpenItem_Throws_ForRetry_NoPartialRow()
    {
        // Arrange
        InvoicePostedEvent message = PostedEventFor(Guid.NewGuid());

        // Act & Assert
        Assert.That(
            async () => await _harness.ConsumeAsync(message),
            Throws.InstanceOf<InvalidOperationException>());
        Assert.That(_scope.Context.InvoiceOpenItems.Count(), Is.Zero, "no partial row is invented");
    }

    [Test]
    public async Task InvoiceCancelledConsumer_SetsCancelled_KeepsRowAndExistingAllocations()
    {
        // Arrange
        InvoiceConfirmedEvent confirmed = InvoiceConfirmedEventBuilder.Create().Build();
        await _harness.ConsumeAsync(confirmed);
        await SeedAllocationAsync(confirmed.InvoiceId, 100.00m);

        // Act
        await _harness.ConsumeAsync(CancelledEventFor(confirmed.InvoiceId, "CN reason"));

        // Assert
        InvoiceOpenItem stored = await LoadAsync(confirmed.InvoiceId);
        Assert.Multiple(() =>
        {
            Assert.That(stored.InvoiceStatus, Is.EqualTo(nameof(InvoiceStatus.Cancelled)));
            Assert.That(
                _scope.Context.PaymentAllocations.Count(),
                Is.EqualTo(1),
                "existing matches are never auto-released");
        });
    }

    [Test]
    public async Task InvoiceCancelledConsumer_MissingOpenItem_UpsertsCancelledTombstone_DoesNotThrow()
    {
        // Arrange
        Guid draftInvoiceId = Guid.NewGuid();
        InvoiceCancelledEvent message = new()
        {
            MessageId = Guid.NewGuid(),
            CorrelationId = StubCorrelationIdAccessor.DefaultCorrelationId,
            OccurredAt = FixedTimeProvider.DefaultNow,
            InvoiceId = draftInvoiceId,
            DocumentNumber = null,
            Reason = "Draft cancelled"
        };

        // Act & Assert
        Assert.That(async () => await _harness.ConsumeAsync(message), Throws.Nothing);
        InvoiceOpenItem tombstone = await LoadAsync(draftInvoiceId);
        Assert.Multiple(() =>
        {
            Assert.That(tombstone.InvoiceStatus, Is.EqualTo(nameof(InvoiceStatus.Cancelled)));
            Assert.That(tombstone.DocumentNumber, Is.Empty);
            Assert.That(tombstone.GrossTotal, Is.EqualTo(0.00m));
            Assert.That(tombstone.SettledAmount, Is.EqualTo(0.00m));
        });
    }

    [Test]
    public async Task InvoiceCancelledConsumer_ThenConfirmedRetry_LeavesRowCancelled_NeverResurrectsInvoice()
    {
        // Arrange
        InvoiceConfirmedEvent confirmed = InvoiceConfirmedEventBuilder.Create().Build();
        await _harness.ConsumeAsync(CancelledEventFor(confirmed.InvoiceId, "Cancelled while confirm retried"));

        // Act
        await _harness.ConsumeAsync(confirmed);

        // Assert
        InvoiceOpenItem stored = await LoadAsync(confirmed.InvoiceId);
        Assert.Multiple(() =>
        {
            Assert.That(
                stored.InvoiceStatus,
                Is.EqualTo(nameof(InvoiceStatus.Cancelled)),
                "a terminal row is never resurrected by a confirmation retry");
            Assert.That(stored.GrossTotal, Is.EqualTo(0.00m), "the tombstone's values are not refreshed");
        });
    }

    [Test]
    public async Task InvoiceCancelledConsumer_WithExistingAllocations_LogsOrphanedSettlementWarning()
    {
        // Arrange
        InvoiceConfirmedEvent confirmed = InvoiceConfirmedEventBuilder.Create().Build();
        await _harness.ConsumeAsync(confirmed);
        await SeedAllocationAsync(confirmed.InvoiceId, 250.00m);
        _harness.ProjectionLogger.Entries.Clear();

        // Act
        await _harness.ConsumeAsync(CancelledEventFor(confirmed.InvoiceId, "Cancelled after settlement"));

        // Assert
        IReadOnlyList<RecordedLogEntry> warnings =
            [.. _harness.ProjectionLogger.Entries.Where(entry => entry.Level == LogLevel.Warning)];
        Assert.Multiple(() =>
        {
            Assert.That(warnings, Has.Count.EqualTo(1));
            Assert.That(warnings[0].Message, Does.Contain("Orphaned settlement detected"));
            Assert.That(warnings[0].Message, Does.Contain("NOT auto-released"));
        });
    }

    [Test]
    public async Task InvoiceReversedConsumer_ExistingOpenItem_SetsReversed_AndStampsLastAppliedAt()
    {
        // Arrange
        InvoiceConfirmedEvent confirmed = InvoiceConfirmedEventBuilder.Create().Build();
        await _harness.ConsumeAsync(confirmed);
        await _harness.ConsumeAsync(PostedEventFor(confirmed.InvoiceId));
        _harness.Clock.UtcNow = FixedTimeProvider.DefaultNow.AddDays(2);

        // Act
        await _harness.ConsumeAsync(ReversedEventFor(confirmed.InvoiceId));

        // Assert
        InvoiceOpenItem stored = await LoadAsync(confirmed.InvoiceId);
        Assert.Multiple(() =>
        {
            Assert.That(stored.InvoiceStatus, Is.EqualTo(nameof(InvoiceStatus.Reversed)));
            Assert.That(stored.LastAppliedAt, Is.EqualTo(FixedTimeProvider.DefaultNow.AddDays(2)));
        });
    }

    [Test]
    public void InvoiceReversedConsumer_MissingOpenItem_Throws_ForRetry_NoPartialRow()
    {
        // Arrange
        InvoiceReversedEvent message = ReversedEventFor(Guid.NewGuid());

        // Act & Assert
        Assert.That(
            async () => await _harness.ConsumeAsync(message),
            Throws.InstanceOf<InvalidOperationException>());
        Assert.That(_scope.Context.InvoiceOpenItems.Count(), Is.Zero);
    }

    [Test]
    public async Task Consumers_NeverMoveRowOutOfCancelledOrReversed()
    {
        // Arrange
        InvoiceConfirmedEvent cancelledInvoice = InvoiceConfirmedEventBuilder.Create().Build();
        InvoiceConfirmedEvent reversedInvoice = InvoiceConfirmedEventBuilder.Create()
            .WithDocumentNumber("SINV-2026-000002")
            .Build();
        await _harness.ConsumeAsync(cancelledInvoice);
        await _harness.ConsumeAsync(reversedInvoice);
        await _harness.ConsumeAsync(CancelledEventFor(cancelledInvoice.InvoiceId, "Void"));
        await _harness.ConsumeAsync(ReversedEventFor(reversedInvoice.InvoiceId));

        // Act
        await _harness.ConsumeAsync(PostedEventFor(cancelledInvoice.InvoiceId));
        await _harness.ConsumeAsync(PostedEventFor(reversedInvoice.InvoiceId));
        await _harness.ConsumeAsync(cancelledInvoice);
        await _harness.ConsumeAsync(reversedInvoice);

        // Assert
        InvoiceOpenItem cancelled = await LoadAsync(cancelledInvoice.InvoiceId);
        InvoiceOpenItem reversed = await LoadAsync(reversedInvoice.InvoiceId);
        Assert.Multiple(() =>
        {
            Assert.That(cancelled.InvoiceStatus, Is.EqualTo(nameof(InvoiceStatus.Cancelled)));
            Assert.That(reversed.InvoiceStatus, Is.EqualTo(nameof(InvoiceStatus.Reversed)));
        });
    }

    [Test]
    public async Task Consumers_NeverOverwriteLocallyOwnedSettledAmount()
    {
        // Arrange
        InvoiceConfirmedEvent confirmed = InvoiceConfirmedEventBuilder.Create().WithGrossTotal(1000.00m).Build();
        await _harness.ConsumeAsync(confirmed);
        InvoiceOpenItem tracked = await _scope.Context.InvoiceOpenItems
            .SingleAsync(item => item.InvoiceId == confirmed.InvoiceId, CancellationToken.None);
        tracked.SettledAmount = 400.00m;
        await _scope.Context.SaveChangesAsync(CancellationToken.None);

        // Act
        await _harness.ConsumeAsync(confirmed);
        await _harness.ConsumeAsync(PostedEventFor(confirmed.InvoiceId));
        await _harness.ConsumeAsync(ReversedEventFor(confirmed.InvoiceId));

        // Assert
        InvoiceOpenItem stored = await LoadAsync(confirmed.InvoiceId);
        Assert.Multiple(() =>
        {
            Assert.That(stored.SettledAmount, Is.EqualTo(400.00m));
            Assert.That(stored.InvoiceStatus, Is.EqualTo(nameof(InvoiceStatus.Reversed)));
        });
    }

    [Test]
    public async Task ProjectionLag_AllocateBeforeEventArrives_ReturnsInvoiceNotFound_Transient()
    {
        // Arrange
        PaymentAllocationTestHarness allocations = PaymentAllocationTestHarness.Build(_scope.Context);
        Payment payment = PaymentBuilder.Create().WithAmount(1000.00m).Build();
        _scope.Context.Payments.Add(payment);
        await _scope.Context.SaveChangesAsync(CancellationToken.None);
        InvoiceConfirmedEvent confirmed = InvoiceConfirmedEventBuilder.Create().WithGrossTotal(500.00m).Build();
        AllocatePaymentRequest request = new()
        {
            Items = [new AllocatePaymentItem { InvoiceId = confirmed.InvoiceId, AllocatedAmount = 100.00m }],
            RowVersion = Convert.ToBase64String(payment.RowVersion)
        };

        // Act
        Result<AllocatePaymentResultDto> beforeEvent =
            await allocations.Service.AllocateAsync(payment.Id, request, CancellationToken.None);
        await _harness.ConsumeAsync(confirmed);
        Result<AllocatePaymentResultDto> afterEvent =
            await allocations.Service.AllocateAsync(payment.Id, request, CancellationToken.None);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(beforeEvent.IsSuccess, Is.False);
            Assert.That(
                beforeEvent.ErrorCode,
                Is.EqualTo(PaymentErrorCodes.PAYMENT_ALLOCATION_INVOICE_NOT_FOUND),
                "projection lag is a legitimate transient 404, never a synchronous read-through");
            Assert.That(afterEvent.IsSuccess, Is.True, afterEvent.ErrorCode);
        });
    }

    /// <summary>Reads the persisted projection row without tracking.</summary>
    /// <param name="invoiceId">The mirrored invoice identifier.</param>
    /// <returns>The persisted open item.</returns>
    private Task<InvoiceOpenItem> LoadAsync(Guid invoiceId) => _scope.Context.InvoiceOpenItems
        .AsNoTracking()
        .SingleAsync(item => item.InvoiceId == invoiceId, CancellationToken.None);

    /// <summary>Seeds a payment and one allocation row pointing at the supplied invoice.</summary>
    /// <param name="invoiceId">The invoice the allocation matches.</param>
    /// <param name="amount">The allocated amount.</param>
    /// <returns>A task completing when the rows are persisted.</returns>
    private async Task SeedAllocationAsync(Guid invoiceId, decimal amount)
    {
        Payment payment = PaymentBuilder.Create().WithAmount(1000.00m).WithAllocatedAmount(amount).Build();
        payment.Allocations.Add(new PaymentAllocation
        {
            PaymentId = payment.Id,
            InvoiceId = invoiceId,
            AllocatedAmount = amount,
            BaseAllocatedAmount = amount,
            AllocatedAt = FixedTimeProvider.DefaultNow,
            AllocatedBy = StubCurrentUserAccessor.TestUserId,
            CorrelationId = StubCorrelationIdAccessor.DefaultCorrelationId
        });

        _scope.Context.Payments.Add(payment);
        await _scope.Context.SaveChangesAsync(CancellationToken.None);
    }

    /// <summary>Builds the invoice posting back-event for the supplied invoice.</summary>
    /// <param name="invoiceId">The invoice identifier.</param>
    /// <returns>The posting event.</returns>
    private static InvoicePostedEvent PostedEventFor(Guid invoiceId) => new()
    {
        MessageId = Guid.NewGuid(),
        CorrelationId = StubCorrelationIdAccessor.DefaultCorrelationId,
        OccurredAt = FixedTimeProvider.DefaultNow,
        InvoiceId = invoiceId,
        JournalEntryId = Guid.NewGuid(),
        JournalEntryNumber = "JE-2026-000001"
    };

    /// <summary>Builds the invoice cancellation event for the supplied invoice.</summary>
    /// <param name="invoiceId">The invoice identifier.</param>
    /// <param name="reason">The cancellation reason.</param>
    /// <returns>The cancellation event.</returns>
    private static InvoiceCancelledEvent CancelledEventFor(Guid invoiceId, string reason) => new()
    {
        MessageId = Guid.NewGuid(),
        CorrelationId = StubCorrelationIdAccessor.DefaultCorrelationId,
        OccurredAt = FixedTimeProvider.DefaultNow,
        InvoiceId = invoiceId,
        DocumentNumber = "SINV-2026-000001",
        Reason = reason
    };

    /// <summary>Builds the invoice reversal event for the supplied invoice.</summary>
    /// <param name="invoiceId">The invoice identifier.</param>
    /// <returns>The reversal event.</returns>
    private static InvoiceReversedEvent ReversedEventFor(Guid invoiceId) => new()
    {
        MessageId = Guid.NewGuid(),
        CorrelationId = StubCorrelationIdAccessor.DefaultCorrelationId,
        OccurredAt = FixedTimeProvider.DefaultNow,
        InvoiceId = invoiceId,
        DocumentNumber = "SINV-2026-000001",
        CorrectingInvoiceId = Guid.NewGuid(),
        Reason = "Reversed"
    };
}
