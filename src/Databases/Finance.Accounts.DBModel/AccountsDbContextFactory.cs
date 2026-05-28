using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Finance.Accounts.DBModel;

/// <summary>
/// Design-time factory used by <c>dotnet ef</c> tooling to create an
/// <see cref="AccountsDbContext"/> without spinning up the API host.
/// </summary>
public sealed class AccountsDbContextFactory : IDesignTimeDbContextFactory<AccountsDbContext>
{
    /// <inheritdoc />
    public AccountsDbContext CreateDbContext(string[] args)
    {
        DbContextOptionsBuilder<AccountsDbContext> builder = new();
        string? connectionString = Environment.GetEnvironmentVariable("FINANCE_ACCOUNTS_DB_CONNECTION")
            ?? "Server=localhost,1433;Database=finance_accounts;User Id=sa;Password=Warehouse@Dev123;TrustServerCertificate=True";

        builder.UseSqlServer(connectionString, sql =>
            sql.MigrationsAssembly(typeof(AccountsDbContext).Assembly.GetName().Name));

        return new AccountsDbContext(builder.Options);
    }
}
