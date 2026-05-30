using Finance.Infrastructure.Audit.Configurations;
using Finance.Infrastructure.Audit.Entities;
using Finance.Infrastructure.Audit.Interfaces;
using Finance.Nomenclature.DBModel.Configurations;
using Finance.Nomenclature.DBModel.Models;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace Finance.Nomenclature.DBModel;

/// <summary>
/// EF Core database context for the Nomenclature reference-data service. Owns the <c>nomenclature</c>
/// schema (currencies + exchange rates), the <c>audit</c> schema audit trail (SDD-AUDIT-001), and the
/// MassTransit transactional-outbox tables used to publish currency events (SDD-INFRA-006).
/// Implements <see cref="IAuditDbContext"/> so the shared
/// <c>AuditService&lt;NomenclatureDbContext&gt;</c> writes audit rows into the same transaction as the
/// change they describe (SDD-NOM-001 §2.0, §2.1).
/// </summary>
public sealed class NomenclatureDbContext : DbContext, IAuditDbContext
{
    /// <summary>Creates a new <see cref="NomenclatureDbContext"/>.</summary>
    /// <param name="options">The context options supplied by DI or the design-time factory.</param>
    public NomenclatureDbContext(DbContextOptions<NomenclatureDbContext> options) : base(options)
    {
    }

    /// <summary>The ISO 4217 currencies owned by this service.</summary>
    public DbSet<Currency> Currencies => Set<Currency>();

    /// <summary>The currency exchange rates owned by this service (read-only in Batch 5).</summary>
    public DbSet<ExchangeRate> ExchangeRates => Set<ExchangeRate>();

    /// <inheritdoc />
    public DbSet<OperationsEvent> OperationsEvents => Set<OperationsEvent>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("nomenclature");
        modelBuilder.ApplyConfiguration(new CurrencyConfiguration());
        modelBuilder.ApplyConfiguration(new ExchangeRateConfiguration());
        modelBuilder.ApplyConfiguration(new OperationsEventConfiguration());

        modelBuilder.AddInboxStateEntity();
        modelBuilder.AddOutboxMessageEntity();
        modelBuilder.AddOutboxStateEntity();
    }
}
