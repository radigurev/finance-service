using Finance.ServiceModel.Nomenclature;

namespace Finance.Nomenclature.API.Tests.Builders;

/// <summary>
/// Builds <see cref="UpdateCurrencyRequest"/> instances for the Nomenclature unit tests. Default values
/// produce a valid request; tests override only the fields under test.
/// </summary>
public sealed class UpdateCurrencyRequestBuilder
{
    private string _name = "Bulgarian Lev";
    private string? _symbol = "лв";
    private bool _isActive = true;
    private string _rowVersion = Convert.ToBase64String(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });

    /// <summary>Creates a new builder seeded with valid defaults.</summary>
    /// <returns>A new <see cref="UpdateCurrencyRequestBuilder"/>.</returns>
    public static UpdateCurrencyRequestBuilder Create() => new();

    /// <summary>Sets the currency name.</summary>
    /// <param name="name">The human-readable currency name.</param>
    /// <returns>This builder.</returns>
    public UpdateCurrencyRequestBuilder WithName(string name)
    {
        _name = name;
        return this;
    }

    /// <summary>Sets the display symbol.</summary>
    /// <param name="symbol">The optional display symbol.</param>
    /// <returns>This builder.</returns>
    public UpdateCurrencyRequestBuilder WithSymbol(string? symbol)
    {
        _symbol = symbol;
        return this;
    }

    /// <summary>Sets the active flag.</summary>
    /// <param name="isActive">Whether the currency is active.</param>
    /// <returns>This builder.</returns>
    public UpdateCurrencyRequestBuilder WithIsActive(bool isActive)
    {
        _isActive = isActive;
        return this;
    }

    /// <summary>Sets the base64-encoded optimistic-concurrency row version.</summary>
    /// <param name="rowVersion">The base64 row-version token.</param>
    /// <returns>This builder.</returns>
    public UpdateCurrencyRequestBuilder WithRowVersion(string rowVersion)
    {
        _rowVersion = rowVersion;
        return this;
    }

    /// <summary>Builds the configured <see cref="UpdateCurrencyRequest"/>.</summary>
    /// <returns>A new <see cref="UpdateCurrencyRequest"/>.</returns>
    public UpdateCurrencyRequest Build() => new()
    {
        Name = _name,
        Symbol = _symbol,
        IsActive = _isActive,
        RowVersion = _rowVersion
    };
}
