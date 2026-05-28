using Finance.Accounts.DBModel.Configurations;
using Finance.Accounts.DBModel.Models;
using Microsoft.EntityFrameworkCore;

namespace Finance.Accounts.DBModel;

/// <summary>
/// EF Core database context for the Chart of Accounts service.
/// Owns the <c>accounts</c> schema in the <c>finance_accounts</c> database.
/// </summary>
public sealed class AccountsDbContext : DbContext
{
    /// <summary>Creates a new <see cref="AccountsDbContext"/>.</summary>
    public AccountsDbContext(DbContextOptions<AccountsDbContext> options) : base(options)
    {
    }

    /// <summary>Accounts in the chart.</summary>
    public DbSet<Account> Accounts => Set<Account>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("accounts");
        modelBuilder.ApplyConfiguration(new AccountConfiguration());
    }
}
