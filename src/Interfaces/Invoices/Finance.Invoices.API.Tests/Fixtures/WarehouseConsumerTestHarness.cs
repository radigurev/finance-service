using Finance.Invoices.API.Interfaces;
using Finance.Invoices.API.Services;
using Finance.ServiceModel.Integration.Warehouse.Events;
using MassTransit;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Finance.Invoices.API.Tests.Fixtures;

/// <summary>
/// Assembles the four Warehouse inbound consumers over the REAL <see cref="WarehouseInvoiceDraftFactory"/>
/// and the REAL <see cref="InvoiceService"/> on a SQLite in-memory context (SDD-INT-WH-001 §6). Because the
/// factory and service are the production types, the tests prove the consumers create drafts through the
/// SAME SDD-INV-001 create path (not a parallel persistence path), default a missing tax rate to the
/// country's standard rate, dedupe on the source document, and leave the invoice in <c>Draft</c>. The
/// underlying invoice harness exposes the persisted context, the fake country strategy, and the captured
/// audit/publish lists.
/// </summary>
public sealed class WarehouseConsumerTestHarness
{
    private WarehouseConsumerTestHarness(InvoiceServiceTestHarness invoices, IWarehouseInvoiceDraftFactory factory)
    {
        Invoices = invoices;
        Factory = factory;
    }

    /// <summary>The underlying invoice-service harness (SQLite context, country strategy, captured events).</summary>
    public InvoiceServiceTestHarness Invoices { get; }

    /// <summary>The real draft factory the consumers delegate to (the shared create path).</summary>
    public IWarehouseInvoiceDraftFactory Factory { get; }

    /// <summary>Builds a harness over the supplied invoice-service harness.</summary>
    /// <param name="invoices">The invoice-service harness providing the real create path.</param>
    /// <returns>A wired consumer harness.</returns>
    public static WarehouseConsumerTestHarness Build(InvoiceServiceTestHarness invoices)
    {
        ArgumentNullException.ThrowIfNull(invoices);

        WarehouseInvoiceDraftFactory factory = new(invoices.Service, invoices.Country);
        return new WarehouseConsumerTestHarness(invoices, factory);
    }

    /// <summary>Runs the goods-receipt consumer over the supplied event.</summary>
    /// <param name="event">The goods-receipt event.</param>
    /// <returns>A task completing when consumption finishes.</returns>
    public Task ConsumeAsync(GoodsReceiptCompletedEvent @event)
    {
        Finance.Invoices.API.Consumers.GoodsReceiptCompletedConsumer consumer = new(
            Factory, NullLogger<Finance.Invoices.API.Consumers.GoodsReceiptCompletedConsumer>.Instance);
        return consumer.Consume(ContextFor(@event));
    }

    /// <summary>Runs the shipment consumer over the supplied event.</summary>
    /// <param name="event">The shipment event.</param>
    /// <returns>A task completing when consumption finishes.</returns>
    public Task ConsumeAsync(ShipmentCompletedEvent @event)
    {
        Finance.Invoices.API.Consumers.ShipmentCompletedConsumer consumer = new(
            Factory, NullLogger<Finance.Invoices.API.Consumers.ShipmentCompletedConsumer>.Instance);
        return consumer.Consume(ContextFor(@event));
    }

    /// <summary>Runs the customer-return consumer over the supplied event.</summary>
    /// <param name="event">The customer-return event.</param>
    /// <returns>A task completing when consumption finishes.</returns>
    public Task ConsumeAsync(CustomerReturnCompletedEvent @event)
    {
        Finance.Invoices.API.Consumers.CustomerReturnCompletedConsumer consumer = new(
            Factory, Invoices.Service, NullLogger<Finance.Invoices.API.Consumers.CustomerReturnCompletedConsumer>.Instance);
        return consumer.Consume(ContextFor(@event));
    }

    /// <summary>Runs the supplier-return consumer over the supplied event.</summary>
    /// <param name="event">The supplier-return event.</param>
    /// <returns>A task completing when consumption finishes.</returns>
    public Task ConsumeAsync(SupplierReturnShippedEvent @event)
    {
        Finance.Invoices.API.Consumers.SupplierReturnShippedConsumer consumer = new(
            Factory, NullLogger<Finance.Invoices.API.Consumers.SupplierReturnShippedConsumer>.Instance);
        return consumer.Consume(ContextFor(@event));
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
