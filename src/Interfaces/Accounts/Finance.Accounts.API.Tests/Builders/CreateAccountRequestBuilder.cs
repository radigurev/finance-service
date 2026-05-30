using Finance.Common.Enums;
using Finance.ServiceModel.Accounts;

namespace Finance.Accounts.API.Tests.Builders;

/// <summary>
/// Builds <see cref="CreateAccountRequest"/> instances for the Accounts unit tests. Default values
/// produce a valid request; tests override only the fields under test.
/// </summary>
public sealed class CreateAccountRequestBuilder
{
    private string _code = "401";
    private string _name = "Доставчици";
    private AccountType _type = AccountType.Liability;
    private int? _parentId;

    /// <summary>Creates a new builder seeded with valid defaults.</summary>
    /// <returns>A new <see cref="CreateAccountRequestBuilder"/>.</returns>
    public static CreateAccountRequestBuilder Create() => new();

    /// <summary>Sets the account code.</summary>
    /// <param name="code">The account code.</param>
    /// <returns>This builder.</returns>
    public CreateAccountRequestBuilder WithCode(string code)
    {
        _code = code;
        return this;
    }

    /// <summary>Sets the account name.</summary>
    /// <param name="name">The account name.</param>
    /// <returns>This builder.</returns>
    public CreateAccountRequestBuilder WithName(string name)
    {
        _name = name;
        return this;
    }

    /// <summary>Sets the account type.</summary>
    /// <param name="type">The account classification.</param>
    /// <returns>This builder.</returns>
    public CreateAccountRequestBuilder WithType(AccountType type)
    {
        _type = type;
        return this;
    }

    /// <summary>Sets the parent account identifier.</summary>
    /// <param name="parentId">The optional parent account id.</param>
    /// <returns>This builder.</returns>
    public CreateAccountRequestBuilder WithParentId(int? parentId)
    {
        _parentId = parentId;
        return this;
    }

    /// <summary>Builds the configured <see cref="CreateAccountRequest"/>.</summary>
    /// <returns>A new <see cref="CreateAccountRequest"/>.</returns>
    public CreateAccountRequest Build() => new()
    {
        Code = _code,
        Name = _name,
        Type = _type,
        ParentId = _parentId
    };
}
