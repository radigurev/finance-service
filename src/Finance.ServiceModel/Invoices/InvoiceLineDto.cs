namespace Finance.ServiceModel.Invoices;

/// <summary>
/// Representation of a single invoice line exposed by the Invoices API (SDD-INV-001 §2.8). All monetary
/// fields are <c>decimal</c>; the tax rate is a decimal fraction (e.g. <c>0.20</c> for 20%).
/// </summary>
public sealed record InvoiceLineDto
{
    /// <summary>The 1-based ordinal for stable display ordering.</summary>
    public required int LineNumber { get; init; }

    /// <summary>Free-text description of the line item.</summary>
    public required string Description { get; init; }

    /// <summary>The billed quantity (strictly positive).</summary>
    public required decimal Quantity { get; init; }

    /// <summary>The per-unit net price (non-negative).</summary>
    public required decimal UnitPrice { get; init; }

    /// <summary>The tax rate applied to the line (decimal fraction; a rate the country recognizes).</summary>
    public required decimal TaxRate { get; init; }

    /// <summary>The computed net amount of the line (<c>Quantity × UnitPrice</c>, rounded).</summary>
    public required decimal LineNet { get; init; }

    /// <summary>The computed tax amount of the line (<c>LineNet × TaxRate</c>, country-rounded).</summary>
    public required decimal LineTax { get; init; }

    /// <summary>The computed gross amount of the line (<c>LineNet + LineTax</c>).</summary>
    public required decimal LineGross { get; init; }
}
