using Finance.Infrastructure.Audit.Configurations;
using Finance.Infrastructure.Audit.Entities;
using Finance.Infrastructure.Audit.Interfaces;
using Finance.Infrastructure.Sequences;
using Finance.Journal.DBModel.Configurations;
using Finance.Journal.DBModel.Models;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace Finance.Journal.DBModel;

/// <summary>
/// EF Core database context for the Journal service. Owns the <c>journal</c> schema in the
/// <c>finance_journal</c> database, the <c>infrastructure.Sequences</c> gapless-number table
/// (SDD-INFRA-003), the <c>audit</c> schema audit trail (SDD-AUDIT-001), and the MassTransit
/// transactional-outbox tables (SDD-INFRA-006). Implements <see cref="IAuditDbContext"/> so the shared
/// <c>AuditService&lt;JournalDbContext&gt;</c> writes audit rows into the same transaction as the change.
/// </summary>
public sealed class JournalDbContext : DbContext, IAuditDbContext
{
    /// <summary>Creates a new <see cref="JournalDbContext"/>.</summary>
    /// <param name="options">The context options supplied by DI.</param>
    public JournalDbContext(DbContextOptions<JournalDbContext> options) : base(options)
    {
    }

    /// <summary>The journal-entry aggregate roots.</summary>
    public DbSet<JournalEntry> JournalEntries => Set<JournalEntry>();

    /// <summary>The journal-entry lines.</summary>
    public DbSet<JournalEntryLine> JournalEntryLines => Set<JournalEntryLine>();

    /// <summary>The append-only journal-entry status-transition history.</summary>
    public DbSet<JournalEntryStatusHistory> JournalEntryStatusHistory => Set<JournalEntryStatusHistory>();

    /// <summary>The editable posting-rule reference-data templates (SDD-FIN-006).</summary>
    public DbSet<PostingRule> PostingRules => Set<PostingRule>();

    /// <summary>The posting-rule lines (SDD-FIN-006).</summary>
    public DbSet<PostingRuleLine> PostingRuleLines => Set<PostingRuleLine>();

    /// <inheritdoc />
    public DbSet<OperationsEvent> OperationsEvents => Set<OperationsEvent>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.HasDefaultSchema("journal");

        modelBuilder.ApplyConfiguration(new JournalEntryConfiguration());
        modelBuilder.ApplyConfiguration(new JournalEntryLineConfiguration());
        modelBuilder.ApplyConfiguration(new JournalEntryStatusHistoryConfiguration());
        modelBuilder.ApplyConfiguration(new PostingRuleConfiguration());
        modelBuilder.ApplyConfiguration(new PostingRuleLineConfiguration());
        modelBuilder.ApplyConfiguration(new OperationsEventConfiguration());
        modelBuilder.ApplyConfiguration(new SequenceCounterConfiguration());

        modelBuilder.AddInboxStateEntity();
        modelBuilder.AddOutboxMessageEntity();
        modelBuilder.AddOutboxStateEntity();
    }
}
