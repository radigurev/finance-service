using Finance.Journal.DBModel;
using Finance.Journal.DBModel.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using NUnit.Framework;

namespace Finance.Journal.API.Tests.Unit.Configurations;

/// <summary>
/// Unit tests for the additive source-document backstop on the <see cref="JournalEntry"/> mapping
/// (SDD-PAY-001 §2.5, SDD-FIN-002). They inspect the built SQL Server model so the production column facets and
/// the index filter are asserted directly.
/// <para>The UNIQUE FILTERED index <c>IX_JournalEntries_SourceDocument</c> is the DB backstop behind the
/// consumers' aggregate-level dedupe check: at most ONE <c>Posted</c> entry may ever exist per source document,
/// while drafts, reversed entries, and manual entries (both columns NULL) stay unconstrained. The
/// <c>[Status] = 'Posted'</c> filter term is expressible only because the status is stored as a string.</para>
/// </summary>
[TestFixture]
[Category("SDD-PAY-001")]
[Category("SDD-FIN-002")]
public sealed class JournalEntrySourceDocumentConfigurationTests
{
    private const string SourceDocumentIndexName = "IX_JournalEntries_SourceDocument";

    private IModel _model = null!;

    /// <summary>Builds the production SQL Server model once for inspection.</summary>
    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        DbContextOptions<JournalDbContext> options = new DbContextOptionsBuilder<JournalDbContext>()
            .UseSqlServer("Server=(localdb)\\mssqllocaldb;Database=model-only")
            .Options;

        using JournalDbContext context = new(options);
        _model = context.Model;
    }

    /// <summary>
    /// The backstop index is UNIQUE, spans (SourceDocumentType, SourceDocumentId) in that order, and is FILTERED
    /// to posted entries with both columns non-null (§2.5).
    /// </summary>
    [Test]
    public void JournalEntryConfiguration_HasUniqueFilteredIndexOnPostedSourceDocument()
    {
        // Arrange
        IEntityType entry = _model.FindEntityType(typeof(JournalEntry))!;

        // Act
        IIndex index = entry.GetIndexes().Single(candidate =>
            candidate.GetDatabaseName() == SourceDocumentIndexName);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(index.IsUnique, Is.True);
            Assert.That(
                index.Properties.Select(property => property.Name),
                Is.EqualTo(new[]
                {
                    nameof(JournalEntry.SourceDocumentType),
                    nameof(JournalEntry.SourceDocumentId)
                }));
            Assert.That(index.GetFilter(), Does.Contain("[SourceDocumentType] IS NOT NULL"));
            Assert.That(index.GetFilter(), Does.Contain("[SourceDocumentId] IS NOT NULL"));
            Assert.That(index.GetFilter(), Does.Contain("[Status] = 'Posted'"));
        });
    }

    /// <summary>
    /// Both source-document columns are NULLABLE with no default, so the already-applied migrations' existing
    /// rows keep them NULL and stay exempt from the filtered index; the type tag is capped at 40 characters
    /// (§2.5, §2.16).
    /// </summary>
    [Test]
    public void JournalEntryConfiguration_ConfiguresNullableSourceDocumentColumns_WithFortyCharTypeTag()
    {
        // Arrange
        IEntityType entry = _model.FindEntityType(typeof(JournalEntry))!;

        // Act
        IProperty sourceDocumentType = entry.FindProperty(nameof(JournalEntry.SourceDocumentType))!;
        IProperty sourceDocumentId = entry.FindProperty(nameof(JournalEntry.SourceDocumentId))!;

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(sourceDocumentType.IsNullable, Is.True);
            Assert.That(sourceDocumentType.GetMaxLength(), Is.EqualTo(40));
            Assert.That(sourceDocumentType.GetDefaultValueSql(), Is.Null);
            Assert.That(sourceDocumentType.GetDefaultValue(), Is.Null);
            Assert.That(sourceDocumentId.IsNullable, Is.True);
            Assert.That(sourceDocumentId.ClrType, Is.EqualTo(typeof(Guid?)));
            Assert.That(sourceDocumentId.GetDefaultValueSql(), Is.Null);
            Assert.That(sourceDocumentId.GetDefaultValue(), Is.Null);
        });
    }

    /// <summary>
    /// The status column is stored as a string, which is what makes the index's <c>[Status] = 'Posted'</c> filter
    /// term expressible at all (§2.5).
    /// </summary>
    [Test]
    public void JournalEntryConfiguration_StoresStatusAsString_SoThePostedFilterTermIsExpressible()
    {
        // Arrange
        IProperty status = _model.FindEntityType(typeof(JournalEntry))!
            .FindProperty(nameof(JournalEntry.Status))!;

        // Act
        Type providerClrType = status.GetProviderClrType() ?? status.ClrType;

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(providerClrType, Is.EqualTo(typeof(string)));
            Assert.That(status.GetMaxLength(), Is.EqualTo(20));
        });
    }
}
