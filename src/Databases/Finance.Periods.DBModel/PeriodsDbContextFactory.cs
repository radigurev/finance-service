using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Finance.Periods.DBModel;

/// <summary>
/// Design-time factory used by <c>dotnet ef</c> tooling to create a <see cref="PeriodsDbContext"/> without
/// spinning up the API host.
/// </summary>
public sealed class PeriodsDbContextFactory : IDesignTimeDbContextFactory<PeriodsDbContext>
{
    /// <inheritdoc />
    public PeriodsDbContext CreateDbContext(string[] args)
    {
        DbContextOptionsBuilder<PeriodsDbContext> builder = new();
        string? connectionString = Environment.GetEnvironmentVariable("FINANCE_PERIODS_DB_CONNECTION")
            ?? "Server=localhost,1433;Database=finance_periods;User Id=sa;Password=Warehouse@Dev123;TrustServerCertificate=True";

        builder.UseSqlServer(connectionString, sql =>
            sql.MigrationsAssembly(typeof(PeriodsDbContext).Assembly.GetName().Name));

        return new PeriodsDbContext(builder.Options);
    }
}
