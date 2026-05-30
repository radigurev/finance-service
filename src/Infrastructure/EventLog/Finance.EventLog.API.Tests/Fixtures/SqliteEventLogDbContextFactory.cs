using Finance.EventLog.DBModel;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Finance.EventLog.API.Tests.Fixtures;

/// <summary>
/// Creates a kept-alive SQLite in-memory <see cref="EventLogDbContext"/> for the EventLog unit tests
/// (SDD-EVTLOG-001 §6 — EF unit tests use SQLite in-memory and run fully offline). The owning test must
/// dispose the returned <see cref="SqliteEventLogDbContextScope"/> to release the connection.
/// </summary>
public static class SqliteEventLogDbContextFactory
{
    /// <summary>
    /// Opens a fresh in-memory SQLite connection and builds an <see cref="EventLogDbContext"/> with the
    /// real model (the <c>eventlog</c> schema's append-only table) created via <c>EnsureCreated</c>, with
    /// the SQL-Server-specific <c>ReceivedAt</c> default stripped for SQLite compatibility.
    /// </summary>
    /// <returns>A disposable scope owning the connection and the context.</returns>
    public static SqliteEventLogDbContextScope Create()
    {
        SqliteConnection connection = new("DataSource=:memory:");
        connection.Open();

        DbContextOptions<EventLogDbContext> options = new DbContextOptionsBuilder<EventLogDbContext>()
            .UseSqlite(connection)
            .ReplaceService<IModelCustomizer, SqliteEventLogModelCustomizer>()
            .Options;

        EventLogDbContext context = new(options);
        context.Database.EnsureCreated();

        return new SqliteEventLogDbContextScope(connection, context);
    }
}
