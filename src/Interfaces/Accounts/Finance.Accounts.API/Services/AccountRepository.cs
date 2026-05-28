using Finance.Accounts.API.Interfaces;
using Finance.Accounts.DBModel;
using Finance.Accounts.DBModel.Models;
using Microsoft.EntityFrameworkCore;

namespace Finance.Accounts.API.Services;

/// <summary>
/// EF Core implementation of <see cref="IAccountRepository"/> backed by <see cref="AccountsDbContext"/>.
/// </summary>
public sealed class AccountRepository : IAccountRepository
{
    private readonly AccountsDbContext _db;

    /// <summary>Creates a new <see cref="AccountRepository"/>.</summary>
    public AccountRepository(AccountsDbContext db)
    {
        _db = db;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Account>> GetAllAsync(CancellationToken cancellationToken)
    {
        return await _db.Accounts
            .AsNoTracking()
            .OrderBy(a => a.CountryCode)
            .ThenBy(a => a.Code)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<Account?> GetByIdAsync(int id, CancellationToken cancellationToken)
    {
        return _db.Accounts.FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
    }

    /// <inheritdoc />
    public Task<Account?> GetByCodeAsync(string countryCode, string code, CancellationToken cancellationToken)
    {
        return _db.Accounts
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.CountryCode == countryCode && a.Code == code, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<Account> AddAsync(Account account, CancellationToken cancellationToken)
    {
        _db.Accounts.Add(account);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return account;
    }

    /// <inheritdoc />
    public async Task UpdateAsync(Account account, CancellationToken cancellationToken)
    {
        account.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
