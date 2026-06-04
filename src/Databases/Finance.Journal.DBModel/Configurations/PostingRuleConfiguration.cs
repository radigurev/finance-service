using Finance.Journal.DBModel.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Finance.Journal.DBModel.Configurations;

/// <summary>
/// EF Core Fluent-API configuration for the <see cref="PostingRule"/> reference-data entity
/// (SDD-FIN-006 §2.1). Maps to <c>journal.PostingRules</c> with an <c>INT IDENTITY</c> PK, a unique
/// <c>RuleKey</c> index, a <c>rowversion</c> concurrency token, and the composed ordered line collection.
/// </summary>
public sealed class PostingRuleConfiguration : IEntityTypeConfiguration<PostingRule>
{
    /// <summary>Configures the table, columns, indexes, and relationships for <see cref="PostingRule"/>.</summary>
    /// <param name="builder">The entity type builder supplied by EF Core.</param>
    public void Configure(EntityTypeBuilder<PostingRule> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("PostingRules", schema: "journal");

        builder.HasKey(rule => rule.Id).HasName("PK_PostingRules");

        builder.Property(rule => rule.RuleKey).IsRequired().HasMaxLength(50);
        builder.Property(rule => rule.Description).IsRequired().HasMaxLength(500);
        builder.Property(rule => rule.CountryCode).IsRequired().HasMaxLength(3);
        builder.Property(rule => rule.IsActive).IsRequired().HasDefaultValue(true);
        builder.Property(rule => rule.CreatedAt).IsRequired().HasDefaultValueSql("SYSDATETIMEOFFSET()");
        builder.Property(rule => rule.UpdatedAt);
        builder.Property(rule => rule.RowVersion).IsRowVersion();

        builder.HasIndex(rule => rule.RuleKey)
            .IsUnique()
            .HasDatabaseName("UQ_PostingRules_RuleKey");
        builder.HasIndex(rule => rule.CountryCode).HasDatabaseName("IX_PostingRules_CountryCode");

        builder.HasMany(rule => rule.Lines)
            .WithOne(line => line.PostingRule)
            .HasForeignKey(line => line.PostingRuleId)
            .HasConstraintName("FK_PostingRuleLines_PostingRules")
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(rule => rule.Lines).AutoInclude();
    }
}
