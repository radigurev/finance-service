namespace Finance.Invoices.API.Consumers;

/// <summary>
/// The <c>SourceDocumentType</c> tag values persisted on a draft invoice created from a Warehouse event
/// (SDD-INT-WH-001 §2.2). They make the document traceable to its Warehouse origin and form the dedupe key
/// together with the source-document id (§2.1.2).
/// </summary>
public static class WarehouseSourceDocumentTypes
{
    /// <summary>A goods-receipt source document (→ draft purchase invoice).</summary>
    public const string GoodsReceipt = "GoodsReceipt";

    /// <summary>A shipment source document (→ draft sale invoice).</summary>
    public const string Shipment = "Shipment";

    /// <summary>A customer-return source document (→ draft credit note).</summary>
    public const string CustomerReturn = "CustomerReturn";

    /// <summary>A supplier-return source document (→ draft debit note).</summary>
    public const string SupplierReturn = "SupplierReturn";
}
