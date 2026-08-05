using Finance.Infrastructure.Audit.Configurations;
using Finance.Infrastructure.Audit.Entities;
using Finance.Infrastructure.Audit.Interfaces;
using Finance.Infrastructure.Sequences;
using Finance.Payments.DBModel.Configurations;
using Finance.Payments.DBModel.Models;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace Finance.Payments.DBModel;

/// <summary>
/// EF Core database context for the Payments service (SDD-PAY-001 §2.16, SDD-PAY-002 §2.12). Owns the
/// <c>payments</c> schema in the <c>finance_payments</c> database — the payment aggregate and its status
/// history, plus the SDD-PAY-002 allocation rows and the local invoice open-item projection — the
/// <c>infrastructure.Sequences</c> gapless-number table
/// (SDD-INFRA-003), the <c>audit</c> schema audit trail (SDD-AUDIT-001), and the MassTransit
/// transactional-outbox / inbox tables (SDD-INFRA-006). Implements <see cref="IAuditDbContext"/> so the shared
/// <c>AuditService&lt;PaymentsDbContext&gt;</c> writes audit rows into the same transaction as the change.
/// </summary>
public sealed class PaymentsDbContext : DbContext, IAuditDbContext
{
    /// <summary>Creates a new <see cref="PaymentsDbContext"/>.</summary>
    /// <param name="options">The context options supplied by DI.</param>
    public PaymentsDbContext(DbContextOptions<PaymentsDbContext> options) : base(options)
    {
    }

    /// <summary>The payment aggregate roots.</summary>
    public DbSet<Payment> Payments => Set<Payment>();

    /// <summary>The append-only payment status-transition history.</summary>
    public DbSet<PaymentStatusHistory> PaymentStatusHistory => Set<PaymentStatusHistory>();

    /// <summary>The sub-ledger payment-to-invoice match rows (SDD-PAY-002 §2.1).</summary>
    public DbSet<PaymentAllocation> PaymentAllocations => Set<PaymentAllocation>();

    /// <summary>
    /// The LOCAL, event-fed read projection of the invoices a payment may be matched against
    /// (SDD-PAY-002 §2.2). It exists so allocation and aging never cross-join <c>finance_invoices</c>.
    /// </summary>
    public DbSet<InvoiceOpenItem> InvoiceOpenItems => Set<InvoiceOpenItem>();

    /// <inheritdoc />
    public DbSet<OperationsEvent> OperationsEvents => Set<OperationsEvent>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.HasDefaultSchema("payments");

        modelBuilder.ApplyConfiguration(new PaymentConfiguration());
        modelBuilder.ApplyConfiguration(new PaymentStatusHistoryConfiguration());
        modelBuilder.ApplyConfiguration(new PaymentAllocationConfiguration());
        modelBuilder.ApplyConfiguration(new InvoiceOpenItemConfiguration());
        modelBuilder.ApplyConfiguration(new OperationsEventConfiguration());
        modelBuilder.ApplyConfiguration(new SequenceCounterConfiguration());

        modelBuilder.AddInboxStateEntity();
        modelBuilder.AddOutboxMessageEntity();
        modelBuilder.AddOutboxStateEntity();
    }
}
