using Finance.Accounts.DBModel.Configurations;
using Finance.Accounts.DBModel.Models;
using Finance.Infrastructure.Audit.Configurations;
using Finance.Infrastructure.Audit.Entities;
using Finance.Infrastructure.Audit.Interfaces;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace Finance.Accounts.DBModel;

/// <summary>
/// EF Core database context for the Chart of Accounts service. Owns the <c>accounts</c> schema in the
/// <c>finance_accounts</c> database, the <c>audit</c> schema audit trail (SDD-AUDIT-001), and the
/// MassTransit transactional-outbox tables (SDD-INFRA-006). Implements <see cref="IAuditDbContext"/> so
/// the shared <c>AuditService&lt;AccountsDbContext&gt;</c> writes audit rows into the same transaction
/// as the change they describe.
/// </summary>
public sealed class AccountsDbContext : DbContext, IAuditDbContext
{
    /// <summary>Creates a new <see cref="AccountsDbContext"/>.</summary>
    public AccountsDbContext(DbContextOptions<AccountsDbContext> options) : base(options)
    {
    }

    /// <summary>Accounts in the chart.</summary>
    public DbSet<Account> Accounts => Set<Account>();

    /// <inheritdoc />
    public DbSet<OperationsEvent> OperationsEvents => Set<OperationsEvent>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("accounts");
        modelBuilder.ApplyConfiguration(new AccountConfiguration());
        modelBuilder.ApplyConfiguration(new OperationsEventConfiguration());

        modelBuilder.AddInboxStateEntity();
        modelBuilder.AddOutboxMessageEntity();
        modelBuilder.AddOutboxStateEntity();
    }
}
