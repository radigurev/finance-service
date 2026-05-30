using Finance.Accounts.DBModel;
using Microsoft.Data.Sqlite;

namespace Finance.Accounts.API.Tests.Fixtures;

/// <summary>
/// Owns the lifetime of a kept-alive SQLite in-memory connection and its <see cref="AccountsDbContext"/>.
/// Disposing the scope disposes the context and closes the connection, dropping the in-memory database.
/// </summary>
public sealed class SqliteAccountsDbContextScope : IDisposable
{
    private readonly SqliteConnection _connection;

    /// <summary>Creates a new scope wrapping the supplied connection and context.</summary>
    /// <param name="connection">The kept-alive SQLite in-memory connection.</param>
    /// <param name="context">The accounts database context bound to the connection.</param>
    public SqliteAccountsDbContextScope(SqliteConnection connection, AccountsDbContext context)
    {
        _connection = connection;
        Context = context;
    }

    /// <summary>The accounts database context bound to the in-memory connection.</summary>
    public AccountsDbContext Context { get; }

    /// <summary>Disposes the context and closes the kept-alive connection.</summary>
    public void Dispose()
    {
        Context.Dispose();
        _connection.Dispose();
    }
}
