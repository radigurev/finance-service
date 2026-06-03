using Finance.Periods.DBModel;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Finance.Periods.API.Tests.Fixtures;

/// <summary>
/// Creates a kept-alive SQLite in-memory <see cref="PeriodsDbContext"/> for the Periods unit tests
/// (SDD-FIN-004 §6 — EF unit tests use SQLite in-memory and run fully offline). The owning test must
/// dispose the returned <see cref="SqlitePeriodsDbContextScope"/> to release the connection.
/// </summary>
public static class SqlitePeriodsDbContextFactory
{
    /// <summary>
    /// Opens a fresh in-memory SQLite connection and builds a <see cref="PeriodsDbContext"/> with the real
    /// model (periods schema, audit table, outbox tables) created via <c>EnsureCreated</c>. The
    /// SQL-Server-only rowversion/default/<c>DateTimeOffset</c> facets are rewritten for SQLite via
    /// <see cref="SqlitePeriodsModelCustomizer"/> and <see cref="SqlitePeriodsRowVersionInterceptor"/>.
    /// </summary>
    /// <returns>A disposable scope owning the connection and the context.</returns>
    public static SqlitePeriodsDbContextScope Create()
    {
        SqliteConnection connection = new("DataSource=:memory:");
        connection.Open();

        DbContextOptions<PeriodsDbContext> options = new DbContextOptionsBuilder<PeriodsDbContext>()
            .UseSqlite(connection)
            .AddInterceptors(new SqlitePeriodsRowVersionInterceptor())
            .ReplaceService<IModelCustomizer, SqlitePeriodsModelCustomizer>()
            .Options;

        PeriodsDbContext context = new(options);
        context.Database.EnsureCreated();

        return new SqlitePeriodsDbContextScope(connection, context);
    }
}
