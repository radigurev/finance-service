using Finance.Country.Abstractions;
using Finance.Invoices.DBModel.Models;

namespace Finance.Invoices.API.Services;

/// <summary>
/// Computes per-line and document totals for an invoice via <see cref="ICountryStrategy"/> (SDD-INV-001 §2.8,
/// SDD-FIN-005). All arithmetic is <c>decimal</c>; tax rounding is delegated to
/// <see cref="ICountryStrategy.ApplyTaxRounding"/> so the core never inlines a rounding mode. Pure and
/// side-effect-free.
/// </summary>
public sealed class InvoiceTotalsCalculator
{
    private const int NetDecimals = 2;

    private readonly ICountryStrategy _countryStrategy;

    /// <summary>Creates a new <see cref="InvoiceTotalsCalculator"/>.</summary>
    /// <param name="countryStrategy">The country strategy owning tax rounding (SDD-CTRY-001).</param>
    public InvoiceTotalsCalculator(ICountryStrategy countryStrategy)
    {
        ArgumentNullException.ThrowIfNull(countryStrategy);
        _countryStrategy = countryStrategy;
    }

    /// <summary>
    /// Computes and stamps <see cref="InvoiceLine.LineNet"/>, <see cref="InvoiceLine.LineTax"/>, and
    /// <see cref="InvoiceLine.LineGross"/> on each line, then sums them into the header
    /// <see cref="Invoice.NetTotal"/>, <see cref="Invoice.TaxTotal"/>, and <see cref="Invoice.GrossTotal"/>.
    /// </summary>
    /// <param name="invoice">The invoice whose line and header totals are (re)computed.</param>
    public void Recompute(Invoice invoice)
    {
        ArgumentNullException.ThrowIfNull(invoice);

        decimal netTotal = 0m;
        decimal taxTotal = 0m;
        decimal grossTotal = 0m;

        foreach (InvoiceLine line in invoice.Lines)
        {
            ComputeLine(line);
            netTotal += line.LineNet;
            taxTotal += line.LineTax;
            grossTotal += line.LineGross;
        }

        invoice.NetTotal = netTotal;
        invoice.TaxTotal = taxTotal;
        invoice.GrossTotal = grossTotal;
    }

    private void ComputeLine(InvoiceLine line)
    {
        line.LineNet = decimal.Round(line.Quantity * line.UnitPrice, NetDecimals, MidpointRounding.AwayFromZero);
        line.LineTax = _countryStrategy.ApplyTaxRounding(line.LineNet * line.TaxRate);
        line.LineGross = line.LineNet + line.LineTax;
    }
}
