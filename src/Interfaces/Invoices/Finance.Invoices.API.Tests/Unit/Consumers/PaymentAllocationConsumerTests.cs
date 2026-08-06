using Finance.Common.Enums;
using Finance.Infrastructure.Audit.Models;
using Finance.Invoices.API.Auditing;
using Finance.Invoices.API.Tests.Builders;
using Finance.Invoices.API.Tests.Fixtures;
using Finance.Invoices.DBModel.Models;
using Finance.ServiceModel.Events.Payments;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NUnit.Framework;

namespace Finance.Invoices.API.Tests.Unit.Consumers;

/// <summary>
/// Unit tests for the inbound settlement handshake — <c>PaymentAllocatedEventConsumer</c>,
/// <c>PaymentDeallocatedEventConsumer</c>, and the real
/// <see cref="Finance.Invoices.API.Services.InvoiceSettlementService"/> behind them (SDD-INV-001 §2.14/§2.15,
/// §6.7). They run fully offline against a SQLite in-memory
/// <see cref="Finance.Invoices.DBModel.InvoicesDbContext"/> with a Moq'd <c>ConsumeContext&lt;T&gt;</c>.
/// <para>The load-bearing test is the ORDERED MIRROR: absolute assignment makes a REPLAY of the same message
/// harmless but is NOT commutative across DIFFERENT messages, and the <c>RowVersion</c> serialization is itself a
/// reordering mechanism — so a strictly older event MUST be dropped silently and successfully, or the invoice
/// freezes at the lower figure permanently while the Payments service holds the higher total and no further event
/// ever corrects it (§2.14 worked example). An event with an EQUAL <c>OccurredAt</c> MUST be applied.</para>
/// <para>Settlement is ORTHOGONAL to the lifecycle: a fully-settled invoice stays <c>Posted</c>, no workflow
/// transition fires, no status-history row is appended — and a LATER allocation event IS applied to a terminal
/// invoice so the orphan-repair deallocation of §2.6 can zero the mirror.</para>
/// </summary>
[TestFixture]
[Category("SDD-INV-001")]
[Category("SDD-PAY-002")]
public sealed class PaymentAllocationConsumerTests
{
    private static readonly DateTimeOffset Earlier = PaymentAllocatedEventBuilder.BaseInstant;
    private static readonly DateTimeOffset Later = PaymentAllocatedEventBuilder.BaseInstant.AddMinutes(5);

    private SqliteInvoicesDbContextScope _scope = null!;
    private InvoiceSettlementTestHarness _harness = null!;

    /// <summary>Creates a fresh SQLite-backed settlement harness before each test.</summary>
    [SetUp]
    public void SetUp()
    {
        _scope = SqliteInvoicesDbContextFactory.Create();
        _harness = InvoiceSettlementTestHarness.Build(_scope.Context);
    }

    /// <summary>Disposes the SQLite scope after each test.</summary>
    [TearDown]
    public void TearDown()
    {
        _scope.Dispose();
    }

    // ---- The ordered mirror (SDD-INV-001 §2.14/§2.15) ----

    /// <summary>
    /// THE ORDERED MIRROR (§2.14 worked example, §2.15): the authoritative 1000.00 event applies first, then the
    /// 300.00 event carrying an EARLIER OccurredAt arrives — the loser of the RowVersion race retrying. The older
    /// event MUST be dropped, so the invoice keeps 1000.00 / Settled. Without the ordering token the mirror would
    /// regress to 300.00 / PartiallySettled permanently while finance_payments held 1000.00.
    /// </summary>
    [Test]
    public async Task AllocationConsumer_EventsAppliedInReverseOrder_HigherAuthoritativeTotalSurvives()
    {
        // Arrange
        Guid invoiceId = await SeedInvoiceAsync(InvoiceSeedBuilder.Create().WithGrossTotal(1000.00m));
        PaymentAllocatedEvent newerFullSettlement = PaymentAllocatedEventBuilder.Create()
            .WithInvoiceId(invoiceId)
            .WithOccurredAt(Later)
            .WithSettlement(700.00m, 1000.00m, SettlementStatus.Settled)
            .Build();
        PaymentAllocatedEvent olderPartialSettlement = PaymentAllocatedEventBuilder.Create()
            .WithInvoiceId(invoiceId)
            .WithOccurredAt(Earlier)
            .WithSettlement(300.00m, 300.00m, SettlementStatus.PartiallySettled)
            .Build();

        // Act
        await _harness.ConsumeAsync(newerFullSettlement);
        await _harness.ConsumeAsync(olderPartialSettlement);

        // Assert
        Invoice reloaded = await ReloadAsync(invoiceId);
        Assert.Multiple(() =>
        {
            Assert.That(reloaded.SettledAmount, Is.EqualTo(1000.00m));
            Assert.That(reloaded.SettlementStatus, Is.EqualTo(SettlementStatus.Settled));
            Assert.That(reloaded.LastSettlementAppliedAt, Is.EqualTo(Later));
        });
    }

    /// <summary>
    /// A strictly older event is dropped SILENTLY and SUCCESSFULLY — no column write, no audit row, no throw, no
    /// dead letter (§2.15 step 2).
    /// </summary>
    [Test]
    public async Task AllocationConsumer_StaleOccurredAt_SkipsSilently_WritesNoAuditRow_DoesNotThrow()
    {
        // Arrange
        Guid invoiceId = await SeedInvoiceAsync(InvoiceSeedBuilder.Create()
            .WithGrossTotal(1000.00m)
            .WithSettlement(1000.00m, SettlementStatus.Settled, Later));
        PaymentAllocatedEvent stale = PaymentAllocatedEventBuilder.Create()
            .WithInvoiceId(invoiceId)
            .WithOccurredAt(Earlier)
            .WithSettlement(300.00m, 300.00m, SettlementStatus.PartiallySettled)
            .Build();

        // Act
        await _harness.ConsumeAsync(stale);

        // Assert
        Invoice reloaded = await ReloadAsync(invoiceId);
        Assert.Multiple(() =>
        {
            Assert.That(reloaded.SettledAmount, Is.EqualTo(1000.00m));
            Assert.That(reloaded.SettlementStatus, Is.EqualTo(SettlementStatus.Settled));
            Assert.That(reloaded.LastSettlementAppliedAt, Is.EqualTo(Later));
            Assert.That(_harness.RecordedAudits, Is.Empty);
        });
    }

    /// <summary>
    /// An event whose OccurredAt EQUALS the stored token is APPLIED — skipping on equality would drop a genuine
    /// second event sharing a timestamp, and absolute assignment makes re-applying harmless (§2.15).
    /// </summary>
    [Test]
    public async Task AllocationConsumer_EqualOccurredAt_IsApplied()
    {
        // Arrange
        Guid invoiceId = await SeedInvoiceAsync(InvoiceSeedBuilder.Create()
            .WithGrossTotal(1000.00m)
            .WithSettlement(300.00m, SettlementStatus.PartiallySettled, Earlier));
        PaymentAllocatedEvent sameInstant = PaymentAllocatedEventBuilder.Create()
            .WithInvoiceId(invoiceId)
            .WithOccurredAt(Earlier)
            .WithSettlement(700.00m, 1000.00m, SettlementStatus.Settled)
            .Build();

        // Act
        await _harness.ConsumeAsync(sameInstant);

        // Assert
        Invoice reloaded = await ReloadAsync(invoiceId);
        Assert.Multiple(() =>
        {
            Assert.That(reloaded.SettledAmount, Is.EqualTo(1000.00m));
            Assert.That(reloaded.SettlementStatus, Is.EqualTo(SettlementStatus.Settled));
        });
    }

    /// <summary>
    /// A NULL ordering token means no settlement event has been applied yet, so the first event always applies
    /// (§2.14).
    /// </summary>
    [Test]
    public async Task AllocationConsumer_NullLastSettlementAppliedAt_AppliesFirstEvent()
    {
        // Arrange
        Guid invoiceId = await SeedInvoiceAsync(InvoiceSeedBuilder.Create().WithGrossTotal(1000.00m));
        PaymentAllocatedEvent first = PaymentAllocatedEventBuilder.Create()
            .WithInvoiceId(invoiceId)
            .WithOccurredAt(Earlier)
            .WithSettlement(250.00m, 250.00m, SettlementStatus.PartiallySettled)
            .Build();

        // Act
        await _harness.ConsumeAsync(first);

        // Assert
        Invoice reloaded = await ReloadAsync(invoiceId);
        Assert.Multiple(() =>
        {
            Assert.That(reloaded.SettledAmount, Is.EqualTo(250.00m));
            Assert.That(reloaded.LastSettlementAppliedAt, Is.EqualTo(Earlier));
        });
    }

    /// <summary>
    /// The ordering token is stamped from the EVENT's OccurredAt, never the row's write time, so it stays
    /// comparable against the next event's token (§2.15 step 5).
    /// </summary>
    [Test]
    public async Task AllocationConsumer_StampsLastSettlementAppliedAt_FromEventOccurredAt_NotUtcNow()
    {
        // Arrange
        Guid invoiceId = await SeedInvoiceAsync(InvoiceSeedBuilder.Create().WithGrossTotal(1000.00m));
        PaymentAllocatedEvent allocation = PaymentAllocatedEventBuilder.Create()
            .WithInvoiceId(invoiceId)
            .WithOccurredAt(Later)
            .WithSettlement(400.00m, 400.00m, SettlementStatus.PartiallySettled)
            .Build();

        // Act
        await _harness.ConsumeAsync(allocation);

        // Assert
        Invoice reloaded = await ReloadAsync(invoiceId);
        Assert.Multiple(() =>
        {
            Assert.That(reloaded.LastSettlementAppliedAt, Is.EqualTo(allocation.OccurredAt));
            Assert.That(reloaded.LastSettlementAppliedAt, Is.LessThan(DateTimeOffset.UtcNow));
        });
    }

    /// <summary>
    /// A stale DEALLOCATION never restores an older settled amount over a newer allocation (§2.15).
    /// </summary>
    [Test]
    public async Task DeallocationConsumer_StaleOccurredAt_DoesNotRestoreOlderSettledAmount()
    {
        // Arrange
        Guid invoiceId = await SeedInvoiceAsync(InvoiceSeedBuilder.Create()
            .WithGrossTotal(1000.00m)
            .WithSettlement(1000.00m, SettlementStatus.Settled, Later));
        PaymentDeallocatedEvent staleRelease = PaymentDeallocatedEventBuilder.Create()
            .WithInvoiceId(invoiceId)
            .WithOccurredAt(Earlier)
            .WithSettlement(1000.00m, 0.00m, SettlementStatus.Unsettled)
            .Build();

        // Act
        await _harness.ConsumeAsync(staleRelease);

        // Assert
        Invoice reloaded = await ReloadAsync(invoiceId);
        Assert.Multiple(() =>
        {
            Assert.That(reloaded.SettledAmount, Is.EqualTo(1000.00m));
            Assert.That(reloaded.SettlementStatus, Is.EqualTo(SettlementStatus.Settled));
        });
    }

    // ---- Absolute assignment & derivation (SDD-INV-001 §2.14/§2.15) ----

    /// <summary>
    /// The first allocation assigns the event's authoritative absolute amount and derives PartiallySettled
    /// (§2.14, §2.15 step 4).
    /// </summary>
    [Test]
    public async Task AllocationConsumer_FirstEvent_SetsSettledAmount_AndDerivesPartiallySettled()
    {
        // Arrange
        Guid invoiceId = await SeedInvoiceAsync(InvoiceSeedBuilder.Create().WithGrossTotal(1000.00m));
        PaymentAllocatedEvent allocation = PaymentAllocatedEventBuilder.Create()
            .WithInvoiceId(invoiceId)
            .WithOccurredAt(Earlier)
            .WithSettlement(300.00m, 300.00m, SettlementStatus.PartiallySettled)
            .Build();

        // Act
        await _harness.ConsumeAsync(allocation);

        // Assert
        Invoice reloaded = await ReloadAsync(invoiceId);
        Assert.Multiple(() =>
        {
            Assert.That(reloaded.SettledAmount, Is.EqualTo(300.00m));
            Assert.That(reloaded.SettlementStatus, Is.EqualTo(SettlementStatus.PartiallySettled));
        });
    }

    /// <summary>
    /// A settled amount equal to the gross total derives Settled while the LIFECYCLE status remains Posted —
    /// Settled is not a lifecycle state (§2.14, §2.13 "Full settlement of a posted invoice").
    /// </summary>
    [Test]
    public async Task AllocationConsumer_FullGrossTotal_DerivesSettled_AndLeavesStatusPosted()
    {
        // Arrange
        Guid invoiceId = await SeedInvoiceAsync(InvoiceSeedBuilder.Create().WithGrossTotal(1000.00m));
        PaymentAllocatedEvent allocation = PaymentAllocatedEventBuilder.Create()
            .WithInvoiceId(invoiceId)
            .WithOccurredAt(Earlier)
            .WithSettlement(1000.00m, 1000.00m, SettlementStatus.Settled)
            .Build();

        // Act
        await _harness.ConsumeAsync(allocation);

        // Assert
        Invoice reloaded = await ReloadAsync(invoiceId);
        Assert.Multiple(() =>
        {
            Assert.That(reloaded.SettlementStatus, Is.EqualTo(SettlementStatus.Settled));
            Assert.That(reloaded.Status, Is.EqualTo(InvoiceStatus.Posted));
            Assert.That(reloaded.StatusHistory, Is.Empty);
        });
    }

    /// <summary>
    /// A duplicate event (the same absolute amount replayed) never double-counts cash — the amount is ASSIGNED,
    /// not incremented (§2.13 "Replayed allocation event", §2.15).
    /// </summary>
    [Test]
    public async Task AllocationConsumer_DuplicateEvent_IsNoOp_DoesNotDoubleCountSettledAmount()
    {
        // Arrange
        Guid invoiceId = await SeedInvoiceAsync(InvoiceSeedBuilder.Create().WithGrossTotal(1000.00m));
        PaymentAllocatedEvent allocation = PaymentAllocatedEventBuilder.Create()
            .WithInvoiceId(invoiceId)
            .WithOccurredAt(Earlier)
            .WithSettlement(400.00m, 400.00m, SettlementStatus.PartiallySettled)
            .Build();

        // Act
        await _harness.ConsumeAsync(allocation);
        await _harness.ConsumeAsync(allocation);

        // Assert
        Invoice reloaded = await ReloadAsync(invoiceId);
        Assert.That(reloaded.SettledAmount, Is.EqualTo(400.00m));
    }

    /// <summary>
    /// Two allocations in order assign the event's ABSOLUTE running total rather than summing the per-allocation
    /// deltas, which is what makes a post-TTL replay harmless (§2.15).
    /// </summary>
    [Test]
    public async Task AllocationConsumer_RecomputesFromEventAbsoluteValue_NotByDelta()
    {
        // Arrange
        Guid invoiceId = await SeedInvoiceAsync(InvoiceSeedBuilder.Create().WithGrossTotal(1000.00m));
        PaymentAllocatedEvent firstAllocation = PaymentAllocatedEventBuilder.Create()
            .WithInvoiceId(invoiceId)
            .WithOccurredAt(Earlier)
            .WithSettlement(300.00m, 300.00m, SettlementStatus.PartiallySettled)
            .Build();
        PaymentAllocatedEvent secondAllocation = PaymentAllocatedEventBuilder.Create()
            .WithInvoiceId(invoiceId)
            .WithOccurredAt(Later)
            .WithSettlement(200.00m, 500.00m, SettlementStatus.PartiallySettled)
            .Build();

        // Act
        await _harness.ConsumeAsync(firstAllocation);
        await _harness.ConsumeAsync(secondAllocation);

        // Assert
        Invoice reloaded = await ReloadAsync(invoiceId);
        Assert.That(reloaded.SettledAmount, Is.EqualTo(500.00m));
    }

    /// <summary>
    /// A full release derives Unsettled from the event's authoritative 0.00 (§2.15).
    /// </summary>
    [Test]
    public async Task DeallocationConsumer_ReleasesAmount_DerivesUnsettled_WhenFullyReleased()
    {
        // Arrange
        Guid invoiceId = await SeedInvoiceAsync(InvoiceSeedBuilder.Create()
            .WithGrossTotal(1000.00m)
            .WithSettlement(1000.00m, SettlementStatus.Settled, Earlier));
        PaymentDeallocatedEvent release = PaymentDeallocatedEventBuilder.Create()
            .WithInvoiceId(invoiceId)
            .WithOccurredAt(Later)
            .WithSettlement(1000.00m, 0.00m, SettlementStatus.Unsettled)
            .Build();

        // Act
        await _harness.ConsumeAsync(release);

        // Assert
        Invoice reloaded = await ReloadAsync(invoiceId);
        Assert.Multiple(() =>
        {
            Assert.That(reloaded.SettledAmount, Is.EqualTo(0.00m));
            Assert.That(reloaded.SettlementStatus, Is.EqualTo(SettlementStatus.Unsettled));
        });
    }

    /// <summary>
    /// The LOCAL derivation is authoritative for this database: when the publisher's reported status disagrees,
    /// the locally derived value is persisted and the remote one is not trusted (§2.15 step 4).
    /// </summary>
    [Test]
    public async Task AllocationConsumer_EventStatusDisagreesWithLocalDerivation_KeepsLocallyDerivedValue()
    {
        // Arrange
        Guid invoiceId = await SeedInvoiceAsync(InvoiceSeedBuilder.Create().WithGrossTotal(1000.00m));
        PaymentAllocatedEvent disagreeing = PaymentAllocatedEventBuilder.Create()
            .WithInvoiceId(invoiceId)
            .WithOccurredAt(Earlier)
            .WithSettlement(1000.00m, 1000.00m, SettlementStatus.PartiallySettled)
            .Build();

        // Act
        await _harness.ConsumeAsync(disagreeing);

        // Assert
        Invoice reloaded = await ReloadAsync(invoiceId);
        Assert.Multiple(() =>
        {
            Assert.That(reloaded.SettlementStatus, Is.EqualTo(SettlementStatus.Settled));
            Assert.That(reloaded.SettlementStatus, Is.Not.EqualTo(disagreeing.InvoiceSettlementStatus));
        });
    }

    /// <summary>
    /// The other half of §2.15 step 4: keeping the local value SILENTLY would hide a real cross-service
    /// divergence, so the service MUST emit a structured warning naming the reported and the derived status.
    /// </summary>
    [Test]
    public async Task AllocationConsumer_EventStatusDisagreesWithLocalDerivation_LogsStructuredWarning()
    {
        // Arrange
        Guid invoiceId = await SeedInvoiceAsync(InvoiceSeedBuilder.Create().WithGrossTotal(1000.00m));
        PaymentAllocatedEvent disagreeing = PaymentAllocatedEventBuilder.Create()
            .WithInvoiceId(invoiceId)
            .WithOccurredAt(Earlier)
            .WithSettlement(1000.00m, 1000.00m, SettlementStatus.PartiallySettled)
            .Build();

        // Act
        await _harness.ConsumeAsync(disagreeing);

        // Assert
        IReadOnlyList<RecordedLogEntry> warnings =
            [.. _harness.Logger.Entries.Where(entry => entry.Level == LogLevel.Warning)];
        Assert.That(warnings, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(warnings[0].Message, Does.Contain("Settlement derivation disagreement"));
            Assert.That(warnings[0].Message, Does.Contain(invoiceId.ToString()));
            Assert.That(warnings[0].Message, Does.Contain(nameof(SettlementStatus.PartiallySettled)));
            Assert.That(warnings[0].Message, Does.Contain(nameof(SettlementStatus.Settled)));
            Assert.That(warnings[0].Message, Does.Contain("Keeping the local value"));
        });
    }

    /// <summary>
    /// The warning is a DISAGREEMENT signal, not a per-event trace: when the publisher's reported status matches
    /// the local derivation nothing is warned about (§2.15 step 4).
    /// </summary>
    [Test]
    public async Task AllocationConsumer_EventStatusAgreesWithLocalDerivation_LogsNoWarning()
    {
        // Arrange
        Guid invoiceId = await SeedInvoiceAsync(InvoiceSeedBuilder.Create().WithGrossTotal(1000.00m));
        PaymentAllocatedEvent agreeing = PaymentAllocatedEventBuilder.Create()
            .WithInvoiceId(invoiceId)
            .WithOccurredAt(Earlier)
            .WithSettlement(1000.00m, 1000.00m, SettlementStatus.Settled)
            .Build();

        // Act
        await _harness.ConsumeAsync(agreeing);

        // Assert
        Assert.That(
            _harness.Logger.Entries.Where(entry => entry.Level == LogLevel.Warning),
            Is.Empty);
    }

    // ---- Defensive invariants (SDD-INV-001 §2.14) ----

    /// <summary>
    /// An amount above the gross total makes the consumer THROW so MassTransit retries and finally dead-letters —
    /// never a clamped, truncated, or silently persisted over-value (§2.14 invariant, §2.13).
    /// </summary>
    [Test]
    public async Task AllocationConsumer_WouldExceedGrossTotal_Throws_NeverClamps()
    {
        // Arrange
        Guid invoiceId = await SeedInvoiceAsync(InvoiceSeedBuilder.Create().WithGrossTotal(1000.00m));
        PaymentAllocatedEvent overCeiling = PaymentAllocatedEventBuilder.Create()
            .WithInvoiceId(invoiceId)
            .WithOccurredAt(Earlier)
            .WithSettlement(1000.01m, 1000.01m, SettlementStatus.Settled)
            .Build();

        // Act & Assert
        Assert.That(
            async () => await _harness.ConsumeAsync(overCeiling),
            Throws.TypeOf<InvalidOperationException>());
        Invoice reloaded = await ReloadAsync(invoiceId);
        Assert.Multiple(() =>
        {
            Assert.That(reloaded.SettledAmount, Is.EqualTo(0.00m));
            Assert.That(reloaded.LastSettlementAppliedAt, Is.Null);
        });
    }

    /// <summary>
    /// A negative authoritative amount is rejected the same way as an over-ceiling one (§2.14 invariant).
    /// </summary>
    [Test]
    public async Task AllocationConsumer_NegativeSettledAmount_Throws_NeverClamps()
    {
        // Arrange
        Guid invoiceId = await SeedInvoiceAsync(InvoiceSeedBuilder.Create().WithGrossTotal(1000.00m));
        PaymentAllocatedEvent negative = PaymentAllocatedEventBuilder.Create()
            .WithInvoiceId(invoiceId)
            .WithOccurredAt(Earlier)
            .WithSettlement(-1.00m, -1.00m, SettlementStatus.Unsettled)
            .Build();

        // Act & Assert
        Assert.That(
            async () => await _harness.ConsumeAsync(negative),
            Throws.TypeOf<InvalidOperationException>());
        Invoice reloaded = await ReloadAsync(invoiceId);
        Assert.That(reloaded.SettledAmount, Is.EqualTo(0.00m));
    }

    /// <summary>
    /// An unknown invoice id makes the consumer throw so MassTransit retries then dead-letters, and no
    /// placeholder invoice is created (§2.15 step 1, §2.13).
    /// </summary>
    [Test]
    public async Task AllocationConsumer_UnknownInvoiceId_Throws_ForRetry_CreatesNoPlaceholderInvoice()
    {
        // Arrange
        PaymentAllocatedEvent orphan = PaymentAllocatedEventBuilder.Create()
            .WithInvoiceId(Guid.NewGuid())
            .WithOccurredAt(Earlier)
            .WithSettlement(100.00m, 100.00m, SettlementStatus.PartiallySettled)
            .Build();

        // Act & Assert
        Assert.That(
            async () => await _harness.ConsumeAsync(orphan),
            Throws.TypeOf<InvalidOperationException>());
        int invoiceCount = await _scope.Context.Invoices.CountAsync(CancellationToken.None);
        Assert.That(invoiceCount, Is.Zero);
    }

    // ---- Audit trail & orthogonality (SDD-INV-001 §2.14/§2.15) ----

    /// <summary>
    /// An applied event writes exactly one audit Update row whose snapshots both include the ordering token, so
    /// the trail shows which event won, carrying the EVENT's correlation id (§2.15 step 6).
    /// </summary>
    [Test]
    public async Task AllocationConsumer_RecordsAuditUpdate_WithOrderingTokenInBothSnapshots()
    {
        // Arrange
        Guid invoiceId = await SeedInvoiceAsync(InvoiceSeedBuilder.Create().WithGrossTotal(1000.00m));
        PaymentAllocatedEvent allocation = PaymentAllocatedEventBuilder.Create()
            .WithInvoiceId(invoiceId)
            .WithOccurredAt(Later)
            .WithCorrelationId("corr-settlement-audit")
            .WithSettlement(600.00m, 600.00m, SettlementStatus.PartiallySettled)
            .Build();

        // Act
        await _harness.ConsumeAsync(allocation);

        // Assert
        AuditEntry audit = _harness.RecordedAudits.Single();
        Assert.Multiple(() =>
        {
            Assert.That(audit.EventType, Is.EqualTo(InvoiceAuditEventTypes.InvoiceSettlementUpdated));
            Assert.That(audit.Operation, Is.EqualTo(AuditOperation.Update));
            Assert.That(audit.EntityType, Is.EqualTo(InvoiceAuditEventTypes.EntityType));
            Assert.That(audit.EntityId, Is.EqualTo(invoiceId.ToString()));
            Assert.That(audit.CorrelationId, Is.EqualTo("corr-settlement-audit"));
            Assert.That(audit.BeforeJson, Does.Contain("LastSettlementAppliedAt"));
            Assert.That(audit.AfterJson, Does.Contain("LastSettlementAppliedAt"));
            Assert.That(audit.Reason, Is.Null);
        });
    }

    /// <summary>
    /// The mirror update appends NO status-history row and drives NO lifecycle transition — settlement is
    /// orthogonal to the lifecycle (§2.14, §2.15).
    /// </summary>
    [Test]
    public async Task AllocationConsumer_AppendsNoStatusHistoryRow_AndDoesNotTransitionLifecycle()
    {
        // Arrange
        Guid invoiceId = await SeedInvoiceAsync(InvoiceSeedBuilder.Create().WithGrossTotal(1000.00m));
        PaymentAllocatedEvent allocation = PaymentAllocatedEventBuilder.Create()
            .WithInvoiceId(invoiceId)
            .WithOccurredAt(Earlier)
            .WithSettlement(500.00m, 500.00m, SettlementStatus.PartiallySettled)
            .Build();

        // Act
        await _harness.ConsumeAsync(allocation);

        // Assert
        Invoice reloaded = await ReloadAsync(invoiceId);
        int historyCount = await _scope.Context.InvoiceStatusHistory.CountAsync(CancellationToken.None);
        Assert.Multiple(() =>
        {
            Assert.That(historyCount, Is.Zero);
            Assert.That(reloaded.Status, Is.EqualTo(InvoiceStatus.Posted));
        });
    }

    /// <summary>
    /// An allocation event is applied whatever the lifecycle state, a CANCELLED invoice included — SDD-PAY-002
    /// keeps allocation rows intact on cancel, so the mirror stays faithful to them (§2.15).
    /// </summary>
    [Test]
    public async Task AllocationConsumer_CancelledInvoice_StillAppliesSettlementUpdate()
    {
        // Arrange
        Guid invoiceId = await SeedInvoiceAsync(InvoiceSeedBuilder.Create()
            .WithStatus(InvoiceStatus.Cancelled)
            .WithGrossTotal(1000.00m));
        PaymentAllocatedEvent allocation = PaymentAllocatedEventBuilder.Create()
            .WithInvoiceId(invoiceId)
            .WithOccurredAt(Earlier)
            .WithSettlement(200.00m, 200.00m, SettlementStatus.PartiallySettled)
            .Build();

        // Act
        await _harness.ConsumeAsync(allocation);

        // Assert
        Invoice reloaded = await ReloadAsync(invoiceId);
        Assert.Multiple(() =>
        {
            Assert.That(reloaded.SettledAmount, Is.EqualTo(200.00m));
            Assert.That(reloaded.SettlementStatus, Is.EqualTo(SettlementStatus.PartiallySettled));
            Assert.That(reloaded.Status, Is.EqualTo(InvoiceStatus.Cancelled));
        });
    }

    /// <summary>
    /// THE ORPHAN-REPAIR PATH (§2.6/§2.14/§2.15): a cancel that won the race against an in-flight allocation
    /// leaves the mirror claiming cash. The later repair deallocation carries 0.00, MUST be applied to the
    /// already-Cancelled invoice, zeroes SettledAmount, re-derives Unsettled, and leaves Status Cancelled.
    /// </summary>
    [Test]
    public async Task AllocationConsumer_DeallocationAfterCancel_ZeroesSettledAmount_AndRederivesStatus()
    {
        // Arrange
        Guid invoiceId = await SeedInvoiceAsync(InvoiceSeedBuilder.Create()
            .WithStatus(InvoiceStatus.Cancelled)
            .WithGrossTotal(1000.00m)
            .WithSettlement(250.00m, SettlementStatus.PartiallySettled, Earlier));
        PaymentDeallocatedEvent repair = PaymentDeallocatedEventBuilder.Create()
            .WithInvoiceId(invoiceId)
            .WithOccurredAt(Later)
            .WithSettlement(250.00m, 0.00m, SettlementStatus.Unsettled)
            .Build();

        // Act
        await _harness.ConsumeAsync(repair);

        // Assert
        Invoice reloaded = await ReloadAsync(invoiceId);
        Assert.Multiple(() =>
        {
            Assert.That(reloaded.SettledAmount, Is.EqualTo(0.00m));
            Assert.That(reloaded.SettlementStatus, Is.EqualTo(SettlementStatus.Unsettled));
            Assert.That(reloaded.Status, Is.EqualTo(InvoiceStatus.Cancelled));
            Assert.That(reloaded.LastSettlementAppliedAt, Is.EqualTo(Later));
        });
    }

    /// <summary>
    /// The mirror never touches the ISSUED document: the number, totals, and the frozen booking rate are
    /// unchanged by an applied settlement event (§2.14).
    /// </summary>
    [Test]
    public async Task AllocationConsumer_LeavesDocumentNumberTotalsAndFrozenExchangeRateUntouched()
    {
        // Arrange
        Guid invoiceId = await SeedInvoiceAsync(InvoiceSeedBuilder.Create()
            .WithGrossTotal(1000.00m)
            .WithCurrency("EUR", 1.955830m));
        PaymentAllocatedEvent allocation = PaymentAllocatedEventBuilder.Create()
            .WithInvoiceId(invoiceId)
            .WithOccurredAt(Earlier)
            .WithSettlement(500.00m, 500.00m, SettlementStatus.PartiallySettled)
            .Build();

        // Act
        await _harness.ConsumeAsync(allocation);

        // Assert
        Invoice reloaded = await ReloadAsync(invoiceId);
        Assert.Multiple(() =>
        {
            Assert.That(reloaded.DocumentNumber, Is.EqualTo("SINV-2026-000001"));
            Assert.That(reloaded.GrossTotal, Is.EqualTo(1000.00m));
            Assert.That(reloaded.ExchangeRate, Is.EqualTo(1.955830m));
        });
    }

    private async Task<Guid> SeedInvoiceAsync(InvoiceSeedBuilder builder)
    {
        Invoice invoice = builder.Build();
        _scope.Context.Invoices.Add(invoice);
        await _scope.Context.SaveChangesAsync(CancellationToken.None);
        _harness.ClearTracker();
        return invoice.Id;
    }

    private async Task<Invoice> ReloadAsync(Guid id)
    {
        _harness.ClearTracker();
        return await _scope.Context.Invoices
            .Include(invoice => invoice.StatusHistory)
            .AsNoTracking()
            .SingleAsync(invoice => invoice.Id == id, CancellationToken.None);
    }
}
