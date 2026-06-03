using Finance.Periods.DBModel;
using Microsoft.Data.Sqlite;

namespace Finance.Periods.API.Tests.Fixtures;

/// <summary>
/// Owns the lifetime of a kept-alive SQLite in-memory connection and its <see cref="PeriodsDbContext"/>.
/// Disposing the scope disposes the context and closes the connection, dropping the in-memory database
/// (SDD-FIN-004 §6 — EF unit tests run fully offline against SQLite in-memory).
/// </summary>
public sealed class SqlitePeriodsDbContextScope : IDisposable
{
    private readonly SqliteConnection _connection;

    /// <summary>Creates a new scope wrapping the supplied connection and context.</summary>
    /// <param name="connection">The kept-alive SQLite in-memory connection.</param>
    /// <param name="context">The periods database context bound to the connection.</param>
    public SqlitePeriodsDbContextScope(SqliteConnection connection, PeriodsDbContext context)
    {
        _connection = connection;
        Context = context;
    }

    /// <summary>The periods database context bound to the in-memory connection.</summary>
    public PeriodsDbContext Context { get; }

    /// <summary>Disposes the context and closes the kept-alive connection.</summary>
    public void Dispose()
    {
        Context.Dispose();
        _connection.Dispose();
    }
}
