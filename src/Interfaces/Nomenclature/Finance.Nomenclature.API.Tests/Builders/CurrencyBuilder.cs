using Finance.Nomenclature.DBModel.Models;

namespace Finance.Nomenclature.API.Tests.Builders;

/// <summary>
/// Builds <see cref="Currency"/> entities for the Nomenclature unit tests. Default values produce a
/// valid, active currency; tests override only the fields under test.
/// </summary>
public sealed class CurrencyBuilder
{
    private string _isoCode = "BGN";
    private string _name = "Bulgarian Lev";
    private string? _symbol = "лв";
    private bool _isActive = true;

    /// <summary>Creates a new builder seeded with valid defaults.</summary>
    /// <returns>A new <see cref="CurrencyBuilder"/>.</returns>
    public static CurrencyBuilder Create() => new();

    /// <summary>Sets the ISO 4217 alphabetic code.</summary>
    /// <param name="isoCode">The three-letter currency code.</param>
    /// <returns>This builder.</returns>
    public CurrencyBuilder WithIsoCode(string isoCode)
    {
        _isoCode = isoCode;
        return this;
    }

    /// <summary>Sets the currency name.</summary>
    /// <param name="name">The human-readable currency name.</param>
    /// <returns>This builder.</returns>
    public CurrencyBuilder WithName(string name)
    {
        _name = name;
        return this;
    }

    /// <summary>Sets the display symbol.</summary>
    /// <param name="symbol">The optional display symbol.</param>
    /// <returns>This builder.</returns>
    public CurrencyBuilder WithSymbol(string? symbol)
    {
        _symbol = symbol;
        return this;
    }

    /// <summary>Sets the active flag.</summary>
    /// <param name="isActive">Whether the currency is active.</param>
    /// <returns>This builder.</returns>
    public CurrencyBuilder WithIsActive(bool isActive)
    {
        _isActive = isActive;
        return this;
    }

    /// <summary>Builds the configured <see cref="Currency"/> entity.</summary>
    /// <returns>A new <see cref="Currency"/> with a server-set creation timestamp.</returns>
    public Currency Build() => new()
    {
        IsoCode = _isoCode,
        Name = _name,
        Symbol = _symbol,
        IsActive = _isActive,
        CreatedAt = DateTimeOffset.UtcNow
    };
}
