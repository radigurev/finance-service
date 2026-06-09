using Finance.ServiceModel.Invoices;

namespace Finance.Invoices.API.Tests.Builders;

/// <summary>
/// Builds <see cref="InvoiceLineRequest"/> instances for the Invoices unit tests. Defaults to a valid line
/// (quantity 1, unit price 100, tax rate 20%) so a test overrides only the field it exercises.
/// </summary>
public sealed class InvoiceLineRequestBuilder
{
    private string _description = "Line item";
    private decimal _quantity = 1m;
    private decimal _unitPrice = 100m;
    private decimal _taxRate = 0.20m;

    /// <summary>Starts a new builder with valid defaults.</summary>
    /// <returns>A fresh builder.</returns>
    public static InvoiceLineRequestBuilder Create() => new();

    /// <summary>Sets the line description.</summary>
    /// <param name="description">The description.</param>
    /// <returns>This builder.</returns>
    public InvoiceLineRequestBuilder WithDescription(string description)
    {
        _description = description;
        return this;
    }

    /// <summary>Sets the line quantity.</summary>
    /// <param name="quantity">The quantity.</param>
    /// <returns>This builder.</returns>
    public InvoiceLineRequestBuilder WithQuantity(decimal quantity)
    {
        _quantity = quantity;
        return this;
    }

    /// <summary>Sets the per-unit net price.</summary>
    /// <param name="unitPrice">The unit price.</param>
    /// <returns>This builder.</returns>
    public InvoiceLineRequestBuilder WithUnitPrice(decimal unitPrice)
    {
        _unitPrice = unitPrice;
        return this;
    }

    /// <summary>Sets the tax rate.</summary>
    /// <param name="taxRate">The tax rate fraction.</param>
    /// <returns>This builder.</returns>
    public InvoiceLineRequestBuilder WithTaxRate(decimal taxRate)
    {
        _taxRate = taxRate;
        return this;
    }

    /// <summary>Materializes the configured line request.</summary>
    /// <returns>The built <see cref="InvoiceLineRequest"/>.</returns>
    public InvoiceLineRequest Build() => new()
    {
        Description = _description,
        Quantity = _quantity,
        UnitPrice = _unitPrice,
        TaxRate = _taxRate
    };
}
