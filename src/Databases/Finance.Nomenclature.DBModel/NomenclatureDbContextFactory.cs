using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Finance.Nomenclature.DBModel;

/// <summary>
/// Design-time factory used by <c>dotnet ef</c> tooling to create a
/// <see cref="NomenclatureDbContext"/> without spinning up the API host.
/// </summary>
public sealed class NomenclatureDbContextFactory : IDesignTimeDbContextFactory<NomenclatureDbContext>
{
    /// <inheritdoc />
    public NomenclatureDbContext CreateDbContext(string[] args)
    {
        DbContextOptionsBuilder<NomenclatureDbContext> builder = new();
        string? connectionString = Environment.GetEnvironmentVariable("FINANCE_NOMENCLATURE_DB_CONNECTION")
            ?? "Server=localhost,1433;Database=finance_nomenclature;User Id=sa;Password=Warehouse@Dev123;TrustServerCertificate=True";

        builder.UseSqlServer(connectionString, sql =>
            sql.MigrationsAssembly(typeof(NomenclatureDbContext).Assembly.GetName().Name));

        return new NomenclatureDbContext(builder.Options);
    }
}
