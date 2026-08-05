using Finance.Payments.DBModel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Finance.Payments.API.Tests.Fixtures;

/// <summary>
/// Builds the PRODUCTION <see cref="PaymentsDbContext"/> model for the EF-configuration assertions
/// (SDD-PAY-001 §6.6, SDD-PAY-002 §6.5). No connection is ever opened — only <see cref="IModel"/> metadata is
/// read — and the SQLite test customizer is deliberately NOT applied, so the assertions see the real
/// <c>decimal(18,2)</c> / <c>decimal(18,6)</c> column types, the filtered unique indexes, and the
/// <c>rowversion</c> concurrency tokens exactly as configured for SQL Server.
/// </summary>
public static class PaymentsModelFactory
{
    /// <summary>Builds the production model without touching a database.</summary>
    /// <returns>A disposable context whose <see cref="DbContext.Model"/> is the production model.</returns>
    public static PaymentsDbContext CreateContext()
    {
        DbContextOptions<PaymentsDbContext> options = new DbContextOptionsBuilder<PaymentsDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;

        return new PaymentsDbContext(options);
    }
}
