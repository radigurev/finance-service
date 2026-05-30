using Finance.ServiceModel.Accounts;

namespace Finance.Accounts.API.Tests.Builders;

/// <summary>
/// Builds <see cref="UpdateAccountRequest"/> instances for the Accounts unit tests. Default values
/// produce a valid name change on an active account; tests override only the fields under test.
/// </summary>
public sealed class UpdateAccountRequestBuilder
{
    private string _name = "Доставчици (updated)";
    private bool _isActive = true;
    private string _rowVersion = string.Empty;

    /// <summary>Creates a new builder seeded with valid defaults.</summary>
    /// <returns>A new <see cref="UpdateAccountRequestBuilder"/>.</returns>
    public static UpdateAccountRequestBuilder Create() => new();

    /// <summary>Sets the account name.</summary>
    /// <param name="name">The account name.</param>
    /// <returns>This builder.</returns>
    public UpdateAccountRequestBuilder WithName(string name)
    {
        _name = name;
        return this;
    }

    /// <summary>Sets the active flag.</summary>
    /// <param name="isActive">Whether the account is active.</param>
    /// <returns>This builder.</returns>
    public UpdateAccountRequestBuilder WithIsActive(bool isActive)
    {
        _isActive = isActive;
        return this;
    }

    /// <summary>Sets the base64-encoded row-version concurrency token.</summary>
    /// <param name="rowVersion">The base64 row version captured from the prior read.</param>
    /// <returns>This builder.</returns>
    public UpdateAccountRequestBuilder WithRowVersion(string rowVersion)
    {
        _rowVersion = rowVersion;
        return this;
    }

    /// <summary>Sets the row version from raw bytes, base64-encoding them.</summary>
    /// <param name="rowVersion">The raw row-version bytes.</param>
    /// <returns>This builder.</returns>
    public UpdateAccountRequestBuilder WithRowVersionBytes(byte[] rowVersion)
    {
        _rowVersion = Convert.ToBase64String(rowVersion);
        return this;
    }

    /// <summary>Builds the configured <see cref="UpdateAccountRequest"/>.</summary>
    /// <returns>A new <see cref="UpdateAccountRequest"/>.</returns>
    public UpdateAccountRequest Build() => new()
    {
        Name = _name,
        IsActive = _isActive,
        RowVersion = _rowVersion
    };
}
