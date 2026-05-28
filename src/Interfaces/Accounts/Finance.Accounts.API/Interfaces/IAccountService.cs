using Finance.ServiceModel.Accounts;

namespace Finance.Accounts.API.Interfaces;

/// <summary>
/// Application service for managing the chart of accounts.
/// </summary>
public interface IAccountService
{
    /// <summary>Lists all accounts in the chart, ordered by country and code.</summary>
    Task<IReadOnlyList<AccountDto>> ListAsync(CancellationToken cancellationToken);

    /// <summary>Returns the account with the given ID, or null if not found.</summary>
    Task<AccountDto?> GetAsync(int id, CancellationToken cancellationToken);

    /// <summary>Creates a new account in the chart for the configured country.</summary>
    Task<AccountDto> CreateAsync(CreateAccountRequest request, string countryCode, CancellationToken cancellationToken);

    /// <summary>Updates the mutable fields of an existing account.</summary>
    Task<AccountDto?> UpdateAsync(int id, UpdateAccountRequest request, CancellationToken cancellationToken);
}
