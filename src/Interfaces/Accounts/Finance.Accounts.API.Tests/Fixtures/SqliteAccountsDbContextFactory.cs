using Finance.Accounts.DBModel;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Finance.Accounts.API.Tests.Fixtures;

/// <summary>
/// Creates a kept-alive SQLite in-memory <see cref="AccountsDbContext"/> for the Accounts unit tests
/// (SDD-ACCT-001 §6 — EF unit tests use SQLite in-memory and run fully offline). The owning test must
/// dispose the returned <see cref="SqliteAccountsDbContextScope"/> to release the connection.
/// </summary>
public static class SqliteAccountsDbContextFactory
{
    /// <summary>
    /// Opens a fresh in-memory SQLite connection and builds an <see cref="AccountsDbContext"/> with the
    /// real model (accounts schema, audit table, outbox tables) created via <c>EnsureCreated</c>.
    /// </summary>
    /// <returns>A disposable scope owning the connection and the context.</returns>
    public static SqliteAccountsDbContextScope Create()
    {
        SqliteConnection connection = new("DataSource=:memory:");
        connection.Open();

        DbContextOptions<AccountsDbContext> options = new DbContextOptionsBuilder<AccountsDbContext>()
            .UseSqlite(connection)
            .AddInterceptors(new SqliteRowVersionInterceptor())
            .ReplaceService<IModelCustomizer, SqliteRowVersionModelCustomizer>()
            .Options;

        AccountsDbContext context = new(options);
        context.Database.EnsureCreated();

        return new SqliteAccountsDbContextScope(connection, context);
    }
}
