using Finance.Common.Enums;
using Finance.Invoices.API.Interfaces;
using Finance.ServiceModel.Integration.Warehouse.Events;
using Finance.ServiceModel.Invoices;
using Microsoft.Extensions.Logging;

namespace Finance.Invoices.API.Consumers;

/// <summary>
/// MassTransit consumer that turns a Warehouse <see cref="CustomerReturnCompletedEvent"/> into a draft
/// <b>Credit Note</b> (counterparty = customer) via the SDD-INV-001 create path (SDD-INT-WH-001 §2.2).
/// When the event references an originating shipment and a matching Finance sale invoice exists, the
/// Credit Note is linked to it via <c>CorrectsInvoiceId</c>; otherwise it is created standalone and the
/// operator links it on review — the consumer MUST NOT fail when no match is found (SDD-INT-WH-001 §2.6).
/// Wrapped transparently by <c>UseFinanceIdempotency()</c> (SDD-INFRA-006); it never confirms or posts.
/// </summary>
public sealed class CustomerReturnCompletedConsumer : WarehouseInvoiceConsumerBase<CustomerReturnCompletedEvent>
{
    private readonly IInvoiceService _invoices;

    /// <summary>Creates a new <see cref="CustomerReturnCompletedConsumer"/>.</summary>
    /// <param name="factory">The shared map-and-create draft factory.</param>
    /// <param name="invoices">The invoice service used to find the originating sale invoice for linkage.</param>
    /// <param name="logger">The consumer logger.</param>
    public CustomerReturnCompletedConsumer(
        IWarehouseInvoiceDraftFactory factory,
        IInvoiceService invoices,
        ILogger<CustomerReturnCompletedConsumer> logger)
        : base(factory, logger)
    {
        ArgumentNullException.ThrowIfNull(invoices);
        _invoices = invoices;
    }

    /// <inheritdoc />
    protected override InvoiceDocumentType DocumentType => InvoiceDocumentType.CreditNote;

    /// <inheritdoc />
    protected override string SourceDocumentType => WarehouseSourceDocumentTypes.CustomerReturn;

    /// <inheritdoc />
    protected override async Task<Guid?> ResolveCorrectsInvoiceIdAsync(
        CustomerReturnCompletedEvent message,
        CancellationToken cancellationToken)
    {
        if (message.OriginatingShipmentId is not { } shipmentId || shipmentId == Guid.Empty)
        {
            return null;
        }

        InvoiceDto? originatingSale = await _invoices
            .FindBySourceDocumentAsync(WarehouseSourceDocumentTypes.Shipment, shipmentId, cancellationToken)
            .ConfigureAwait(false);

        return originatingSale?.Id;
    }
}
