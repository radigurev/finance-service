using Finance.Infrastructure.Audit.Configurations;
using Finance.Infrastructure.Audit.Entities;
using Finance.Infrastructure.Audit.Interfaces;
using Finance.Infrastructure.Sequences;
using Finance.Invoices.DBModel.Configurations;
using Finance.Invoices.DBModel.Models;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace Finance.Invoices.DBModel;

/// <summary>
/// EF Core database context for the Invoices service. Owns the <c>finance_invoices</c> schema in the
/// <c>finance_invoices</c> database, the <c>infrastructure.Sequences</c> gapless-number table
/// (SDD-INFRA-003), the <c>audit</c> schema audit trail (SDD-AUDIT-001), and the MassTransit
/// transactional-outbox / inbox tables (SDD-INFRA-006). Implements <see cref="IAuditDbContext"/> so the
/// shared <c>AuditService&lt;InvoicesDbContext&gt;</c> writes audit rows into the same transaction as the
/// change.
/// </summary>
public sealed class InvoicesDbContext : DbContext, IAuditDbContext
{
    /// <summary>Creates a new <see cref="InvoicesDbContext"/>.</summary>
    /// <param name="options">The context options supplied by DI.</param>
    public InvoicesDbContext(DbContextOptions<InvoicesDbContext> options) : base(options)
    {
    }

    /// <summary>The invoice aggregate roots.</summary>
    public DbSet<Invoice> Invoices => Set<Invoice>();

    /// <summary>The invoice lines.</summary>
    public DbSet<InvoiceLine> InvoiceLines => Set<InvoiceLine>();

    /// <summary>The append-only invoice status-transition history.</summary>
    public DbSet<InvoiceStatusHistory> InvoiceStatusHistory => Set<InvoiceStatusHistory>();

    /// <inheritdoc />
    public DbSet<OperationsEvent> OperationsEvents => Set<OperationsEvent>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.HasDefaultSchema("finance_invoices");

        modelBuilder.ApplyConfiguration(new InvoiceConfiguration());
        modelBuilder.ApplyConfiguration(new InvoiceLineConfiguration());
        modelBuilder.ApplyConfiguration(new InvoiceStatusHistoryConfiguration());
        modelBuilder.ApplyConfiguration(new OperationsEventConfiguration());
        modelBuilder.ApplyConfiguration(new SequenceCounterConfiguration());

        modelBuilder.AddInboxStateEntity();
        modelBuilder.AddOutboxMessageEntity();
        modelBuilder.AddOutboxStateEntity();
    }
}
