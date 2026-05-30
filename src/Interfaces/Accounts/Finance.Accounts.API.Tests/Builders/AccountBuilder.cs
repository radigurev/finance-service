using Finance.Accounts.DBModel.Models;
using Finance.Common.Enums;

namespace Finance.Accounts.API.Tests.Builders;

/// <summary>
/// Builds <see cref="Account"/> entities for the Accounts unit tests. Default values produce a valid,
/// active Bulgarian asset account; tests override only the fields under test.
/// </summary>
public sealed class AccountBuilder
{
    private string _code = "304";
    private string _name = "Стоки";
    private AccountType _type = AccountType.Asset;
    private int? _parentId;
    private bool _isActive = true;
    private string _countryCode = "BG";

    /// <summary>Creates a new builder seeded with valid defaults.</summary>
    /// <returns>A new <see cref="AccountBuilder"/>.</returns>
    public static AccountBuilder Create() => new();

    /// <summary>Sets the account code.</summary>
    /// <param name="code">The country-specific account code.</param>
    /// <returns>This builder.</returns>
    public AccountBuilder WithCode(string code)
    {
        _code = code;
        return this;
    }

    /// <summary>Sets the account name.</summary>
    /// <param name="name">The human-readable account name.</param>
    /// <returns>This builder.</returns>
    public AccountBuilder WithName(string name)
    {
        _name = name;
        return this;
    }

    /// <summary>Sets the account type.</summary>
    /// <param name="type">The account classification.</param>
    /// <returns>This builder.</returns>
    public AccountBuilder WithType(AccountType type)
    {
        _type = type;
        return this;
    }

    /// <summary>Sets the parent account identifier.</summary>
    /// <param name="parentId">The optional parent account id.</param>
    /// <returns>This builder.</returns>
    public AccountBuilder WithParentId(int? parentId)
    {
        _parentId = parentId;
        return this;
    }

    /// <summary>Sets the active flag.</summary>
    /// <param name="isActive">Whether the account is active.</param>
    /// <returns>This builder.</returns>
    public AccountBuilder WithIsActive(bool isActive)
    {
        _isActive = isActive;
        return this;
    }

    /// <summary>Sets the owning country code.</summary>
    /// <param name="countryCode">The ISO 3166-1 alpha-2 country code.</param>
    /// <returns>This builder.</returns>
    public AccountBuilder WithCountryCode(string countryCode)
    {
        _countryCode = countryCode;
        return this;
    }

    /// <summary>Builds the configured <see cref="Account"/> entity.</summary>
    /// <returns>A new <see cref="Account"/> with a server-set creation timestamp.</returns>
    public Account Build() => new()
    {
        Code = _code,
        Name = _name,
        Type = _type,
        ParentId = _parentId,
        IsActive = _isActive,
        CountryCode = _countryCode,
        CreatedAt = DateTimeOffset.UtcNow
    };
}
