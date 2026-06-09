using Finance.Invoices.DBModel;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Finance.Invoices.API.Tests.Fixtures;

/// <summary>
/// Creates a kept-alive SQLite in-memory <see cref="InvoicesDbContext"/> for the Invoices unit tests
/// (SDD-INV-001 §6, SDD-INT-WH-001 §6 — EF unit tests use SQLite in-memory and run fully offline). The owning
/// test must dispose the returned <see cref="SqliteInvoicesDbContextScope"/> to release the connection.
/// </summary>
public static class SqliteInvoicesDbContextFactory
{
    /// <summary>
    /// Opens a fresh in-memory SQLite connection and builds an <see cref="InvoicesDbContext"/> with the real
    /// model (invoice schema, audit table, sequence table, outbox tables) created via <c>EnsureCreated</c>.
    /// The SQL-Server-only PK/rowversion generation facets are rewritten for SQLite via
    /// <see cref="SqliteInvoicesModelCustomizer"/> and <see cref="SqliteInvoicesRowVersionInterceptor"/>.
    /// </summary>
    /// <returns>A disposable scope owning the connection and the context.</returns>
    public static SqliteInvoicesDbContextScope Create()
    {
        SqliteConnection connection = new("DataSource=:memory:");
        connection.Open();

        DbContextOptions<InvoicesDbContext> options = new DbContextOptionsBuilder<InvoicesDbContext>()
            .UseSqlite(connection)
            .AddInterceptors(new SqliteInvoicesRowVersionInterceptor())
            .ReplaceService<IModelCustomizer, SqliteInvoicesModelCustomizer>()
            .Options;

        InvoicesDbContext context = new(options);
        context.Database.EnsureCreated();

        return new SqliteInvoicesDbContextScope(connection, context);
    }
}
