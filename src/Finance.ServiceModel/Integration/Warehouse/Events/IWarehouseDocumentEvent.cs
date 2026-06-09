namespace Finance.ServiceModel.Integration.Warehouse.Events;

/// <summary>
/// The consumption contract (SDD-INT-WH-001 §2.3) common to every Warehouse logistics event Finance turns
/// into a draft invoice. These are <b>Warehouse-owned</b> shapes mirrored locally because no shared contract
/// assembly is available in this repository; Finance binds only to the fields it depends on and tolerates
/// additional Warehouse fields it does not consume (forward-compatible). The four concrete events
/// (<c>GoodsReceiptCompletedEvent</c>, <c>ShipmentCompletedEvent</c>, <c>CustomerReturnCompletedEvent</c>,
/// <c>SupplierReturnShippedEvent</c>) implement this so the inbound consumers can map them through one shared
/// factory.
/// </summary>
public interface IWarehouseDocumentEvent
{
    /// <summary>The idempotency key (SDD-INFRA-006); also the per-event message identifier.</summary>
    Guid MessageId { get; }

    /// <summary>The originating correlation identifier, stamped onto the created draft and the NLog scope.</summary>
    string CorrelationId { get; }

    /// <summary>The instant the originating Warehouse change occurred; a fallback <c>IssueDate</c>.</summary>
    DateTimeOffset OccurredAt { get; }

    /// <summary>The Warehouse source-document identifier (goods-receipt / shipment / return id); the dedupe key.</summary>
    Guid SourceDocumentId { get; }

    /// <summary>The supplier (purchase/debit) or customer (sale/credit) reference, stored opaquely on the invoice.</summary>
    Guid CounterpartyId { get; }

    /// <summary>The document currency (ISO 4217, three characters).</summary>
    string CurrencyCode { get; }

    /// <summary>The line items the financial document is built from.</summary>
    IReadOnlyList<WarehouseDocumentLine> Lines { get; }
}
