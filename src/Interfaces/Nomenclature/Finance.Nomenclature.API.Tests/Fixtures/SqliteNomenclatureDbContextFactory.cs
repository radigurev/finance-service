using Finance.Nomenclature.DBModel;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Finance.Nomenclature.API.Tests.Fixtures;

/// <summary>
/// Creates a kept-alive SQLite in-memory <see cref="NomenclatureDbContext"/> for the Nomenclature unit
/// tests (SDD-NOM-001 §6 — EF unit tests use SQLite in-memory and run fully offline). The owning test
/// must dispose the returned <see cref="SqliteNomenclatureDbContextScope"/> to release the connection.
/// </summary>
public static class SqliteNomenclatureDbContextFactory
{
    /// <summary>
    /// Opens a fresh in-memory SQLite connection and builds a <see cref="NomenclatureDbContext"/> with the
    /// real model (nomenclature schema, audit table, outbox tables) created via <c>EnsureCreated</c>.
    /// </summary>
    /// <returns>A disposable scope owning the connection and the context.</returns>
    public static SqliteNomenclatureDbContextScope Create()
    {
        SqliteConnection connection = new("DataSource=:memory:");
        connection.Open();

        DbContextOptions<NomenclatureDbContext> options = new DbContextOptionsBuilder<NomenclatureDbContext>()
            .UseSqlite(connection)
            .AddInterceptors(new SqliteRowVersionInterceptor())
            .ReplaceService<IModelCustomizer, SqliteRowVersionModelCustomizer>()
            .Options;

        NomenclatureDbContext context = new(options);
        context.Database.EnsureCreated();

        return new SqliteNomenclatureDbContextScope(connection, context);
    }
}
