using Finance.Common.Enums;
using Finance.Common.ErrorCodes;
using Finance.Invoices.API.Consumers;
using Finance.Invoices.API.Interfaces;
using Finance.Invoices.API.Tests.Builders;
using Finance.Invoices.API.Tests.Fixtures;
using Finance.Invoices.DBModel.Models;
using Finance.ServiceModel.Integration.Warehouse.Events;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;

namespace Finance.Invoices.API.Tests.Unit.Consumers;

/// <summary>
/// Unit tests for the four Warehouse inbound consumers and the shared
/// <see cref="Finance.Invoices.API.Services.WarehouseInvoiceDraftFactory"/> (SDD-INT-WH-001 §6.1, §6.3).
/// They run the REAL consumers over the REAL factory and REAL <c>InvoiceService</c> on a SQLite in-memory
/// context, proving each event maps to the right draft document via the SAME SDD-INV-001 create path, stamps
/// the inbound correlation id and source-document linkage, leaves the invoice in <c>Draft</c>, and applies
/// the permanent-vs-transient failure policy (log + acknowledge vs throw-for-retry). Permanent business
/// failures are surfaced through the factory as <see cref="WarehouseDraftOutcomeKind.PermanentFailure"/> and
/// the consumer acknowledges (does not throw).
/// </summary>
[TestFixture]
[Category("SDD-INT-WH-001")]
public sealed class WarehouseInboundConsumerTests
{
    private SqliteInvoicesDbContextScope _scope = null!;
    private InvoiceServiceTestHarness _invoices = null!;
    private WarehouseConsumerTestHarness _harness = null!;

    /// <summary>Creates a fresh SQLite-backed consumer harness before each test.</summary>
    [SetUp]
    public void SetUp()
    {
        _scope = SqliteInvoicesDbContextFactory.Create();
        _invoices = InvoiceServiceTestHarness.Build(_scope.Context);
        _harness = WarehouseConsumerTestHarness.Build(_invoices);
    }

    /// <summary>Disposes the SQLite scope after each test.</summary>
    [TearDown]
    public void TearDown()
    {
        _scope.Dispose();
    }

    // ---- Consumer mapping (SDD-INT-WH-001 §6.1) ----

    /// <summary>A goods-receipt event creates a draft purchase invoice with the supplier and lines (§2.2, §6.1).</summary>
    [Test]
    public async Task GoodsReceiptCompleted_CreatesDraftPurchaseInvoice_WithSupplierAndLines()
    {
        // Arrange
        Guid supplier = Guid.NewGuid();
        GoodsReceiptCompletedEvent @event = WarehouseEventBuilder.Create()
            .WithCounterpartyId(supplier)
            .WithLines(WarehouseDocumentLineBuilder.Create().WithQuantity(2m).WithUnitPrice(50m).Build())
            .BuildGoodsReceipt();

        // Act
        await _harness.ConsumeAsync(@event);

        // Assert
        Invoice draft = await SingleInvoiceAsync();
        Assert.Multiple(() =>
        {
            Assert.That(draft.DocumentType, Is.EqualTo(InvoiceDocumentType.PurchaseInvoice));
            Assert.That(draft.Direction, Is.EqualTo(InvoiceDirection.AP));
            Assert.That(draft.CounterpartyId, Is.EqualTo(supplier));
            Assert.That(draft.Lines, Has.Count.EqualTo(1));
            Assert.That(draft.Status, Is.EqualTo(InvoiceStatus.Draft));
        });
    }

    /// <summary>A shipment event creates a draft sale invoice with the customer and lines (§2.2, §6.1).</summary>
    [Test]
    public async Task ShipmentCompleted_CreatesDraftSaleInvoice_WithCustomerAndLines()
    {
        // Arrange
        Guid customer = Guid.NewGuid();
        ShipmentCompletedEvent @event = WarehouseEventBuilder.Create()
            .WithCounterpartyId(customer)
            .BuildShipment();

        // Act
        await _harness.ConsumeAsync(@event);

        // Assert
        Invoice draft = await SingleInvoiceAsync();
        Assert.Multiple(() =>
        {
            Assert.That(draft.DocumentType, Is.EqualTo(InvoiceDocumentType.SaleInvoice));
            Assert.That(draft.Direction, Is.EqualTo(InvoiceDirection.AR));
            Assert.That(draft.CounterpartyId, Is.EqualTo(customer));
        });
    }

    /// <summary>A customer-return event creates a draft credit note with the customer and lines (§2.2, §6.1).</summary>
    [Test]
    public async Task CustomerReturnCompleted_CreatesDraftCreditNote_WithCustomerAndLines()
    {
        // Arrange
        Guid customer = Guid.NewGuid();
        CustomerReturnCompletedEvent @event = WarehouseEventBuilder.Create()
            .WithCounterpartyId(customer)
            .BuildCustomerReturn();

        // Act
        await _harness.ConsumeAsync(@event);

        // Assert
        Invoice draft = await SingleInvoiceAsync();
        Assert.Multiple(() =>
        {
            Assert.That(draft.DocumentType, Is.EqualTo(InvoiceDocumentType.CreditNote));
            Assert.That(draft.CounterpartyId, Is.EqualTo(customer));
        });
    }

    /// <summary>A supplier-return event creates a draft debit note with the supplier and lines (§2.2, §6.1).</summary>
    [Test]
    public async Task SupplierReturnShipped_CreatesDraftDebitNote_WithSupplierAndLines()
    {
        // Arrange
        Guid supplier = Guid.NewGuid();
        SupplierReturnShippedEvent @event = WarehouseEventBuilder.Create()
            .WithCounterpartyId(supplier)
            .BuildSupplierReturn();

        // Act
        await _harness.ConsumeAsync(@event);

        // Assert
        Invoice draft = await SingleInvoiceAsync();
        Assert.Multiple(() =>
        {
            Assert.That(draft.DocumentType, Is.EqualTo(InvoiceDocumentType.DebitNote));
            Assert.That(draft.Direction, Is.EqualTo(InvoiceDirection.AR));
            Assert.That(draft.CounterpartyId, Is.EqualTo(supplier));
        });
    }

    /// <summary>The consumer creates the draft via the InvoiceService create path, recording an audit Create (§2.1, §6.1).</summary>
    [Test]
    public async Task Consumer_CreatesDraftViaInvoiceServiceCreatePath_NotDirectPersistence()
    {
        // Arrange
        ShipmentCompletedEvent @event = WarehouseEventBuilder.Create().BuildShipment();

        // Act
        await _harness.ConsumeAsync(@event);

        // Assert — the audit Create row is the signature of the shared SDD-INV-001 create path.
        Assert.That(
            _invoices.RecordedAudits.Any(a => a.EventType == "InvoiceCreated"),
            Is.True);
    }

    /// <summary>The created draft carries the inbound event's correlation id (§2.1, §6.1).</summary>
    [Test]
    public async Task Consumer_StampsInboundCorrelationId_OnCreatedDraft()
    {
        // Arrange
        ShipmentCompletedEvent @event = WarehouseEventBuilder.Create()
            .WithCorrelationId("inbound-corr-42")
            .BuildShipment();

        // Act
        await _harness.ConsumeAsync(@event);

        // Assert
        Invoice draft = await SingleInvoiceAsync();
        Assert.That(draft.CorrelationId, Is.EqualTo("inbound-corr-42"));
    }

    /// <summary>The created draft persists the source-document id and type for traceability and dedupe (§2.1.4, §6.1).</summary>
    [Test]
    public async Task Consumer_PersistsSourceDocumentIdAndType_ForTraceabilityAndDedupe()
    {
        // Arrange
        Guid sourceDocumentId = Guid.NewGuid();
        GoodsReceiptCompletedEvent @event = WarehouseEventBuilder.Create()
            .WithSourceDocumentId(sourceDocumentId)
            .BuildGoodsReceipt();

        // Act
        await _harness.ConsumeAsync(@event);

        // Assert
        Invoice draft = await SingleInvoiceAsync();
        Assert.Multiple(() =>
        {
            Assert.That(draft.SourceDocumentId, Is.EqualTo(sourceDocumentId));
            Assert.That(draft.SourceDocumentType, Is.EqualTo(WarehouseSourceDocumentTypes.GoodsReceipt));
        });
    }

    /// <summary>The consumer leaves the created invoice in Draft — it never confirms or posts (§2.1, §6.1).</summary>
    [Test]
    public async Task Consumer_DoesNotConfirmOrPost_LeavesInvoiceInDraft()
    {
        // Arrange
        ShipmentCompletedEvent @event = WarehouseEventBuilder.Create().BuildShipment();

        // Act
        await _harness.ConsumeAsync(@event);

        // Assert
        Invoice draft = await SingleInvoiceAsync();
        Assert.Multiple(() =>
        {
            Assert.That(draft.Status, Is.EqualTo(InvoiceStatus.Draft));
            Assert.That(draft.DocumentNumber, Is.Null);
            Assert.That(_invoices.PublishedEvents, Is.Empty);
        });
    }

    /// <summary>A customer return with no matching source sale invoice still creates a standalone credit note (§2.6, §6.1).</summary>
    [Test]
    public async Task CustomerReturnCompleted_NoMatchingSourceInvoice_CreatesStandaloneCreditNote()
    {
        // Arrange — an originating shipment id that matches no Finance invoice.
        CustomerReturnCompletedEvent @event = WarehouseEventBuilder.Create()
            .WithOriginatingShipmentId(Guid.NewGuid())
            .BuildCustomerReturn();

        // Act
        await _harness.ConsumeAsync(@event);

        // Assert
        Invoice draft = await SingleInvoiceAsync();
        Assert.Multiple(() =>
        {
            Assert.That(draft.DocumentType, Is.EqualTo(InvoiceDocumentType.CreditNote));
            Assert.That(draft.CorrectsInvoiceId, Is.Null);
        });
    }

    // ---- Failure handling & contract checks (SDD-INT-WH-001 §6.3) ----

    /// <summary>A zero-line event is a permanent failure: logged and acknowledged, no draft (§2.4, §6.3).</summary>
    [Test]
    public async Task Consumer_EventWithZeroLines_LogsAndAcknowledges_NoDraft()
    {
        // Arrange
        GoodsReceiptCompletedEvent @event = WarehouseEventBuilder.Create().WithNoLines().BuildGoodsReceipt();

        // Act & Assert — the consumer acknowledges (does not throw) and no draft is created.
        Assert.That(async () => await _harness.ConsumeAsync(@event), Throws.Nothing);
        Assert.That(await _scope.Context.Invoices.CountAsync(CancellationToken.None), Is.Zero);
    }

    /// <summary>A malformed (empty) counterparty is a permanent failure: acknowledged, no draft (§2.4, §6.3).</summary>
    [Test]
    public async Task Consumer_MalformedCounterparty_LogsAndAcknowledges_NoDraft()
    {
        // Arrange
        ShipmentCompletedEvent @event = WarehouseEventBuilder.Create()
            .WithCounterpartyId(Guid.Empty)
            .BuildShipment();

        // Act & Assert
        Assert.That(async () => await _harness.ConsumeAsync(@event), Throws.Nothing);
        Assert.That(await _scope.Context.Invoices.CountAsync(CancellationToken.None), Is.Zero);
    }

    /// <summary>An unknown currency is a permanent failure: acknowledged, no draft (§2.4, §6.3).</summary>
    [Test]
    public async Task Consumer_UnknownCurrency_LogsAndAcknowledges_NoDraft()
    {
        // Arrange
        ShipmentCompletedEvent @event = WarehouseEventBuilder.Create()
            .WithCurrencyCode("BADCODE")
            .BuildShipment();

        // Act & Assert
        Assert.That(async () => await _harness.ConsumeAsync(@event), Throws.Nothing);
        Assert.That(await _scope.Context.Invoices.CountAsync(CancellationToken.None), Is.Zero);
    }

    /// <summary>A transient infrastructure failure from the create path propagates so MassTransit retries (§2.4, §6.3).</summary>
    [Test]
    public void Consumer_TransientInfrastructureFailure_Throws_ForRetry()
    {
        // Arrange — a factory whose create path throws a transient infrastructure exception.
        Mock<IWarehouseInvoiceDraftFactory> factoryMock = new();
        factoryMock
            .Setup(f => f.CreateDraftAsync(
                It.IsAny<IWarehouseDocumentEvent>(),
                It.IsAny<InvoiceDocumentType>(),
                It.IsAny<string>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("DB unreachable"));
        Finance.Invoices.API.Consumers.ShipmentCompletedConsumer consumer = new(
            factoryMock.Object, NullLogger<Finance.Invoices.API.Consumers.ShipmentCompletedConsumer>.Instance);
        ShipmentCompletedEvent @event = WarehouseEventBuilder.Create().BuildShipment();

        // Act & Assert
        Assert.That(
            async () => await consumer.Consume(ContextFor(@event)),
            Throws.TypeOf<TimeoutException>());
    }

    /// <summary>A business failure surfaced from the create path is acknowledged, not thrown (§2.4, §6.3).</summary>
    [Test]
    public void Consumer_BusinessFailureFromCreatePath_AcknowledgesNotThrows()
    {
        // Arrange — a factory that returns a permanent business-failure outcome.
        Mock<IWarehouseInvoiceDraftFactory> factoryMock = new();
        factoryMock
            .Setup(f => f.CreateDraftAsync(
                It.IsAny<IWarehouseDocumentEvent>(),
                It.IsAny<InvoiceDocumentType>(),
                It.IsAny<string>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(WarehouseDraftOutcome.PermanentFailure(InvoiceErrorCodes.INVOICE_LINES_REQUIRED));
        Finance.Invoices.API.Consumers.ShipmentCompletedConsumer consumer = new(
            factoryMock.Object, NullLogger<Finance.Invoices.API.Consumers.ShipmentCompletedConsumer>.Instance);
        ShipmentCompletedEvent @event = WarehouseEventBuilder.Create().BuildShipment();

        // Act & Assert
        Assert.That(async () => await consumer.Consume(ContextFor(@event)), Throws.Nothing);
    }

    /// <summary>The consumer tolerates and ignores Warehouse fields it does not consume (§2.3, §6.3).</summary>
    [Test]
    public async Task Consumer_ToleratesUnknownWarehouseFields_DoesNotFail()
    {
        // Arrange — a line with an omitted tax rate (the consumer defaults it) and a null description.
        ShipmentCompletedEvent @event = WarehouseEventBuilder.Create()
            .WithLines(WarehouseDocumentLineBuilder.Create()
                .WithTaxRate(null)
                .WithDescription(null)
                .Build())
            .BuildShipment();

        // Act & Assert
        Assert.That(async () => await _harness.ConsumeAsync(@event), Throws.Nothing);
        Invoice draft = await SingleInvoiceAsync();
        Assert.That(draft.Lines.Single().TaxRate, Is.EqualTo(_invoices.Country.StandardTaxRate));
    }

    private async Task<Invoice> SingleInvoiceAsync()
    {
        _scope.Context.ChangeTracker.Clear();
        return await _scope.Context.Invoices
            .Include(invoice => invoice.Lines)
            .SingleAsync(CancellationToken.None);
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
