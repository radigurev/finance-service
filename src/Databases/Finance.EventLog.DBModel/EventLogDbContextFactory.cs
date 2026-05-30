using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Finance.EventLog.DBModel;

/// <summary>
/// Design-time factory used by <c>dotnet ef</c> tooling to create an
/// <see cref="EventLogDbContext"/> without spinning up the API host.
/// </summary>
public sealed class EventLogDbContextFactory : IDesignTimeDbContextFactory<EventLogDbContext>
{
    /// <inheritdoc />
    public EventLogDbContext CreateDbContext(string[] args)
    {
        DbContextOptionsBuilder<EventLogDbContext> builder = new();
        string? connectionString = Environment.GetEnvironmentVariable("FINANCE_EVENTLOG_DB_CONNECTION")
            ?? "Server=localhost,1433;Database=finance_eventlog;User Id=sa;Password=Warehouse@Dev123;TrustServerCertificate=True";

        builder.UseSqlServer(connectionString, sql =>
            sql.MigrationsAssembly(typeof(EventLogDbContext).Assembly.GetName().Name));

        return new EventLogDbContext(builder.Options);
    }
}
