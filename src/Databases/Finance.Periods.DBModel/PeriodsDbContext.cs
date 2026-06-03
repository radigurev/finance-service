using Finance.Infrastructure.Audit.Configurations;
using Finance.Infrastructure.Audit.Entities;
using Finance.Infrastructure.Audit.Interfaces;
using Finance.Periods.DBModel.Configurations;
using Finance.Periods.DBModel.Models;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace Finance.Periods.DBModel;

/// <summary>
/// EF Core database context for the Periods service. Owns the <c>periods</c> schema in the
/// <c>finance_periods</c> database, the <c>audit</c> schema audit trail (SDD-AUDIT-001), and the MassTransit
/// transactional-outbox tables (SDD-INFRA-006). Implements <see cref="IAuditDbContext"/> so the shared
/// <c>AuditService&lt;PeriodsDbContext&gt;</c> writes audit rows into the same transaction as the change.
/// </summary>
public sealed class PeriodsDbContext : DbContext, IAuditDbContext
{
    /// <summary>Creates a new <see cref="PeriodsDbContext"/>.</summary>
    /// <param name="options">The context options supplied by DI.</param>
    public PeriodsDbContext(DbContextOptions<PeriodsDbContext> options) : base(options)
    {
    }

    /// <summary>The fiscal-period aggregate roots.</summary>
    public DbSet<FiscalPeriod> FiscalPeriods => Set<FiscalPeriod>();

    /// <summary>The append-only fiscal-period status-transition history.</summary>
    public DbSet<FiscalPeriodStatusHistory> FiscalPeriodStatusHistory => Set<FiscalPeriodStatusHistory>();

    /// <inheritdoc />
    public DbSet<OperationsEvent> OperationsEvents => Set<OperationsEvent>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.HasDefaultSchema("periods");

        modelBuilder.ApplyConfiguration(new FiscalPeriodConfiguration());
        modelBuilder.ApplyConfiguration(new FiscalPeriodStatusHistoryConfiguration());
        modelBuilder.ApplyConfiguration(new OperationsEventConfiguration());

        modelBuilder.AddInboxStateEntity();
        modelBuilder.AddOutboxMessageEntity();
        modelBuilder.AddOutboxStateEntity();
    }
}
