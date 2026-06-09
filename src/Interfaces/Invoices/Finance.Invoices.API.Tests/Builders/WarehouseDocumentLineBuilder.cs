using Finance.ServiceModel.Integration.Warehouse.Events;

namespace Finance.Invoices.API.Tests.Builders;

/// <summary>
/// Builds <see cref="WarehouseDocumentLine"/> instances for the Warehouse inbound-consumer tests
/// (SDD-INT-WH-001 §6). Defaults to a usable line (quantity 2, unit price 50, tax rate 20%); a test overrides
/// only what it exercises, including omitting the tax rate to assert the country-default fallback.
/// </summary>
public sealed class WarehouseDocumentLineBuilder
{
    private Guid _productId = Guid.NewGuid();
    private decimal _quantity = 2m;
    private decimal _unitPrice = 50m;
    private decimal? _taxRate = 0.20m;
    private string? _description = "Widget";

    /// <summary>Starts a new builder with usable defaults.</summary>
    /// <returns>A fresh builder.</returns>
    public static WarehouseDocumentLineBuilder Create() => new();

    /// <summary>Sets the product reference.</summary>
    /// <param name="productId">The product id.</param>
    /// <returns>This builder.</returns>
    public WarehouseDocumentLineBuilder WithProductId(Guid productId)
    {
        _productId = productId;
        return this;
    }

    /// <summary>Sets the line quantity.</summary>
    /// <param name="quantity">The quantity.</param>
    /// <returns>This builder.</returns>
    public WarehouseDocumentLineBuilder WithQuantity(decimal quantity)
    {
        _quantity = quantity;
        return this;
    }

    /// <summary>Sets the per-unit net price.</summary>
    /// <param name="unitPrice">The unit price.</param>
    /// <returns>This builder.</returns>
    public WarehouseDocumentLineBuilder WithUnitPrice(decimal unitPrice)
    {
        _unitPrice = unitPrice;
        return this;
    }

    /// <summary>Sets the tax rate (or <c>null</c> to omit it for the country-default fallback).</summary>
    /// <param name="taxRate">The optional tax rate fraction.</param>
    /// <returns>This builder.</returns>
    public WarehouseDocumentLineBuilder WithTaxRate(decimal? taxRate)
    {
        _taxRate = taxRate;
        return this;
    }

    /// <summary>Sets the optional line description.</summary>
    /// <param name="description">The description, or <c>null</c> for the derived placeholder.</param>
    /// <returns>This builder.</returns>
    public WarehouseDocumentLineBuilder WithDescription(string? description)
    {
        _description = description;
        return this;
    }

    /// <summary>Materializes the configured Warehouse line.</summary>
    /// <returns>The built <see cref="WarehouseDocumentLine"/>.</returns>
    public WarehouseDocumentLine Build() => new()
    {
        ProductId = _productId,
        Quantity = _quantity,
        UnitPrice = _unitPrice,
        TaxRate = _taxRate,
        Description = _description
    };
}
