using Finance.Invoices.DBModel;
using Microsoft.Data.Sqlite;

namespace Finance.Invoices.API.Tests.Fixtures;

/// <summary>
/// Owns the lifetime of a kept-alive SQLite in-memory connection and its <see cref="InvoicesDbContext"/>.
/// Disposing the scope disposes the context and closes the connection, dropping the in-memory database
/// (SDD-INV-001 §6, SDD-INT-WH-001 §6 — EF unit tests run fully offline against SQLite in-memory).
/// </summary>
public sealed class SqliteInvoicesDbContextScope : IDisposable
{
    private readonly SqliteConnection _connection;

    /// <summary>Creates a new scope wrapping the supplied connection and context.</summary>
    /// <param name="connection">The kept-alive SQLite in-memory connection.</param>
    /// <param name="context">The invoices database context bound to the connection.</param>
    public SqliteInvoicesDbContextScope(SqliteConnection connection, InvoicesDbContext context)
    {
        _connection = connection;
        Context = context;
    }

    /// <summary>The invoices database context bound to the in-memory connection.</summary>
    public InvoicesDbContext Context { get; }

    /// <summary>Disposes the context and closes the kept-alive connection.</summary>
    public void Dispose()
    {
        Context.Dispose();
        _connection.Dispose();
    }
}
