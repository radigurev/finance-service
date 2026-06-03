using AutoMapper;
using Finance.Common.Enums;
using Finance.Journal.API.Mapping;
using Finance.Journal.DBModel.Models;
using Finance.ServiceModel.Journal;
using NUnit.Framework;

namespace Finance.Journal.API.Tests.Unit.Mapping;

/// <summary>
/// Unit tests for <see cref="JournalMappingProfile"/> (SDD-FIN-001 §2.1). Asserts the AutoMapper
/// configuration is internally valid and that the entry → DTO map base64-encodes the row version and
/// orders the lines by line number, with no domain logic in the profile.
/// </summary>
[TestFixture]
[Category("SDD-FIN-001")]
public sealed class JournalMappingProfileTests
{
    private IMapper _mapper = null!;

    /// <summary>Builds the mapper from the Journal profile before each test.</summary>
    [SetUp]
    public void SetUp()
    {
        MapperConfiguration configuration = new(cfg => cfg.AddProfile<JournalMappingProfile>());
        _mapper = configuration.CreateMapper();
    }

    /// <summary>The Journal mapping configuration is internally consistent (SDD-FIN-001 §2.1).</summary>
    [Test]
    public void Configuration_IsValid()
    {
        // Arrange
        MapperConfiguration configuration = new(cfg => cfg.AddProfile<JournalMappingProfile>());

        // Act & Assert
        Assert.DoesNotThrow(() => configuration.AssertConfigurationIsValid());
    }

    /// <summary>An entry maps to a DTO with a base64 row version and line-number-ordered lines (SDD-FIN-001 §2.1).</summary>
    [Test]
    public void Map_JournalEntryToDto_EncodesRowVersionAndOrdersLines()
    {
        // Arrange
        byte[] rowVersion = { 1, 2, 3, 4, 5, 6, 7, 8 };
        JournalEntry entry = new()
        {
            Id = Guid.NewGuid(),
            EntryNumber = "JE-2026-000001",
            EntryDate = new DateTimeOffset(2026, 6, 3, 0, 0, 0, TimeSpan.Zero),
            Description = "Test",
            BaseCurrencyCode = "BGN",
            Status = JournalEntryStatus.Posted,
            CorrelationId = "corr",
            CreatedAt = new DateTimeOffset(2026, 6, 3, 0, 0, 0, TimeSpan.Zero),
            RowVersion = rowVersion,
            Lines =
            [
                new JournalEntryLine
                {
                    AccountId = 2, CreditAmount = 100m, CurrencyCode = "BGN",
                    ExchangeRate = 1m, BaseCreditAmount = 100m, LineNumber = 2
                },
                new JournalEntryLine
                {
                    AccountId = 1, DebitAmount = 100m, CurrencyCode = "BGN",
                    ExchangeRate = 1m, BaseDebitAmount = 100m, LineNumber = 1
                }
            ]
        };

        // Act
        JournalEntryDto dto = _mapper.Map<JournalEntryDto>(entry);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(dto.RowVersion, Is.EqualTo(Convert.ToBase64String(rowVersion)));
            Assert.That(dto.Lines[0].LineNumber, Is.EqualTo(1));
            Assert.That(dto.Lines[1].LineNumber, Is.EqualTo(2));
            Assert.That(dto.Status, Is.EqualTo(JournalEntryStatus.Posted));
        });
    }
}
