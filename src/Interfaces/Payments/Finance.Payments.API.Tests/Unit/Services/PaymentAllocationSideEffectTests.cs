using System.Reflection;
using Finance.Common.Enums;
using Finance.Common.ErrorCodes;
using Finance.Common.Results;
using Finance.Infrastructure.Audit.Models;
using Finance.Payments.API.Auditing;
using Finance.Payments.API.Services;
using Finance.Payments.API.Tests.Builders;
using Finance.Payments.API.Tests.Fixtures;
using Finance.Payments.DBModel.Models;
using Finance.ServiceModel.Events.Payments;
using Finance.ServiceModel.Payments;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;

namespace Finance.Payments.API.Tests.Unit.Services;

/// <summary>
/// Unit tests for the allocate / deallocate SIDE EFFECTS (SDD-PAY-002 §2.4, §2.6, §2.10, §2.11, §6.2): one audit
/// <c>Update</c> row per allocation written BEFORE the outbox row, the payment's pre-change matching snapshot as
/// <c>BeforeJson</c>, one event per row with its <c>OccurredAt</c> stamped INSIDE the transaction, the open item's
/// settled amount maintained in the same transaction, the workflow engine never invoked, and the optimistic
/// concurrency failures that write nothing.
/// </summary>
[TestFixture]
[Category("SDD-PAY-002")]
public sealed class PaymentAllocationSideEffectTests
{
    private SqlitePaymentsDbContextScope _scope = null!;
    private PaymentAllocationTestHarness _harness = null!;

    /// <summary>Creates a fresh SQLite-backed harness before each test.</summary>
    [SetUp]
    public void SetUp()
    {
        _scope = SqlitePaymentsDbContextFactory.Create();
        _harness = PaymentAllocationTestHarness.Build(_scope.Context);
    }

    /// <summary>Disposes the SQLite scope after each test.</summary>
    [TearDown]
    public void TearDown()
    {
        _scope.Dispose();
    }

    [Test]
    public async Task Allocate_WritesOneAuditUpdateRowPerAllocation_BeforeOutboxPublish()
    {
        // Arrange
        Payment payment = await SeedPaymentAsync(1000.00m);
        InvoiceOpenItem first = await SeedOpenItemAsync(500.00m);
        InvoiceOpenItem second = await SeedOpenItemAsync(500.00m, "SINV-2026-000002");

        // Act
        Result<AllocatePaymentResultDto> result = await AllocateAsync(
            payment, (first.InvoiceId, 200.00m), (second.InvoiceId, 300.00m));

        // Assert
        Assert.That(result.IsSuccess, Is.True, result.ErrorCode);
        Assert.Multiple(() =>
        {
            Assert.That(_harness.RecordedAudits, Has.Count.EqualTo(2), "one audit row per created allocation");
            Assert.That(_harness.Timeline[0], Is.InstanceOf<AuditEntry>());
            Assert.That(_harness.Timeline[1], Is.InstanceOf<AuditEntry>());
            Assert.That(_harness.Timeline[2], Is.InstanceOf<PaymentAllocatedEvent>());
            Assert.That(_harness.Timeline[3], Is.InstanceOf<PaymentAllocatedEvent>());
            Assert.That(
                _harness.RecordedAudits,
                Has.All.Property(nameof(AuditEntry.Operation)).EqualTo(AuditOperation.Update));
            Assert.That(
                _harness.RecordedAudits,
                Has.All.Property(nameof(AuditEntry.EventType))
                    .EqualTo(PaymentAuditEventTypes.PaymentAllocated));
            Assert.That(
                _harness.RecordedAudits,
                Has.All.Property(nameof(AuditEntry.EntityType))
                    .EqualTo(PaymentAuditEventTypes.EntityType));
        });
    }

    [Test]
    public async Task Allocate_AuditBeforeJson_IsPaymentPreChangeMatchingSnapshot_NonEmpty()
    {
        // Arrange
        Payment payment = await SeedPaymentAsync(1000.00m);
        InvoiceOpenItem openItem = await SeedOpenItemAsync(500.00m);

        // Act
        Result<AllocatePaymentResultDto> result = await AllocateAsync(payment, (openItem.InvoiceId, 250.00m));

        // Assert
        Assert.That(result.IsSuccess, Is.True, result.ErrorCode);
        AuditEntry audit = _harness.RecordedAudits.Single();
        Assert.Multiple(() =>
        {
            Assert.That(audit.BeforeJson, Is.Not.Null.And.Not.Empty);
            Assert.That(audit.BeforeJson, Does.Contain($"\"PaymentId\":\"{payment.Id}\""));
            Assert.That(audit.BeforeJson, Does.Contain("\"AllocatedAmount\":0"));
            Assert.That(audit.BeforeJson, Does.Contain("\"Allocations\":[]"));
            Assert.That(audit.AfterJson, Does.Contain("\"AllocatedAmount\":250.00"));
            Assert.That(audit.EntityId, Is.EqualTo(payment.Id.ToString()));
        });
    }

    [Test]
    public async Task Allocate_PublishesOnePaymentAllocatedEventPerAllocation_WithSettlementStatus()
    {
        // Arrange
        Payment payment = await SeedPaymentAsync(1000.00m);
        InvoiceOpenItem partial = await SeedOpenItemAsync(500.00m);
        InvoiceOpenItem full = await SeedOpenItemAsync(300.00m, "SINV-2026-000002");

        // Act
        Result<AllocatePaymentResultDto> result = await AllocateAsync(
            payment, (partial.InvoiceId, 200.00m), (full.InvoiceId, 300.00m));

        // Assert
        Assert.That(result.IsSuccess, Is.True, result.ErrorCode);
        IReadOnlyList<PaymentAllocatedEvent> published = _harness.EventsOf<PaymentAllocatedEvent>();
        Assert.Multiple(() =>
        {
            Assert.That(published, Has.Count.EqualTo(2));
            Assert.That(
                published.Single(message => message.InvoiceId == partial.InvoiceId).InvoiceSettlementStatus,
                Is.EqualTo(SettlementStatus.PartiallySettled));
            Assert.That(
                published.Single(message => message.InvoiceId == full.InvoiceId).InvoiceSettlementStatus,
                Is.EqualTo(SettlementStatus.Settled));
            Assert.That(
                published.Single(message => message.InvoiceId == full.InvoiceId).InvoiceSettledAmount,
                Is.EqualTo(300.00m));
            Assert.That(published.Select(message => message.MessageId).Distinct().Count(), Is.EqualTo(2));
            Assert.That(published, Has.All.Property(nameof(PaymentAllocatedEvent.PaymentId)).EqualTo(payment.Id));
        });
    }

    [Test]
    public async Task Allocate_StampsEventOccurredAtInsideAllocationTransaction_NotAtOutboxDispatch()
    {
        // Arrange
        Payment payment = await SeedPaymentAsync(1000.00m);
        InvoiceOpenItem openItem = await SeedOpenItemAsync(500.00m);
        DateTimeOffset allocationClock = new(2026, 6, 15, 11, 22, 33, TimeSpan.Zero);
        _harness.Clock.UtcNow = allocationClock;

        // Act
        Result<AllocatePaymentResultDto> allocated = await AllocateAsync(payment, (openItem.InvoiceId, 100.00m));
        _harness.Clock.UtcNow = allocationClock.AddHours(5);
        Result<DeallocatePaymentResultDto> released = await _harness.Service.DeallocateAsync(
            payment.Id,
            allocated.Value!.Allocations.Single().Id,
            Convert.ToBase64String(payment.RowVersion),
            reason: null,
            CancellationToken.None);

        // Assert
        PaymentAllocatedEvent allocatedEvent = _harness.EventsOf<PaymentAllocatedEvent>().Single();
        PaymentDeallocatedEvent deallocatedEvent = _harness.EventsOf<PaymentDeallocatedEvent>().Single();
        Assert.Multiple(() =>
        {
            Assert.That(released.IsSuccess, Is.True, released.ErrorCode);
            Assert.That(allocatedEvent.OccurredAt, Is.EqualTo(allocationClock));
            Assert.That(allocatedEvent.AllocatedAt, Is.EqualTo(allocationClock));
            Assert.That(deallocatedEvent.OccurredAt, Is.EqualTo(allocationClock.AddHours(5)));
            Assert.That(deallocatedEvent.DeallocatedAt, Is.EqualTo(allocationClock.AddHours(5)));
        });
    }

    [Test]
    public async Task Allocate_IncrementsInvoiceOpenItemSettledAmount_InSameTransaction()
    {
        // Arrange
        Payment payment = await SeedPaymentAsync(1000.00m);
        InvoiceOpenItem openItem = await SeedOpenItemAsync(500.00m);

        // Act
        Result<AllocatePaymentResultDto> result = await AllocateAsync(payment, (openItem.InvoiceId, 175.50m));

        // Assert
        Assert.That(result.IsSuccess, Is.True, result.ErrorCode);
        InvoiceOpenItem storedItem = await LoadOpenItemAsync(openItem.InvoiceId);
        Payment storedPayment = await LoadPaymentAsync(payment.Id);
        Assert.Multiple(() =>
        {
            Assert.That(storedItem.SettledAmount, Is.EqualTo(175.50m));
            Assert.That(storedItem.Outstanding, Is.EqualTo(324.50m));
            Assert.That(storedPayment.AllocatedAmount, Is.EqualTo(175.50m));
        });
    }

    [Test]
    public async Task Allocate_StampsCorrelationIdAndAllocatedBy_OnEveryRow()
    {
        // Arrange
        _harness.Correlation.CorrelationId = "allocate-correlation";
        Payment payment = await SeedPaymentAsync(1000.00m);
        InvoiceOpenItem first = await SeedOpenItemAsync(500.00m);
        InvoiceOpenItem second = await SeedOpenItemAsync(500.00m, "SINV-2026-000002");

        // Act
        Result<AllocatePaymentResultDto> result = await AllocateAsync(
            payment, (first.InvoiceId, 100.00m), (second.InvoiceId, 200.00m));

        // Assert
        Assert.That(result.IsSuccess, Is.True, result.ErrorCode);
        List<PaymentAllocation> rows = await _scope.Context.PaymentAllocations
            .AsNoTracking()
            .ToListAsync(CancellationToken.None);
        Assert.Multiple(() =>
        {
            Assert.That(rows, Has.Count.EqualTo(2));
            Assert.That(
                rows,
                Has.All.Property(nameof(PaymentAllocation.CorrelationId)).EqualTo("allocate-correlation"));
            Assert.That(
                rows,
                Has.All.Property(nameof(PaymentAllocation.AllocatedBy))
                    .EqualTo(StubCurrentUserAccessor.TestUserId));
            Assert.That(
                _harness.RecordedAudits,
                Has.All.Property(nameof(AuditEntry.CorrelationId)).EqualTo("allocate-correlation"));
            Assert.That(
                _harness.EventsOf<PaymentAllocatedEvent>(),
                Has.All.Property(nameof(PaymentAllocatedEvent.CorrelationId)).EqualTo("allocate-correlation"));
        });
    }

    [Test]
    public async Task Allocate_DoesNotCreateOrMutateJournalEntry()
    {
        // Arrange
        Guid journalEntryId = Guid.NewGuid();
        Payment payment = await SeedPaymentAsync(1000.00m, journalEntryId);
        InvoiceOpenItem openItem = await SeedOpenItemAsync(500.00m);

        // Act
        Result<AllocatePaymentResultDto> result = await AllocateAsync(payment, (openItem.InvoiceId, 100.00m));

        // Assert
        Assert.That(result.IsSuccess, Is.True, result.ErrorCode);
        Payment stored = await LoadPaymentAsync(payment.Id);
        Assert.Multiple(() =>
        {
            Assert.That(stored.JournalEntryId, Is.EqualTo(journalEntryId), "the posting link is untouched");
            Assert.That(_harness.EventsOf<PaymentConfirmedEvent>(), Is.Empty);
            Assert.That(_harness.EventsOf<PaymentReversedEvent>(), Is.Empty);
            Assert.That(_harness.PublishedEvents, Has.All.InstanceOf<PaymentAllocatedEvent>());
        });
    }

    [Test]
    public async Task Allocate_DoesNotInvokeWorkflowEngine_AndLeavesPaymentStatusUnchanged()
    {
        // Arrange
        Payment payment = await SeedPaymentAsync(1000.00m);
        InvoiceOpenItem openItem = await SeedOpenItemAsync(500.00m);
        IReadOnlyList<Type> constructorDependencies =
        [
            .. typeof(PaymentAllocationService)
                .GetConstructors(BindingFlags.Public | BindingFlags.Instance)
                .Single()
                .GetParameters()
                .Select(parameter => parameter.ParameterType)
        ];

        // Act
        Result<AllocatePaymentResultDto> result = await AllocateAsync(payment, (openItem.InvoiceId, 100.00m));

        // Assert
        Assert.That(result.IsSuccess, Is.True, result.ErrorCode);
        Payment storedPayment = await LoadPaymentAsync(payment.Id);
        Assert.Multiple(() =>
        {
            Assert.That(storedPayment.Status, Is.EqualTo(PaymentStatus.Confirmed));
            Assert.That(
                constructorDependencies.Any(type =>
                    type.IsGenericType && type.Name.StartsWith("IWorkflowEngine", StringComparison.Ordinal)),
                Is.False,
                "allocation is matching, not a lifecycle transition, so the engine is not a dependency");
            Assert.That(_scope.Context.PaymentStatusHistory.Count(), Is.Zero);
        });
    }

    [Test]
    public async Task Allocate_StaleRowVersion_ReturnsConcurrentModification_WritesNothing()
    {
        // Arrange
        Payment payment = await SeedPaymentAsync(1000.00m);
        InvoiceOpenItem openItem = await SeedOpenItemAsync(500.00m);
        AllocatePaymentRequest request = new()
        {
            Items = [new AllocatePaymentItem { InvoiceId = openItem.InvoiceId, AllocatedAmount = 100.00m }],
            RowVersion = Convert.ToBase64String(new byte[] { 9, 9, 9, 9, 9, 9, 9, 9 })
        };

        // Act
        Result<AllocatePaymentResultDto> result =
            await _harness.Service.AllocateAsync(payment.Id, request, CancellationToken.None);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo(CommonErrorCodes.CONCURRENT_MODIFICATION));
            Assert.That(_scope.Context.PaymentAllocations.Count(), Is.Zero);
        });
    }

    [Test]
    public async Task Allocate_ProjectionConsumerBumpedOpenItemRowVersion_ReturnsConcurrentModification()
    {
        // Arrange
        Payment payment = await SeedPaymentAsync(1000.00m);
        InvoiceOpenItem openItem = await SeedOpenItemAsync(500.00m);
        _scope.RowVersions.TamperOpenItemRowVersionOnce = true;

        // Act
        Result<AllocatePaymentResultDto> result = await AllocateAsync(payment, (openItem.InvoiceId, 100.00m));

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo(CommonErrorCodes.CONCURRENT_MODIFICATION));
            Assert.That(_scope.Context.PaymentAllocations.Count(), Is.Zero);
        });
    }

    [Test]
    public async Task Allocate_FailedValidation_PublishesNoEvent_AndWritesNoAuditRow()
    {
        // Arrange
        Payment payment = await SeedPaymentAsync(1000.00m);
        InvoiceOpenItem openItem = await SeedOpenItemAsync(100.00m);

        // Act
        Result<AllocatePaymentResultDto> result = await AllocateAsync(payment, (openItem.InvoiceId, 200.00m));

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.ErrorCode, Is.EqualTo(PaymentErrorCodes.PAYMENT_ALLOCATION_EXCEEDS_OUTSTANDING));
            Assert.That(_harness.PublishedEvents, Is.Empty);
            Assert.That(_harness.RecordedAudits, Is.Empty);
            Assert.That(_scope.Context.PaymentAllocations.Count(), Is.Zero);
        });
    }

    [Test]
    public async Task Deallocate_RemovesRow_AndDecrementsAllocatedAmountAndSettledAmount()
    {
        // Arrange
        Payment payment = await SeedPaymentAsync(1000.00m);
        InvoiceOpenItem openItem = await SeedOpenItemAsync(500.00m);
        Result<AllocatePaymentResultDto> allocated = await AllocateAsync(payment, (openItem.InvoiceId, 300.00m));
        Assert.That(allocated.IsSuccess, Is.True, allocated.ErrorCode);
        int allocationId = allocated.Value!.Allocations.Single().Id;

        // Act
        Result<DeallocatePaymentResultDto> result = await _harness.Service.DeallocateAsync(
            payment.Id, allocationId, allocated.Value.RowVersion, reason: null, CancellationToken.None);

        // Assert
        Assert.That(result.IsSuccess, Is.True, result.ErrorCode);
        Payment storedPayment = await LoadPaymentAsync(payment.Id);
        InvoiceOpenItem storedItem = await LoadOpenItemAsync(openItem.InvoiceId);
        Assert.Multiple(() =>
        {
            Assert.That(_scope.Context.PaymentAllocations.Count(), Is.Zero);
            Assert.That(storedPayment.AllocatedAmount, Is.EqualTo(0.00m));
            Assert.That(storedItem.SettledAmount, Is.EqualTo(0.00m));
            Assert.That(result.Value!.ReleasedAmount, Is.EqualTo(300.00m));
            Assert.That(
                result.Value.AffectedInvoice.SettlementStatus,
                Is.EqualTo(SettlementStatus.Unsettled));
        });
    }

    [Test]
    public async Task Deallocate_PublishesPaymentDeallocatedEvent_AfterAuditRow()
    {
        // Arrange
        Payment payment = await SeedPaymentAsync(1000.00m);
        InvoiceOpenItem openItem = await SeedOpenItemAsync(500.00m);
        Result<AllocatePaymentResultDto> allocated = await AllocateAsync(payment, (openItem.InvoiceId, 300.00m));
        _harness.Timeline.Clear();

        // Act
        Result<DeallocatePaymentResultDto> result = await _harness.Service.DeallocateAsync(
            payment.Id,
            allocated.Value!.Allocations.Single().Id,
            allocated.Value.RowVersion,
            reason: null,
            CancellationToken.None);

        // Assert
        Assert.That(result.IsSuccess, Is.True, result.ErrorCode);
        Assert.That(_harness.Timeline, Has.Count.EqualTo(2));
        AuditEntry audit = (AuditEntry)_harness.Timeline[0];
        Assert.Multiple(() =>
        {
            Assert.That(_harness.Timeline[1], Is.InstanceOf<PaymentDeallocatedEvent>());
            Assert.That(audit.EventType, Is.EqualTo(PaymentAuditEventTypes.PaymentDeallocated));
            Assert.That(audit.Operation, Is.EqualTo(AuditOperation.Update));
            Assert.That(audit.BeforeJson, Does.Contain("\"RemovedAllocation\""));
        });
    }

    [Test]
    public async Task Deallocate_WithOptionalReason_PersistsReasonOnAuditRow()
    {
        // Arrange
        Payment payment = await SeedPaymentAsync(1000.00m);
        InvoiceOpenItem openItem = await SeedOpenItemAsync(500.00m);
        Result<AllocatePaymentResultDto> allocated = await AllocateAsync(payment, (openItem.InvoiceId, 300.00m));
        _harness.Timeline.Clear();

        // Act
        Result<DeallocatePaymentResultDto> result = await _harness.Service.DeallocateAsync(
            payment.Id,
            allocated.Value!.Allocations.Single().Id,
            allocated.Value.RowVersion,
            "Matched the wrong invoice",
            CancellationToken.None);

        // Assert
        Assert.That(result.IsSuccess, Is.True, result.ErrorCode);
        Assert.That(_harness.RecordedAudits.Single().Reason, Is.EqualTo("Matched the wrong invoice"));
    }

    [Test]
    public async Task Deallocate_AllocationOfAnotherPayment_ReturnsPaymentAllocationNotFound()
    {
        // Arrange
        Payment owner = await SeedPaymentAsync(1000.00m);
        Payment other = await SeedPaymentAsync(1000.00m);
        InvoiceOpenItem openItem = await SeedOpenItemAsync(500.00m);
        Result<AllocatePaymentResultDto> allocated = await AllocateAsync(owner, (openItem.InvoiceId, 200.00m));
        int foreignAllocationId = allocated.Value!.Allocations.Single().Id;

        // Act
        Result<DeallocatePaymentResultDto> result = await _harness.Service.DeallocateAsync(
            other.Id,
            foreignAllocationId,
            Convert.ToBase64String(other.RowVersion),
            reason: null,
            CancellationToken.None);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo(PaymentErrorCodes.PAYMENT_ALLOCATION_NOT_FOUND));
            Assert.That(_scope.Context.PaymentAllocations.Count(), Is.EqualTo(1), "never a cross-payment delete");
        });
    }

    [Test]
    public async Task Deallocate_CancelledPayment_ReturnsPaymentNotAllocatable_KeepsRow()
    {
        // Arrange — an allocated Cancelled payment is UNREACHABLE through the v1 paths, so it is seeded directly.
        InvoiceOpenItem openItem = await SeedOpenItemAsync(500.00m);
        Payment cancelled = PaymentBuilder.Create()
            .WithStatus(PaymentStatus.Cancelled)
            .WithAmount(1000.00m)
            .WithAllocatedAmount(300.00m)
            .Build();
        cancelled.Allocations.Add(new PaymentAllocation
        {
            PaymentId = cancelled.Id,
            InvoiceId = openItem.InvoiceId,
            AllocatedAmount = 300.00m,
            BaseAllocatedAmount = 300.00m,
            AllocatedAt = FixedTimeProvider.DefaultNow,
            AllocatedBy = StubCurrentUserAccessor.TestUserId,
            CorrelationId = StubCorrelationIdAccessor.DefaultCorrelationId
        });
        _scope.Context.Payments.Add(cancelled);
        await _scope.Context.SaveChangesAsync(CancellationToken.None);
        int allocationId = cancelled.Allocations.Single().Id;

        // Act
        Result<DeallocatePaymentResultDto> result = await _harness.Service.DeallocateAsync(
            cancelled.Id,
            allocationId,
            Convert.ToBase64String(cancelled.RowVersion),
            reason: null,
            CancellationToken.None);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo(PaymentErrorCodes.PAYMENT_NOT_ALLOCATABLE));
            Assert.That(_scope.Context.PaymentAllocations.Count(), Is.EqualTo(1), "the row is kept for audit");
        });
    }

    [Test]
    public async Task Deallocate_NeverDrivesAllocatedAmountOrSettledAmountBelowZero()
    {
        // Arrange
        Payment payment = await SeedPaymentAsync(1000.00m);
        InvoiceOpenItem openItem = await SeedOpenItemAsync(500.00m);
        Result<AllocatePaymentResultDto> allocated = await AllocateAsync(payment, (openItem.InvoiceId, 500.00m));
        int allocationId = allocated.Value!.Allocations.Single().Id;
        Result<DeallocatePaymentResultDto> first = await _harness.Service.DeallocateAsync(
            payment.Id, allocationId, allocated.Value.RowVersion, reason: null, CancellationToken.None);
        Assert.That(first.IsSuccess, Is.True, first.ErrorCode);

        // Act
        Result<DeallocatePaymentResultDto> second = await _harness.Service.DeallocateAsync(
            payment.Id, allocationId, first.Value!.RowVersion, reason: null, CancellationToken.None);

        // Assert
        Payment storedPayment = await LoadPaymentAsync(payment.Id);
        InvoiceOpenItem storedItem = await LoadOpenItemAsync(openItem.InvoiceId);
        Assert.Multiple(() =>
        {
            Assert.That(second.IsSuccess, Is.False);
            Assert.That(second.ErrorCode, Is.EqualTo(PaymentErrorCodes.PAYMENT_ALLOCATION_NOT_FOUND));
            Assert.That(storedPayment.AllocatedAmount, Is.EqualTo(0.00m));
            Assert.That(storedItem.SettledAmount, Is.EqualTo(0.00m));
        });
    }

    /// <summary>Persists a confirmed payment of the supplied amount.</summary>
    /// <param name="amount">The payment amount.</param>
    /// <param name="journalEntryId">An optional linked journal entry.</param>
    /// <returns>The persisted payment.</returns>
    private async Task<Payment> SeedPaymentAsync(decimal amount, Guid? journalEntryId = null)
    {
        Payment payment = PaymentBuilder.Create()
            .WithAmount(amount)
            .WithJournalEntryId(journalEntryId)
            .Build();
        _scope.Context.Payments.Add(payment);
        await _scope.Context.SaveChangesAsync(CancellationToken.None);
        return payment;
    }

    /// <summary>Persists a confirmed, unsettled sale-invoice open item.</summary>
    /// <param name="grossTotal">The invoice gross total.</param>
    /// <param name="documentNumber">The invoice document number.</param>
    /// <returns>The persisted open item.</returns>
    private async Task<InvoiceOpenItem> SeedOpenItemAsync(
        decimal grossTotal,
        string documentNumber = "SINV-2026-000001")
    {
        InvoiceOpenItem openItem = InvoiceOpenItemBuilder.Create()
            .WithGrossTotal(grossTotal)
            .WithDocumentNumber(documentNumber)
            .Build();
        _scope.Context.InvoiceOpenItems.Add(openItem);
        await _scope.Context.SaveChangesAsync(CancellationToken.None);
        return openItem;
    }

    /// <summary>Allocates the supplied items against the payment using its current concurrency token.</summary>
    /// <param name="payment">The payment to match.</param>
    /// <param name="items">The invoice/amount pairs to request.</param>
    /// <returns>The allocation result.</returns>
    private Task<Result<AllocatePaymentResultDto>> AllocateAsync(
        Payment payment,
        params (Guid InvoiceId, decimal Amount)[] items)
    {
        AllocatePaymentRequest request = new()
        {
            Items =
            [
                .. items.Select(item => new AllocatePaymentItem
                {
                    InvoiceId = item.InvoiceId,
                    AllocatedAmount = item.Amount
                })
            ],
            RowVersion = Convert.ToBase64String(payment.RowVersion)
        };

        return _harness.Service.AllocateAsync(payment.Id, request, CancellationToken.None);
    }

    /// <summary>Reads the persisted payment without tracking.</summary>
    /// <param name="id">The payment identifier.</param>
    /// <returns>The persisted payment.</returns>
    private Task<Payment> LoadPaymentAsync(Guid id) => _scope.Context.Payments
        .AsNoTracking()
        .SingleAsync(payment => payment.Id == id, CancellationToken.None);

    /// <summary>Reads the persisted open item without tracking.</summary>
    /// <param name="invoiceId">The invoice identifier.</param>
    /// <returns>The persisted open item.</returns>
    private Task<InvoiceOpenItem> LoadOpenItemAsync(Guid invoiceId) => _scope.Context.InvoiceOpenItems
        .AsNoTracking()
        .SingleAsync(item => item.InvoiceId == invoiceId, CancellationToken.None);
}
