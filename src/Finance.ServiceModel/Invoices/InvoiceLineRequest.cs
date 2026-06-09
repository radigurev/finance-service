namespace Finance.ServiceModel.Invoices;

/// <summary>
/// Request body for a single invoice line on create / update (SDD-INV-001 §2.8, §3.1). The caller supplies
/// the quantity, unit price, and tax rate; the service computes net / tax / gross via <c>ICountryStrategy</c>.
/// </summary>
public sealed record InvoiceLineRequest
{
    /// <summary>Free-text description of the line item.</summary>
    public required string Description { get; init; }

    /// <summary>The billed quantity (must be strictly positive).</summary>
    public required decimal Quantity { get; init; }

    /// <summary>The per-unit net price (must be non-negative).</summary>
    public required decimal UnitPrice { get; init; }

    /// <summary>The tax rate applied to the line (decimal fraction; must be a rate the country recognizes).</summary>
    public required decimal TaxRate { get; init; }
}
