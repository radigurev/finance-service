using Finance.Common.Enums;
using Finance.Invoices.API.Consumers;
using Finance.ServiceModel.Integration.Warehouse.Events;

namespace Finance.Invoices.API.Interfaces;

/// <summary>
/// Shared map-and-create helper for the four Warehouse inbound consumers (SDD-INT-WH-001 §2.1-§2.3). It
/// contract-checks the event fields the consumer depends on, dedupes on the source document, maps the event
/// to an SDD-INV-001 <c>CreateInvoiceRequest</c> (defaulting a missing tax rate to the country's standard
/// rate), and creates the draft via the SAME <c>IInvoiceService</c> create path the manual endpoint uses —
/// never constructing an <see cref="Finance.Invoices.DBModel.Models.Invoice"/> directly and never
/// confirming/posting. A permanent business failure is returned as a
/// <see cref="WarehouseDraftOutcomeKind.PermanentFailure"/> outcome (the consumer logs + acknowledges); a
/// transient infrastructure failure propagates as an exception so MassTransit retries.
/// </summary>
public interface IWarehouseInvoiceDraftFactory
{
    /// <summary>
    /// Materializes (or dedupes) a draft invoice of <paramref name="documentType"/> and
    /// <paramref name="sourceDocumentType"/> from the supplied Warehouse event.
    /// </summary>
    /// <param name="event">The inbound Warehouse document event.</param>
    /// <param name="documentType">The invoice document type to create (SDD-INT-WH-001 §2.2).</param>
    /// <param name="sourceDocumentType">The source-document type tag (e.g. <c>GoodsReceipt</c>).</param>
    /// <param name="correctsInvoiceId">The original invoice this draft corrects, when linked; otherwise <c>null</c>.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>The terminal <see cref="WarehouseDraftOutcome"/> describing what happened.</returns>
    Task<WarehouseDraftOutcome> CreateDraftAsync(
        IWarehouseDocumentEvent @event,
        InvoiceDocumentType documentType,
        string sourceDocumentType,
        Guid? correctsInvoiceId,
        CancellationToken cancellationToken);
}
