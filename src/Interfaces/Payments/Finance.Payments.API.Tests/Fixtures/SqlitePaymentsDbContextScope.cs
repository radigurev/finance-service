using Finance.Payments.DBModel;
using Microsoft.Data.Sqlite;

namespace Finance.Payments.API.Tests.Fixtures;

/// <summary>
/// Owns the lifetime of a kept-alive SQLite in-memory connection and its <see cref="PaymentsDbContext"/>.
/// Disposing the scope disposes the context and closes the connection, dropping the in-memory database
/// (SDD-PAY-001 §6, SDD-PAY-002 §6, SDD-PAY-003 §6 — EF unit tests run fully offline against SQLite in-memory).
/// </summary>
public sealed class SqlitePaymentsDbContextScope : IDisposable
{
    private readonly SqliteConnection _connection;

    /// <summary>Creates a new scope wrapping the supplied connection, context, and interceptors.</summary>
    /// <param name="connection">The kept-alive SQLite in-memory connection.</param>
    /// <param name="context">The payments database context bound to the connection.</param>
    /// <param name="rowVersions">The interceptor emulating the SQL Server store-generated columns.</param>
    /// <param name="commands">The interceptor counting executed database commands.</param>
    public SqlitePaymentsDbContextScope(
        SqliteConnection connection,
        PaymentsDbContext context,
        SqlitePaymentsRowVersionInterceptor rowVersions,
        SqlitePaymentsCommandCounter commands)
    {
        _connection = connection;
        Context = context;
        RowVersions = rowVersions;
        Commands = commands;
    }

    /// <summary>The payments database context bound to the in-memory connection.</summary>
    public PaymentsDbContext Context { get; }

    /// <summary>The row-version interceptor, also used to simulate a concurrent projection write.</summary>
    public SqlitePaymentsRowVersionInterceptor RowVersions { get; }

    /// <summary>The command counter used to pin the single-round-trip aging aggregation.</summary>
    public SqlitePaymentsCommandCounter Commands { get; }

    /// <summary>Disposes the context and closes the kept-alive connection.</summary>
    public void Dispose()
    {
        Context.Dispose();
        _connection.Dispose();
    }
}
