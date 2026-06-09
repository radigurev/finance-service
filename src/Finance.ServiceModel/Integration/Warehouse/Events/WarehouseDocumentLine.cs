namespace Finance.ServiceModel.Integration.Warehouse.Events;

/// <summary>
/// A single line item carried on a Warehouse logistics event (SDD-INT-WH-001 §2.3). This is a
/// <b>Warehouse-owned</b> contract shape mirrored locally because no shared contract assembly is available
/// in this repository; Finance binds to the subset of fields it depends on and tolerates additional
/// Warehouse fields it does not consume (forward-compatible — extra fields MUST NOT cause a failure).
/// The Invoice create path computes net / tax / gross from these inputs via <c>ICountryStrategy</c>; Finance
/// never trusts a Warehouse-supplied total.
/// </summary>
public sealed record WarehouseDocumentLine
{
    /// <summary>The Warehouse-owned product reference for the line.</summary>
    public required Guid ProductId { get; init; }

    /// <summary>The line quantity (must be strictly positive for the line to be usable).</summary>
    public required decimal Quantity { get; init; }

    /// <summary>The per-unit net price (must be non-negative for the line to be usable).</summary>
    public required decimal UnitPrice { get; init; }

    /// <summary>
    /// The tax rate applied to the line (decimal fraction, e.g. <c>0.20</c>). When omitted by Warehouse the
    /// consumer defaults to the country's standard rate via <c>ICountryStrategy</c> (SDD-INT-WH-001 §2.3).
    /// </summary>
    public decimal? TaxRate { get; init; }

    /// <summary>An optional free-text description; when absent the consumer derives a placeholder.</summary>
    public string? Description { get; init; }
}
