using Finance.ServiceModel.Nomenclature;

namespace Finance.Nomenclature.API.Tests.Builders;

/// <summary>
/// Builds <see cref="CreateCurrencyRequest"/> instances for the Nomenclature unit tests. Default values
/// produce a valid request; tests override only the fields under test.
/// </summary>
public sealed class CreateCurrencyRequestBuilder
{
    private string _isoCode = "USD";
    private string _name = "United States Dollar";
    private string? _symbol = "$";
    private bool _isActive = true;

    /// <summary>Creates a new builder seeded with valid defaults.</summary>
    /// <returns>A new <see cref="CreateCurrencyRequestBuilder"/>.</returns>
    public static CreateCurrencyRequestBuilder Create() => new();

    /// <summary>Sets the ISO 4217 alphabetic code.</summary>
    /// <param name="isoCode">The three-letter currency code.</param>
    /// <returns>This builder.</returns>
    public CreateCurrencyRequestBuilder WithIsoCode(string isoCode)
    {
        _isoCode = isoCode;
        return this;
    }

    /// <summary>Sets the currency name.</summary>
    /// <param name="name">The human-readable currency name.</param>
    /// <returns>This builder.</returns>
    public CreateCurrencyRequestBuilder WithName(string name)
    {
        _name = name;
        return this;
    }

    /// <summary>Sets the display symbol.</summary>
    /// <param name="symbol">The optional display symbol.</param>
    /// <returns>This builder.</returns>
    public CreateCurrencyRequestBuilder WithSymbol(string? symbol)
    {
        _symbol = symbol;
        return this;
    }

    /// <summary>Sets the active flag.</summary>
    /// <param name="isActive">Whether the currency is active on creation.</param>
    /// <returns>This builder.</returns>
    public CreateCurrencyRequestBuilder WithIsActive(bool isActive)
    {
        _isActive = isActive;
        return this;
    }

    /// <summary>Builds the configured <see cref="CreateCurrencyRequest"/>.</summary>
    /// <returns>A new <see cref="CreateCurrencyRequest"/>.</returns>
    public CreateCurrencyRequest Build() => new()
    {
        IsoCode = _isoCode,
        Name = _name,
        Symbol = _symbol,
        IsActive = _isActive
    };
}
