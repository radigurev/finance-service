namespace Finance.Invoices.DBModel.Models;

/// <summary>
/// A single line of an <see cref="Invoice"/> (SDD-INV-001 §2.8). Carries the billed quantity, unit price,
/// and tax rate plus the derived net / tax / gross amounts computed via <c>ICountryStrategy</c>. A line has
/// no independent lifecycle (composition). All monetary fields are <c>decimal</c>.
/// </summary>
public sealed class InvoiceLine
{
    /// <summary>Internal surrogate identifier (not externally exposed).</summary>
    public int Id { get; set; }

    /// <summary>Foreign key to the owning <see cref="Invoice"/>.</summary>
    public Guid InvoiceId { get; set; }

    /// <summary>Navigation to the owning invoice.</summary>
    public Invoice? Invoice { get; set; }

    /// <summary>The 1-based ordinal for stable display ordering.</summary>
    public int LineNumber { get; set; }

    /// <summary>Free-text description of the line item.</summary>
    public required string Description { get; set; }

    /// <summary>The billed quantity (strictly positive).</summary>
    public decimal Quantity { get; set; }

    /// <summary>The per-unit net price (non-negative).</summary>
    public decimal UnitPrice { get; set; }

    /// <summary>The tax rate applied to the line (decimal fraction; a rate the country recognizes).</summary>
    public decimal TaxRate { get; set; }

    /// <summary>The computed net amount of the line (<c>Quantity × UnitPrice</c>, rounded).</summary>
    public decimal LineNet { get; set; }

    /// <summary>The computed tax amount of the line (<c>LineNet × TaxRate</c>, country-rounded).</summary>
    public decimal LineTax { get; set; }

    /// <summary>The computed gross amount of the line (<c>LineNet + LineTax</c>).</summary>
    public decimal LineGross { get; set; }
}
