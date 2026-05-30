using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Finance.Infrastructure.Sequences;

/// <summary>
/// Fluent-API mapping for <see cref="SequenceCounter"/> onto <c>infrastructure.Sequences</c>
/// (SDD-INFRA-003 §2.2). Each publishing service DbContext applies this configuration; the
/// physical table and migration land per-service (Batch 4+).
/// </summary>
public sealed class SequenceCounterConfiguration : IEntityTypeConfiguration<SequenceCounter>
{
    /// <summary>Maximum stored length of a composite sequence key.</summary>
    private const int KeyMaxLength = 64;

    /// <summary>The schema owning the shared sequence table.</summary>
    public const string SchemaName = "infrastructure";

    /// <summary>The table holding one row per composite sequence key.</summary>
    public const string TableName = "Sequences";

    /// <summary>Configures table, schema, primary key, and column facets for the counter entity.</summary>
    /// <param name="builder">The entity type builder supplied by EF Core.</param>
    public void Configure(EntityTypeBuilder<SequenceCounter> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(TableName, SchemaName);

        builder.HasKey(counter => counter.Key)
            .HasName("PK_Sequences");

        builder.Property(counter => counter.Key)
            .HasColumnName("Key")
            .HasMaxLength(KeyMaxLength)
            .IsRequired();

        builder.Property(counter => counter.CurrentValue)
            .HasColumnName("CurrentValue")
            .IsRequired();

        builder.Property(counter => counter.ModifiedAt)
            .HasColumnName("ModifiedAt")
            .HasDefaultValueSql("SYSDATETIMEOFFSET()")
            .IsRequired();
    }
}
