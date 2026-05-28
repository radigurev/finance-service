using Finance.Accounts.DBModel.Models;

namespace Finance.Accounts.API.Interfaces;

/// <summary>
/// Persistence contract for <see cref="Account"/> records.
/// </summary>
public interface IAccountRepository
{
    /// <summary>Returns all accounts in the chart.</summary>
    Task<IReadOnlyList<Account>> GetAllAsync(CancellationToken cancellationToken);

    /// <summary>Returns the account with the given surrogate ID, or null.</summary>
    Task<Account?> GetByIdAsync(int id, CancellationToken cancellationToken);

    /// <summary>Returns the account for the given country + code, or null.</summary>
    Task<Account?> GetByCodeAsync(string countryCode, string code, CancellationToken cancellationToken);

    /// <summary>Adds a new account and persists changes.</summary>
    Task<Account> AddAsync(Account account, CancellationToken cancellationToken);

    /// <summary>Updates an existing account and persists changes.</summary>
    Task UpdateAsync(Account account, CancellationToken cancellationToken);
}
