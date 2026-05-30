using Finance.Common.ErrorCodes;
using Finance.Infrastructure.Sequences;
using Finance.Infrastructure.Sequences.Interfaces;
using Finance.Infrastructure.Stateful.Tests.Sequences.Fixtures;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;

namespace Finance.Infrastructure.Stateful.Tests.Sequences;

/// <summary>
/// Unit tests for <see cref="SequenceGenerator{TContext}"/> over a SQLite in-memory context
/// (SDD-INFRA-003 §2.2, §2.5, §6). They verify formatted output for registered keys, per-call
/// increment, the new-fiscal-year start-at-one rule, delegation to the registered formatter, and
/// UNKNOWN_SEQUENCE_KEY for unregistered/empty keys. Real SQL Server lock concurrency is
/// <c>[Category("Integration")]</c> and excluded from the default run.
/// </summary>
[TestFixture]
[Category("SDD-INFRA-003")]
public sealed class SequenceGeneratorTests
{
    private static readonly DateTimeOffset Year2026 = new(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);

    private SqliteConnection _connection = null!;
    private TestSequenceDbContext _context = null!;

    /// <summary>Opens a shared in-memory SQLite connection and creates the schema before each test.</summary>
    [SetUp]
    public void SetUp()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        DbContextOptions<TestSequenceDbContext> options =
            new DbContextOptionsBuilder<TestSequenceDbContext>()
                .UseSqlite(_connection)
                .Options;

        _context = new TestSequenceDbContext(options);
        _context.Database.EnsureCreated();
    }

    /// <summary>Disposes the context and connection after each test.</summary>
    [TearDown]
    public void TearDown()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    /// <summary>The first call for a registered key returns the formatted value at counter one.</summary>
    [Test]
    public async Task NextAsync_ReturnsFormattedValueForRegisteredKey()
    {
        // Arrange
        ISequenceGenerator generator = BuildGenerator(Year2026);

        // Act
        string number = await generator.NextAsync(SequenceKeys.JournalEntry, CancellationToken.None);

        // Assert
        Assert.That(number, Is.EqualTo("JE-2026-000001"));
    }

    /// <summary>Successive calls for the same key produce sequential values.</summary>
    [Test]
    public async Task NextAsync_IncrementsCounterPerCall()
    {
        // Arrange
        ISequenceGenerator generator = BuildGenerator(Year2026);

        // Act
        string first = await generator.NextAsync(SequenceKeys.JournalEntry, CancellationToken.None);
        string second = await generator.NextAsync(SequenceKeys.JournalEntry, CancellationToken.None);
        string third = await generator.NextAsync(SequenceKeys.JournalEntry, CancellationToken.None);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(first, Is.EqualTo("JE-2026-000001"));
            Assert.That(second, Is.EqualTo("JE-2026-000002"));
            Assert.That(third, Is.EqualTo("JE-2026-000003"));
        });
    }

    /// <summary>The first call of a new fiscal year starts the per-year counter at one (SDD-INFRA-003 §2.5).</summary>
    [Test]
    public async Task NextAsync_StartsAtOne_ForNewFiscalYear()
    {
        // Arrange
        ISequenceGenerator generator2026 = BuildGenerator(new DateTimeOffset(2026, 12, 31, 0, 0, 0, TimeSpan.Zero));
        await generator2026.NextAsync(SequenceKeys.JournalEntry, CancellationToken.None);
        await generator2026.NextAsync(SequenceKeys.JournalEntry, CancellationToken.None);

        ISequenceGenerator generator2027 = BuildGenerator(new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero));

        // Act
        string firstOf2027 = await generator2027.NextAsync(SequenceKeys.JournalEntry, CancellationToken.None);

        // Assert
        Assert.That(firstOf2027, Is.EqualTo("JE-2027-000001"));
    }

    /// <summary>Distinct keys maintain independent counters (one key's increments do not affect another).</summary>
    [Test]
    public async Task NextAsync_DistinctKeys_HaveIndependentCounters()
    {
        // Arrange
        ISequenceGenerator generator = BuildGenerator(Year2026);
        await generator.NextAsync(SequenceKeys.JournalEntry, CancellationToken.None);

        // Act
        string firstPayment = await generator.NextAsync(SequenceKeys.Payment, CancellationToken.None);

        // Assert
        Assert.That(firstPayment, Is.EqualTo("PAY-2026-000001"));
    }

    /// <summary>The generator hands the counter to the registered formatter for output shaping.</summary>
    [Test]
    public async Task NextAsync_UsesRegisteredFormatter_ForOutput()
    {
        // Arrange
        IDocumentNumberFormatter recordingFormatter = new RecordingFormatter();
        ISequenceGenerator generator = new SequenceGenerator<TestSequenceDbContext>(
            _context,
            SequenceDefinitions.BuiltIn,
            recordingFormatter,
            new FixedTimeProvider(Year2026));

        // Act
        string output = await generator.NextAsync(SequenceKeys.JournalEntry, CancellationToken.None);

        // Assert
        Assert.That(output, Is.EqualTo("FMT|JE|2026|1"));
    }

    /// <summary>An unregistered key throws ArgumentException carrying UNKNOWN_SEQUENCE_KEY.</summary>
    [Test]
    public void NextAsync_Throws_WhenSequenceKeyNotRegistered()
    {
        // Arrange
        ISequenceGenerator generator = BuildGenerator(Year2026);

        // Act & Assert
        Assert.That(
            async () => await generator.NextAsync("NOPE", CancellationToken.None),
            Throws.TypeOf<ArgumentException>()
                .With.Message.Contains(SequenceErrorCodes.UNKNOWN_SEQUENCE_KEY));
    }

    /// <summary>An empty or whitespace key throws ArgumentException carrying UNKNOWN_SEQUENCE_KEY.</summary>
    [TestCase("")]
    [TestCase("   ")]
    public void NextAsync_Throws_WhenSequenceKeyEmpty(string key)
    {
        // Arrange
        ISequenceGenerator generator = BuildGenerator(Year2026);

        // Act & Assert
        Assert.That(
            async () => await generator.NextAsync(key, CancellationToken.None),
            Throws.TypeOf<ArgumentException>()
                .With.Message.Contains(SequenceErrorCodes.UNKNOWN_SEQUENCE_KEY));
    }

    private ISequenceGenerator BuildGenerator(DateTimeOffset now)
    {
        return new SequenceGenerator<TestSequenceDbContext>(
            _context,
            SequenceDefinitions.BuiltIn,
            new DefaultDocumentNumberFormatter(SequenceDefinitions.BuiltIn),
            new FixedTimeProvider(now));
    }

    /// <summary>A formatter that records its inputs so delegation can be asserted.</summary>
    private sealed class RecordingFormatter : IDocumentNumberFormatter
    {
        /// <inheritdoc />
        public string Format(string sequenceKey, string periodSegment, long counter)
        {
            return $"FMT|{sequenceKey}|{periodSegment}|{counter}";
        }
    }
}
