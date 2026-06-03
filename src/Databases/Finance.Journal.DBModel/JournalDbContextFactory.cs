using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Finance.Journal.DBModel;

/// <summary>
/// Design-time factory used by <c>dotnet ef</c> tooling to create a <see cref="JournalDbContext"/>
/// without spinning up the API host.
/// </summary>
public sealed class JournalDbContextFactory : IDesignTimeDbContextFactory<JournalDbContext>
{
    /// <inheritdoc />
    public JournalDbContext CreateDbContext(string[] args)
    {
        DbContextOptionsBuilder<JournalDbContext> builder = new();
        string? connectionString = Environment.GetEnvironmentVariable("FINANCE_JOURNAL_DB_CONNECTION")
            ?? "Server=localhost,1433;Database=finance_journal;User Id=sa;Password=Warehouse@Dev123;TrustServerCertificate=True";

        builder.UseSqlServer(connectionString, sql =>
            sql.MigrationsAssembly(typeof(JournalDbContext).Assembly.GetName().Name));

        return new JournalDbContext(builder.Options);
    }
}
