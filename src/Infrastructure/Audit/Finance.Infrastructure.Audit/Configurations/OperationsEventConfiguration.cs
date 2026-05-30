using Finance.Infrastructure.Audit.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Finance.Infrastructure.Audit.Configurations;

/// <summary>
/// EF Core configuration for the <see cref="OperationsEvent"/> entity (SDD-AUDIT-001 §2.5).
/// Maps the audit row onto the dedicated <c>audit</c> schema so a per-service migration can
/// apply INSERT-only grants. Each service DbContext applies this configuration through its
/// <c>IAuditDbContext</c> implementation.
/// </summary>
public sealed class OperationsEventConfiguration : IEntityTypeConfiguration<OperationsEvent>
{
    /// <summary>Configures the table, columns, indexes, and immutability semantics for <see cref="OperationsEvent"/>.</summary>
    /// <param name="builder">The entity type builder supplied by EF Core.</param>
    public void Configure(EntityTypeBuilder<OperationsEvent> builder)
    {
        builder.ToTable("OperationsEvents", schema: "audit");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.EventType).IsRequired().HasMaxLength(100);
        builder.Property(e => e.EntityType).IsRequired().HasMaxLength(100);
        builder.Property(e => e.EntityId).IsRequired().HasMaxLength(100);
        builder.Property(e => e.UserId).IsRequired();
        builder.Property(e => e.Username).IsRequired().HasMaxLength(256);
        builder.Property(e => e.OccurredAt).IsRequired();
        builder.Property(e => e.CorrelationId).IsRequired().HasMaxLength(100);
        builder.Property(e => e.BeforeJson);
        builder.Property(e => e.AfterJson).IsRequired();
        builder.Property(e => e.Reason).HasMaxLength(1000);

        builder.HasIndex(e => e.OccurredAt);
        builder.HasIndex(e => new { e.EntityType, e.EntityId });
        builder.HasIndex(e => e.CorrelationId);
    }
}
