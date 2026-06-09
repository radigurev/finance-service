using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Finance.Invoices.DBModel;

/// <summary>
/// Design-time factory used by <c>dotnet ef</c> tooling to create an <see cref="InvoicesDbContext"/>
/// without spinning up the API host.
/// </summary>
public sealed class InvoicesDbContextFactory : IDesignTimeDbContextFactory<InvoicesDbContext>
{
    /// <inheritdoc />
    public InvoicesDbContext CreateDbContext(string[] args)
    {
        DbContextOptionsBuilder<InvoicesDbContext> builder = new();
        string? connectionString = Environment.GetEnvironmentVariable("FINANCE_INVOICES_DB_CONNECTION")
            ?? "Server=localhost,1433;Database=finance_invoices;User Id=sa;Password=Warehouse@Dev123;TrustServerCertificate=True";

        builder.UseSqlServer(connectionString, sql =>
            sql.MigrationsAssembly(typeof(InvoicesDbContext).Assembly.GetName().Name));

        return new InvoicesDbContext(builder.Options);
    }
}
