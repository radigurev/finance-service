using Finance.Journal.DBModel;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Finance.Journal.API.Tests.Fixtures;

/// <summary>
/// Creates a kept-alive SQLite in-memory <see cref="JournalDbContext"/> for the Journal unit tests
/// (SDD-FIN-001 §6, SDD-FIN-002 §6 — EF unit tests use SQLite in-memory and run fully offline). The owning
/// test must dispose the returned <see cref="SqliteJournalDbContextScope"/> to release the connection.
/// </summary>
public static class SqliteJournalDbContextFactory
{
    /// <summary>
    /// Opens a fresh in-memory SQLite connection and builds a <see cref="JournalDbContext"/> with the real
    /// model (journal schema, audit table, sequence table, outbox tables) created via <c>EnsureCreated</c>.
    /// The SQL-Server-only PK/rowversion generation facets are rewritten for SQLite via
    /// <see cref="SqliteJournalModelCustomizer"/> and <see cref="SqliteJournalRowVersionInterceptor"/>.
    /// </summary>
    /// <returns>A disposable scope owning the connection and the context.</returns>
    public static SqliteJournalDbContextScope Create()
    {
        SqliteConnection connection = new("DataSource=:memory:");
        connection.Open();

        DbContextOptions<JournalDbContext> options = new DbContextOptionsBuilder<JournalDbContext>()
            .UseSqlite(connection)
            .AddInterceptors(new SqliteJournalRowVersionInterceptor())
            .ReplaceService<IModelCustomizer, SqliteJournalModelCustomizer>()
            .Options;

        JournalDbContext context = new(options);
        context.Database.EnsureCreated();

        return new SqliteJournalDbContextScope(connection, context);
    }
}
