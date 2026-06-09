using Finance.Common.Enums;
using Finance.Invoices.API.Interfaces;
using Finance.ServiceModel.Integration.Warehouse.Events;
using Microsoft.Extensions.Logging;

namespace Finance.Invoices.API.Consumers;

/// <summary>
/// MassTransit consumer that turns a Warehouse <see cref="ShipmentCompletedEvent"/> into a draft <b>Sale
/// Invoice</b> (counterparty = customer) via the SDD-INV-001 create path (SDD-INT-WH-001 §2.2). Wrapped
/// transparently by <c>UseFinanceIdempotency()</c> (SDD-INFRA-006); it never confirms or posts.
/// </summary>
public sealed class ShipmentCompletedConsumer : WarehouseInvoiceConsumerBase<ShipmentCompletedEvent>
{
    /// <summary>Creates a new <see cref="ShipmentCompletedConsumer"/>.</summary>
    /// <param name="factory">The shared map-and-create draft factory.</param>
    /// <param name="logger">The consumer logger.</param>
    public ShipmentCompletedConsumer(
        IWarehouseInvoiceDraftFactory factory,
        ILogger<ShipmentCompletedConsumer> logger)
        : base(factory, logger)
    {
    }

    /// <inheritdoc />
    protected override InvoiceDocumentType DocumentType => InvoiceDocumentType.SaleInvoice;

    /// <inheritdoc />
    protected override string SourceDocumentType => WarehouseSourceDocumentTypes.Shipment;
}
