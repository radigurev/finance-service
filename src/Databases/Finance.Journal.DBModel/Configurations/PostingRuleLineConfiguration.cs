using Finance.Journal.DBModel.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Finance.Journal.DBModel.Configurations;

/// <summary>
/// EF Core Fluent-API configuration for the <see cref="PostingRuleLine"/> entity (SDD-FIN-006 §2.1). Maps
/// to <c>journal.PostingRuleLines</c> with the enum-as-string <c>DebitOrCredit</c>/<c>AmountSource</c>
/// columns and the reserved <c>DECIMAL(18,6)</c> percentage / <c>DECIMAL(18,2)</c> fixed-amount columns
/// (inert in v1; SDD-FIN-006 §5).
/// </summary>
public sealed class PostingRuleLineConfiguration : IEntityTypeConfiguration<PostingRuleLine>
{
    private const string AmountColumnType = "decimal(18,2)";
    private const string RateColumnType = "decimal(18,6)";

    /// <summary>Configures the table, columns, and indexes for <see cref="PostingRuleLine"/>.</summary>
    /// <param name="builder">The entity type builder supplied by EF Core.</param>
    public void Configure(EntityTypeBuilder<PostingRuleLine> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("PostingRuleLines", schema: "journal");

        builder.HasKey(line => line.Id).HasName("PK_PostingRuleLines");

        builder.Property(line => line.PostingRuleId).IsRequired();
        builder.Property(line => line.LineNumber).IsRequired();
        builder.Property(line => line.AccountSelector).IsRequired().HasMaxLength(20);
        builder.Property(line => line.DebitOrCredit).IsRequired().HasConversion<string>().HasMaxLength(10);
        builder.Property(line => line.AmountSource).IsRequired().HasConversion<string>().HasMaxLength(20);
        builder.Property(line => line.Percentage).HasColumnType(RateColumnType);
        builder.Property(line => line.FixedAmount).HasColumnType(AmountColumnType);

        builder.HasIndex(line => line.PostingRuleId).HasDatabaseName("IX_PostingRuleLines_PostingRuleId");
    }
}
