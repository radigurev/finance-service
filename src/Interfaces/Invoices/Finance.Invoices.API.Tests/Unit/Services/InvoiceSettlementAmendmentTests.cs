using Finance.Common.Enums;
using Finance.Common.ErrorCodes;
using Finance.Common.Results;
using Finance.Infrastructure.Audit.Models;
using Finance.Invoices.API.Auditing;
using Finance.Invoices.API.Interfaces;
using Finance.Invoices.API.Tests.Builders;
using Finance.Invoices.API.Tests.Fixtures;
using Finance.Invoices.DBModel.Models;
using Finance.ServiceModel.Events.Invoices;
using Finance.ServiceModel.Invoices;
using Microsoft.EntityFrameworkCore;
using Moq;
using NUnit.Framework;

namespace Finance.Invoices.API.Tests.Unit.Services;

/// <summary>
/// Unit tests for the SDD-INV-001 settlement amendment on
/// <see cref="Finance.Invoices.API.Services.InvoiceService"/> (§2.6, §2.7, §2.11, §2.14, §6.7): the booking rate
/// FROZEN at creation, the two additive <see cref="InvoiceConfirmedEvent"/> fields sourced from it, the
/// <c>Posted → Reversed</c> publish of <see cref="InvoiceReversedEvent"/> through the outbox, the best-effort
/// <c>INVOICE_HAS_SETTLEMENTS</c> cancel guard, and the rule that the cancel and reverse PATHS never write the
/// settlement columns.
/// <para>The <see cref="InvoiceReversedEvent"/> assertions read the harness's TYPED capture: the shipped publish
/// mock's <c>It.IsAny&lt;object&gt;()</c> catch-all does NOT intercept a different generic instantiation of
/// <c>Publish&lt;T&gt;</c>, so the event is only observable through the per-type setup.</para>
/// Runs fully offline against a SQLite in-memory context with the real workflow engine and totals calculator.
/// </summary>
[TestFixture]
[Category("SDD-INV-001")]
[Category("SDD-PAY-002")]
public sealed class InvoiceSettlementAmendmentTests
{
    private SqliteInvoicesDbContextScope _scope = null!;
    private InvoiceServiceTestHarness _harness = null!;

    /// <summary>Creates a fresh SQLite-backed harness before each test.</summary>
    [SetUp]
    public void SetUp()
    {
        _scope = SqliteInvoicesDbContextFactory.Create();
        _harness = InvoiceServiceTestHarness.Build(_scope.Context);
    }

    /// <summary>Disposes the SQLite scope after each test.</summary>
    [TearDown]
    public void TearDown()
    {
        _scope.Dispose();
    }

    // ---- The frozen booking rate (SDD-INV-001 §2.14) ----

    /// <summary>
    /// A base-currency invoice freezes ExchangeRate at exactly 1.000000 whatever the caller supplied — the rate is
    /// irrelevant when the transactional currency IS the base currency (§2.14).
    /// </summary>
    [Test]
    public async Task CreateDraft_FreezesExchangeRate_DefaultsToOne_WhenCurrencyIsBaseCurrency()
    {
        // Arrange
        CreateInvoiceRequest request = CreateInvoiceRequestBuilder.Create()
            .WithCurrencyCode(FakeInvoiceCountryStrategy.BaseCurrency)
            .WithExchangeRate(1.955830m)
            .Build();

        // Act
        Result<InvoiceDto> created =
            await _harness.Service.CreateDraftAsync(request, allowEmptyLines: false, CancellationToken.None);

        // Assert
        Assert.That(created.IsSuccess, Is.True, created.ErrorCode);
        Invoice persisted = await ReloadAsync(created.Value!.Id);
        Assert.Multiple(() =>
        {
            Assert.That(persisted.ExchangeRate, Is.EqualTo(1.000000m));
            Assert.That(created.Value.ExchangeRate, Is.EqualTo(1.000000m));
        });
    }

    /// <summary>
    /// A non-base-currency invoice freezes the CALLER-SUPPLIED rate, which is the only source of
    /// InvoiceConfirmedEvent.BookingExchangeRate (§2.14).
    /// </summary>
    [Test]
    public async Task CreateDraft_FreezesCallerSuppliedExchangeRate_ForNonBaseCurrencyInvoice()
    {
        // Arrange
        CreateInvoiceRequest request = CreateInvoiceRequestBuilder.Create()
            .WithCurrencyCode("EUR")
            .WithExchangeRate(1.955830m)
            .Build();

        // Act
        Result<InvoiceDto> created =
            await _harness.Service.CreateDraftAsync(request, allowEmptyLines: false, CancellationToken.None);

        // Assert
        Assert.That(created.IsSuccess, Is.True, created.ErrorCode);
        Invoice persisted = await ReloadAsync(created.Value!.Id);
        Assert.That(persisted.ExchangeRate, Is.EqualTo(1.955830m));
    }

    /// <summary>
    /// A new draft starts fully unsettled with a NULL ordering token, so the first allocation event always applies
    /// (§2.14).
    /// </summary>
    [Test]
    public async Task CreateDraft_InitializesSettlementColumns_ToUnsettledZeroAndNullOrderingToken()
    {
        // Arrange
        CreateInvoiceRequest request = CreateInvoiceRequestBuilder.Create().Build();

        // Act
        Result<InvoiceDto> created =
            await _harness.Service.CreateDraftAsync(request, allowEmptyLines: false, CancellationToken.None);

        // Assert
        Assert.That(created.IsSuccess, Is.True, created.ErrorCode);
        Invoice persisted = await ReloadAsync(created.Value!.Id);
        Assert.Multiple(() =>
        {
            Assert.That(persisted.SettledAmount, Is.EqualTo(0.00m));
            Assert.That(persisted.SettlementStatus, Is.EqualTo(SettlementStatus.Unsettled));
            Assert.That(persisted.LastSettlementAppliedAt, Is.Null);
        });
    }

    /// <summary>
    /// Updating a draft leaves the frozen booking rate untouched — the rate belongs to the issued document, not to
    /// the mutable draft surface (§2.14, §2.9).
    /// </summary>
    [Test]
    public async Task UpdateDraft_DoesNotChangeTheFrozenExchangeRate()
    {
        // Arrange
        Invoice draft = await PersistDraftAsync(currencyCode: "EUR", exchangeRate: 1.955830m);

        // Act
        Result<InvoiceDto> updated = await _harness.Service.UpdateDraftAsync(
            draft.Id, UpdateRequest(draft), CancellationToken.None);

        // Assert
        Assert.That(updated.IsSuccess, Is.True, updated.ErrorCode);
        Invoice reloaded = await ReloadAsync(draft.Id);
        Assert.That(reloaded.ExchangeRate, Is.EqualTo(1.955830m));
    }

    // ---- The two additive InvoiceConfirmedEvent fields (SDD-INV-001 §2.11/§2.15) ----

    /// <summary>
    /// Confirm publishes DueDate and BookingExchangeRate sourced from the invoice's own columns, so SDD-PAY-002
    /// never takes its degraded fallback (§2.11, §2.15).
    /// </summary>
    [Test]
    public async Task Confirm_PublishesInvoiceConfirmedEvent_CarryingDueDateAndBookingExchangeRate()
    {
        // Arrange
        Invoice draft = await PersistDraftAsync();

        // Act
        Result<InvoiceDto> confirmed = await _harness.Service.ConfirmAsync(
            draft.Id, ConfirmRequest(draft), CancellationToken.None);

        // Assert
        Assert.That(confirmed.IsSuccess, Is.True, confirmed.ErrorCode);
        InvoiceConfirmedEvent published = _harness.PublishedEvents.OfType<InvoiceConfirmedEvent>().Single();
        Assert.Multiple(() =>
        {
            Assert.That(published.DueDate, Is.EqualTo(draft.DueDate));
            Assert.That(published.BookingExchangeRate, Is.EqualTo(1.000000m));
        });
    }

    /// <summary>
    /// A NON-base-currency invoice publishes its REAL frozen rate, never a fabricated 1.000000 — a fabricated rate
    /// would corrupt SDD-PAY-002's realized-FX difference and SDD-PAY-003's base outstanding for every such
    /// invoice (§2.11, §2.14, §2.15).
    /// </summary>
    [Test]
    public async Task Confirm_NonBaseCurrencyInvoice_PublishesItsRealBookingExchangeRate_NotOne()
    {
        // Arrange
        Invoice draft = await PersistDraftAsync(currencyCode: "EUR", exchangeRate: 1.955830m);

        // Act
        Result<InvoiceDto> confirmed = await _harness.Service.ConfirmAsync(
            draft.Id, ConfirmRequest(draft), CancellationToken.None);

        // Assert
        Assert.That(confirmed.IsSuccess, Is.True, confirmed.ErrorCode);
        InvoiceConfirmedEvent published = _harness.PublishedEvents.OfType<InvoiceConfirmedEvent>().Single();
        Assert.Multiple(() =>
        {
            Assert.That(published.BookingExchangeRate, Is.EqualTo(1.955830m));
            Assert.That(published.BookingExchangeRate, Is.Not.EqualTo(1.000000m));
            Assert.That(published.CurrencyCode, Is.EqualTo("EUR"));
        });
    }

    // ---- The Posted → Reversed publish (SDD-INV-001 §2.7/§2.11) ----

    /// <summary>
    /// The Posted → Reversed transition flips the state flag and enqueues InvoiceReversedEvent to the outbox, so a
    /// reversal can never land without the sub-ledger being told (§2.7).
    /// </summary>
    [Test]
    public async Task PostedToReversedTransition_PublishesInvoiceReversedEvent_ThroughOutbox()
    {
        // Arrange
        Invoice posted = await PersistPostedAsync();
        InvoiceReversalRequest request = ReversalRequest(posted.Id, "Fully offset by credit note");

        // Act
        Result<InvoiceDto> reversed = await _harness.Service.MarkReversedAsync(request, CancellationToken.None);

        // Assert
        Assert.That(reversed.IsSuccess, Is.True, reversed.ErrorCode);
        Invoice reloaded = await ReloadAsync(posted.Id);
        Assert.Multiple(() =>
        {
            Assert.That(reloaded.Status, Is.EqualTo(InvoiceStatus.Reversed));
            Assert.That(_harness.PublishedEvents.OfType<InvoiceReversedEvent>().Count(), Is.EqualTo(1));
        });
    }

    /// <summary>
    /// The published reversal event carries the reversed ORIGINAL's id and document number, the correcting note's
    /// id, and the reason (§2.11).
    /// </summary>
    [Test]
    public async Task InvoiceReversedEvent_CarriesOriginalInvoiceId_DocumentNumber_CorrectingInvoiceId_AndReason()
    {
        // Arrange
        Invoice posted = await PersistPostedAsync();
        Guid correctingNoteId = Guid.NewGuid();
        InvoiceReversalRequest request = new()
        {
            InvoiceId = posted.Id,
            CorrectingInvoiceId = correctingNoteId,
            Reason = "Credit note CN-2026-000001 fully offsets the original"
        };

        // Act
        Result<InvoiceDto> reversed = await _harness.Service.MarkReversedAsync(request, CancellationToken.None);

        // Assert
        Assert.That(reversed.IsSuccess, Is.True, reversed.ErrorCode);
        InvoiceReversedEvent published = _harness.PublishedEvents.OfType<InvoiceReversedEvent>().Single();
        Assert.Multiple(() =>
        {
            Assert.That(published.InvoiceId, Is.EqualTo(posted.Id));
            Assert.That(published.DocumentNumber, Is.EqualTo(posted.DocumentNumber));
            Assert.That(published.CorrectingInvoiceId, Is.EqualTo(correctingNoteId));
            Assert.That(published.Reason, Is.EqualTo(request.Reason));
            Assert.That(published.MessageId, Is.Not.EqualTo(Guid.Empty));
            Assert.That(published.CorrelationId, Is.EqualTo(StubCorrelationIdAccessor.CorrelationId));
        });
    }

    /// <summary>
    /// The reversal writes its audit StateChange row BEFORE the outbox publish (audit-first, SDD-AUDIT-001,
    /// §2.7).
    /// </summary>
    [Test]
    public async Task PostedToReversedTransition_RecordsAuditStateChange_BeforeOutboxPublish()
    {
        // Arrange
        Invoice posted = await PersistPostedAsync();
        _harness.RecordedAudits.Clear();
        _harness.PublishedEvents.Clear();
        List<string> callOrder = [];
        _harness.AuditMock
            .Setup(a => a.RecordAsync(It.IsAny<AuditEntry>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
            .Callback<AuditEntry, CancellationToken, bool>((entry, _, _) =>
            {
                _harness.RecordedAudits.Add(entry);
                callOrder.Add(nameof(AuditEntry));
            })
            .ReturnsAsync(Result.Success());
        _harness.PublishMock
            .Setup(p => p.Publish(It.IsAny<InvoiceReversedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<InvoiceReversedEvent, CancellationToken>((message, _) =>
            {
                _harness.PublishedEvents.Add(message);
                callOrder.Add(nameof(InvoiceReversedEvent));
            })
            .Returns(Task.CompletedTask);

        // Act
        Result<InvoiceDto> reversed = await _harness.Service.MarkReversedAsync(
            ReversalRequest(posted.Id, "Audit ordering check"), CancellationToken.None);

        // Assert
        Assert.That(reversed.IsSuccess, Is.True, reversed.ErrorCode);
        Assert.Multiple(() =>
        {
            Assert.That(callOrder, Is.EqualTo(new[] { nameof(AuditEntry), nameof(InvoiceReversedEvent) }));
            Assert.That(
                _harness.RecordedAudits.Single().EventType,
                Is.EqualTo(InvoiceAuditEventTypes.InvoiceReversed));
            Assert.That(_harness.RecordedAudits.Single().Operation, Is.EqualTo(AuditOperation.StateChange));
        });
    }

    /// <summary>
    /// The reverse PATH never writes the settlement columns: a terminal transition carries forward exactly the
    /// figures the invoice held (§2.14).
    /// </summary>
    [Test]
    public async Task PostedToReversedTransition_DoesNotWriteSettlementColumns()
    {
        // Arrange
        Invoice posted = await PersistPostedAsync();
        await SetSettlementMirrorAsync(posted.Id, 25.00m, SettlementStatus.PartiallySettled);

        // Act
        Result<InvoiceDto> reversed = await _harness.Service.MarkReversedAsync(
            ReversalRequest(posted.Id, "Settlement carry-forward check"), CancellationToken.None);

        // Assert
        Assert.That(reversed.IsSuccess, Is.True, reversed.ErrorCode);
        Invoice reloaded = await ReloadAsync(posted.Id);
        Assert.Multiple(() =>
        {
            Assert.That(reloaded.Status, Is.EqualTo(InvoiceStatus.Reversed));
            Assert.That(reloaded.SettledAmount, Is.EqualTo(25.00m));
            Assert.That(reloaded.SettlementStatus, Is.EqualTo(SettlementStatus.PartiallySettled));
            Assert.That(reloaded.LastSettlementAppliedAt, Is.Null);
        });
    }

    /// <summary>
    /// Reversal is legal only from Posted: a Draft original is rejected by the workflow engine and publishes
    /// nothing (§2.1, §2.7).
    /// </summary>
    [Test]
    public async Task MarkReversed_NonPostedInvoice_ReturnsInvalidInvoiceStateTransition_AndPublishesNothing()
    {
        // Arrange
        Invoice draft = await PersistDraftAsync();
        _harness.PublishedEvents.Clear();

        // Act
        Result<InvoiceDto> reversed = await _harness.Service.MarkReversedAsync(
            ReversalRequest(draft.Id, "Illegal reversal"), CancellationToken.None);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(reversed.IsSuccess, Is.False);
            Assert.That(reversed.ErrorCode, Is.EqualTo(InvoiceErrorCodes.INVALID_INVOICE_STATE_TRANSITION));
            Assert.That(_harness.PublishedEvents.OfType<InvoiceReversedEvent>(), Is.Empty);
        });
    }

    // ---- The best-effort INVOICE_HAS_SETTLEMENTS cancel guard (SDD-INV-001 §2.6) ----

    /// <summary>
    /// A confirmed invoice that already carries settlements cannot be cancelled: the guard fails BEFORE the
    /// transition, so no state changes, no audit row is written, and no event is published (§2.6).
    /// </summary>
    [Test]
    public async Task Cancel_InvoiceWithSettlements_ReturnsInvoiceHasSettlements_WritesNothing()
    {
        // Arrange
        Invoice confirmed = await PersistConfirmedAsync();
        await SetSettlementMirrorAsync(confirmed.Id, 100.00m, SettlementStatus.PartiallySettled);
        Invoice settled = await ReloadAsync(confirmed.Id);
        _harness.RecordedAudits.Clear();
        _harness.PublishedEvents.Clear();

        // Act
        Result<InvoiceDto> cancelled = await _harness.Service.CancelAsync(
            settled.Id, CancelRequest(settled, "Operator mistake"), CancellationToken.None);

        // Assert
        Invoice reloaded = await ReloadAsync(confirmed.Id);
        Assert.Multiple(() =>
        {
            Assert.That(cancelled.IsSuccess, Is.False);
            Assert.That(cancelled.ErrorCode, Is.EqualTo(InvoiceErrorCodes.INVOICE_HAS_SETTLEMENTS));
            Assert.That(reloaded.Status, Is.EqualTo(InvoiceStatus.Confirmed));
            Assert.That(_harness.RecordedAudits, Is.Empty);
            Assert.That(_harness.PublishedEvents.OfType<InvoiceCancelledEvent>(), Is.Empty);
        });
    }

    /// <summary>
    /// A zero settled amount does not trip the guard — an unsettled confirmed invoice still cancels normally
    /// (§2.6).
    /// </summary>
    [Test]
    public async Task Cancel_InvoiceWithZeroSettledAmount_StillTransitionsToCancelled()
    {
        // Arrange
        Invoice confirmed = await PersistConfirmedAsync();

        // Act
        Result<InvoiceDto> cancelled = await _harness.Service.CancelAsync(
            confirmed.Id, CancelRequest(confirmed, "Duplicate document"), CancellationToken.None);

        // Assert
        Assert.That(cancelled.IsSuccess, Is.True, cancelled.ErrorCode);
        Invoice reloaded = await ReloadAsync(confirmed.Id);
        Assert.That(reloaded.Status, Is.EqualTo(InvoiceStatus.Cancelled));
    }

    /// <summary>
    /// The cancel PATH never writes the settlement columns — the cancelled row carries forward exactly the
    /// figures it held (§2.14).
    /// </summary>
    [Test]
    public async Task Cancel_DoesNotWriteSettlementColumns()
    {
        // Arrange
        Invoice confirmed = await PersistConfirmedAsync();

        // Act
        Result<InvoiceDto> cancelled = await _harness.Service.CancelAsync(
            confirmed.Id, CancelRequest(confirmed, "Duplicate document"), CancellationToken.None);

        // Assert
        Assert.That(cancelled.IsSuccess, Is.True, cancelled.ErrorCode);
        Invoice reloaded = await ReloadAsync(confirmed.Id);
        Assert.Multiple(() =>
        {
            Assert.That(reloaded.SettledAmount, Is.EqualTo(0.00m));
            Assert.That(reloaded.SettlementStatus, Is.EqualTo(SettlementStatus.Unsettled));
            Assert.That(reloaded.LastSettlementAppliedAt, Is.Null);
        });
    }

    /// <summary>The amendment's cancel-guard error code is declared as a constant (§4, §6.7).</summary>
    [Test]
    public void InvoiceErrorCodes_DefinesInvoiceHasSettlements()
    {
        // Arrange & Act
        string code = InvoiceErrorCodes.INVOICE_HAS_SETTLEMENTS;

        // Assert
        Assert.That(code, Is.EqualTo("INVOICE_HAS_SETTLEMENTS"));
    }

    // ---- Helpers ----

    private async Task<Invoice> PersistDraftAsync(
        string? currencyCode = null,
        decimal? exchangeRate = null)
    {
        CreateInvoiceRequest request = CreateInvoiceRequestBuilder.Create()
            .WithCurrencyCode(currencyCode ?? FakeInvoiceCountryStrategy.BaseCurrency)
            .WithExchangeRate(exchangeRate)
            .Build();
        Result<InvoiceDto> created =
            await _harness.Service.CreateDraftAsync(request, allowEmptyLines: false, CancellationToken.None);
        Assert.That(created.IsSuccess, Is.True, created.ErrorCode);
        return await ReloadAsync(created.Value!.Id);
    }

    private async Task<Invoice> PersistConfirmedAsync()
    {
        Invoice draft = await PersistDraftAsync();
        Result<InvoiceDto> confirmed = await _harness.Service.ConfirmAsync(
            draft.Id, ConfirmRequest(draft), CancellationToken.None);
        Assert.That(confirmed.IsSuccess, Is.True, confirmed.ErrorCode);
        return await ReloadAsync(draft.Id);
    }

    private async Task<Invoice> PersistPostedAsync()
    {
        Invoice confirmed = await PersistConfirmedAsync();
        Result linked = await _harness.Service.LinkPostedJournalEntryAsync(
            confirmed.Id, Guid.NewGuid(), CancellationToken.None);
        Assert.That(linked.IsSuccess, Is.True, linked.ErrorCode);
        return await ReloadAsync(confirmed.Id);
    }

    /// <summary>
    /// Writes the settlement mirror directly, the way the SDD-PAY-002 allocation consumers would, so a cancel or
    /// reverse path can be exercised against an invoice that already carries settlements.
    /// </summary>
    /// <param name="invoiceId">The invoice whose mirror is seeded.</param>
    /// <param name="settledAmount">The mirrored settled amount.</param>
    /// <param name="settlementStatus">The derived settlement status.</param>
    private async Task SetSettlementMirrorAsync(
        Guid invoiceId,
        decimal settledAmount,
        SettlementStatus settlementStatus)
    {
        _scope.Context.ChangeTracker.Clear();
        Invoice tracked = await _scope.Context.Invoices
            .IgnoreAutoIncludes()
            .SingleAsync(invoice => invoice.Id == invoiceId, CancellationToken.None);
        tracked.SettledAmount = settledAmount;
        tracked.SettlementStatus = settlementStatus;
        await _scope.Context.SaveChangesAsync(CancellationToken.None);
        _scope.Context.ChangeTracker.Clear();
    }

    private async Task<Invoice> ReloadAsync(Guid id)
    {
        _scope.Context.ChangeTracker.Clear();
        return await _scope.Context.Invoices
            .Include(invoice => invoice.Lines)
            .Include(invoice => invoice.StatusHistory)
            .AsNoTracking()
            .SingleAsync(invoice => invoice.Id == id, CancellationToken.None);
    }

    private static InvoiceReversalRequest ReversalRequest(Guid invoiceId, string reason) => new()
    {
        InvoiceId = invoiceId,
        CorrectingInvoiceId = Guid.NewGuid(),
        Reason = reason
    };

    private static ConfirmInvoiceRequest ConfirmRequest(Invoice invoice) =>
        new() { RowVersion = Convert.ToBase64String(invoice.RowVersion) };

    private static CancelInvoiceRequest CancelRequest(Invoice invoice, string reason) =>
        new() { Reason = reason, RowVersion = Convert.ToBase64String(invoice.RowVersion) };

    private static UpdateInvoiceRequest UpdateRequest(Invoice invoice) => new()
    {
        CounterpartyId = invoice.CounterpartyId,
        CurrencyCode = invoice.CurrencyCode,
        IssueDate = invoice.IssueDate,
        DueDate = invoice.DueDate,
        RowVersion = Convert.ToBase64String(invoice.RowVersion),
        Lines = [InvoiceLineRequestBuilder.Create().Build()]
    };
}
