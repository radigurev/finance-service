using Finance.Journal.DBModel.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Finance.Journal.API.Tests.Fixtures;

/// <summary>
/// Post-processes the built Journal model so SQL-Server-only behaviour works on SQLite for the offline unit
/// tests (SDD-FIN-001 §6, SDD-FIN-002 §6). SQLite cannot evaluate the production <c>NEWSEQUENTIALID()</c> PK
/// default, auto-fill a <c>rowversion</c> token, or <c>ORDER BY</c> a <c>DateTimeOffset</c>. This customizer
/// drops the <see cref="JournalEntry.Id"/> generation (the application supplies the GUID), rewrites
/// <see cref="JournalEntry.RowVersion"/> to a never-generated concurrency token that
/// <see cref="SqliteJournalRowVersionInterceptor"/> stamps on each write, and applies a binary
/// <c>DateTimeOffset</c> converter so the default <c>EntryDate</c> ordering (SDD-FIN-002 §2.9) is sortable —
/// all leaving production code untouched.
/// </summary>
public sealed class SqliteJournalModelCustomizer : RelationalModelCustomizer
{
    private const decimal AmountScale = 100m;
    private const decimal RateScale = 1000000m;

    private static readonly DateTimeOffsetToBinaryConverter DateTimeOffsetConverter = new();

    private static readonly ValueConverter<decimal, long> AmountConverter =
        new(value => (long)decimal.Round(value * AmountScale, 0, MidpointRounding.AwayFromZero),
            stored => stored / AmountScale);

    private static readonly ValueConverter<decimal, long> RateConverter =
        new(value => (long)decimal.Round(value * RateScale, 0, MidpointRounding.AwayFromZero),
            stored => stored / RateScale);

    /// <summary>Creates the customizer with the supplied dependencies.</summary>
    /// <param name="dependencies">The model-customizer dependencies supplied by EF Core.</param>
    public SqliteJournalModelCustomizer(ModelCustomizerDependencies dependencies)
        : base(dependencies)
    {
    }

    /// <inheritdoc />
    public override void Customize(ModelBuilder modelBuilder, DbContext context)
    {
        base.Customize(modelBuilder, context);

        ApplyDateTimeOffsetConverter(modelBuilder);
        ApplyDecimalConverters(modelBuilder);

        IMutableEntityType? entryType = modelBuilder.Model.FindEntityType(typeof(JournalEntry));
        if (entryType is null)
        {
            return;
        }

        IMutableProperty? id = entryType.FindProperty(nameof(JournalEntry.Id));
        if (id is not null)
        {
            id.ValueGenerated = ValueGenerated.Never;
            id.SetDefaultValueSql(null);
        }

        IMutableProperty? createdAt = entryType.FindProperty(nameof(JournalEntry.CreatedAt));
        createdAt?.SetDefaultValueSql(null);

        IMutableProperty? rowVersion = entryType.FindProperty(nameof(JournalEntry.RowVersion));
        if (rowVersion is not null)
        {
            rowVersion.ValueGenerated = ValueGenerated.Never;
            rowVersion.IsConcurrencyToken = true;
        }

        RewritePostingRuleStoreGeneratedColumns(modelBuilder);
    }

    private static void RewritePostingRuleStoreGeneratedColumns(ModelBuilder modelBuilder)
    {
        IMutableEntityType? ruleType = modelBuilder.Model.FindEntityType(typeof(PostingRule));
        if (ruleType is null)
        {
            return;
        }

        IMutableProperty? createdAt = ruleType.FindProperty(nameof(PostingRule.CreatedAt));
        createdAt?.SetDefaultValueSql(null);

        IMutableProperty? rowVersion = ruleType.FindProperty(nameof(PostingRule.RowVersion));
        if (rowVersion is not null)
        {
            rowVersion.ValueGenerated = ValueGenerated.Never;
            rowVersion.IsConcurrencyToken = true;
        }
    }

    private static void ApplyDecimalConverters(ModelBuilder modelBuilder)
    {
        foreach (IMutableEntityType entity in modelBuilder.Model.GetEntityTypes())
        {
            foreach (IMutableProperty property in entity.GetProperties())
            {
                if (property.ClrType != typeof(decimal) && property.ClrType != typeof(decimal?))
                {
                    continue;
                }

                string? columnType = property.GetColumnType();
                bool isRate = columnType is not null && columnType.Contains(",6", StringComparison.OrdinalIgnoreCase);
                property.SetValueConverter(isRate ? RateConverter : AmountConverter);
                property.SetColumnType(null);
            }
        }
    }

    private static void ApplyDateTimeOffsetConverter(ModelBuilder modelBuilder)
    {
        foreach (IMutableEntityType entity in modelBuilder.Model.GetEntityTypes())
        {
            foreach (IMutableProperty property in entity.GetProperties())
            {
                if (property.ClrType == typeof(DateTimeOffset) || property.ClrType == typeof(DateTimeOffset?))
                {
                    property.SetValueConverter(DateTimeOffsetConverter);
                    property.SetDefaultValueSql(null);
                }
            }
        }
    }
}
