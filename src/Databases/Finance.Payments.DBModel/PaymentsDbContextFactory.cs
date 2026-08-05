using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Finance.Payments.DBModel;

/// <summary>
/// Design-time factory used by <c>dotnet ef</c> tooling to create a <see cref="PaymentsDbContext"/> without
/// spinning up the API host (SDD-PAY-001 §2.16). Reads the connection string from
/// <c>FINANCE_PAYMENTS_DB_CONNECTION</c>.
/// </summary>
public sealed class PaymentsDbContextFactory : IDesignTimeDbContextFactory<PaymentsDbContext>
{
    /// <inheritdoc />
    public PaymentsDbContext CreateDbContext(string[] args)
    {
        DbContextOptionsBuilder<PaymentsDbContext> builder = new();
        string? connectionString = Environment.GetEnvironmentVariable("FINANCE_PAYMENTS_DB_CONNECTION")
            ?? "Server=localhost,1433;Database=finance_payments;User Id=sa;Password=Warehouse@Dev123;TrustServerCertificate=True";

        builder.UseSqlServer(connectionString, sql =>
            sql.MigrationsAssembly(typeof(PaymentsDbContext).Assembly.GetName().Name));

        return new PaymentsDbContext(builder.Options);
    }
}
