using Finance.Journal.DBModel;
using Microsoft.Data.Sqlite;

namespace Finance.Journal.API.Tests.Fixtures;

/// <summary>
/// Owns the lifetime of a kept-alive SQLite in-memory connection and its <see cref="JournalDbContext"/>.
/// Disposing the scope disposes the context and closes the connection, dropping the in-memory database
/// (SDD-FIN-001 §6, SDD-FIN-002 §6 — EF unit tests run fully offline against SQLite in-memory).
/// </summary>
public sealed class SqliteJournalDbContextScope : IDisposable
{
    private readonly SqliteConnection _connection;

    /// <summary>Creates a new scope wrapping the supplied connection and context.</summary>
    /// <param name="connection">The kept-alive SQLite in-memory connection.</param>
    /// <param name="context">The journal database context bound to the connection.</param>
    public SqliteJournalDbContextScope(SqliteConnection connection, JournalDbContext context)
    {
        _connection = connection;
        Context = context;
    }

    /// <summary>The journal database context bound to the in-memory connection.</summary>
    public JournalDbContext Context { get; }

    /// <summary>Disposes the context and closes the kept-alive connection.</summary>
    public void Dispose()
    {
        Context.Dispose();
        _connection.Dispose();
    }
}
