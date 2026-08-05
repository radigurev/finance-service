using Finance.Payments.DBModel;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Finance.Payments.API.Tests.Fixtures;

/// <summary>
/// Creates a kept-alive SQLite in-memory <see cref="PaymentsDbContext"/> for the Payments unit tests
/// (SDD-PAY-001 §6, SDD-PAY-002 §6, SDD-PAY-003 §6 — EF unit tests use SQLite in-memory and run fully offline).
/// The owning test must dispose the returned <see cref="SqlitePaymentsDbContextScope"/> to release the
/// connection.
/// </summary>
public static class SqlitePaymentsDbContextFactory
{
    /// <summary>
    /// Opens a fresh in-memory SQLite connection and builds a <see cref="PaymentsDbContext"/> with the real
    /// model (payments schema, allocations, open-item projection, audit table, sequence table, outbox tables)
    /// created via <c>EnsureCreated</c>. The SQL-Server-only PK/rowversion/<c>DATETIMEOFFSET</c>/<c>DECIMAL</c>
    /// facets are rewritten for SQLite via <see cref="SqlitePaymentsModelCustomizer"/> and
    /// <see cref="SqlitePaymentsRowVersionInterceptor"/>.
    /// </summary>
    /// <returns>A disposable scope owning the connection, the context, and the interceptors.</returns>
    public static SqlitePaymentsDbContextScope Create()
    {
        SqliteConnection connection = new("DataSource=:memory:");
        connection.Open();

        SqlitePaymentsRowVersionInterceptor rowVersions = new();
        SqlitePaymentsCommandCounter commands = new();

        DbContextOptions<PaymentsDbContext> options = new DbContextOptionsBuilder<PaymentsDbContext>()
            .UseSqlite(connection)
            .AddInterceptors(rowVersions, commands)
            .ReplaceService<IModelCustomizer, SqlitePaymentsModelCustomizer>()
            .Options;

        PaymentsDbContext context = new(options);
        context.Database.EnsureCreated();

        return new SqlitePaymentsDbContextScope(connection, context, rowVersions, commands);
    }
}
