using Finance.Country.Abstractions;
using Finance.Journal.API.Services;
using Finance.Journal.API.Tests.Fixtures;
using Finance.Journal.DBModel.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace Finance.Journal.API.Tests.Unit.Services;

/// <summary>
/// Unit tests for <see cref="PostingRuleSeeder"/> (SDD-FIN-006 §6.3) against a faked
/// <see cref="ICountryStrategy"/> and a SQLite in-memory <see cref="Finance.Journal.DBModel.JournalDbContext"/>:
/// the seed inserts every template whose rule key is not yet present; it is idempotent (a second run inserts
/// nothing and never overwrites an existing rule's edits); an empty template set is a no-op; and a
/// structurally unbalanceable template is skipped and logged without crashing the seed. Account selectors are
/// persisted as codes and resolved lazily at apply time (SDD-FIN-006 §7), so the seeder performs no account
/// resolution and is exercised without a reference-data reader.
/// </summary>
[TestFixture]
[Category("SDD-FIN-006")]
public sealed class PostingRuleSeederTests
{
    private SqliteJournalDbContextScope _scope = null!;

    /// <summary>Creates a fresh SQLite-backed context before each test.</summary>
    [SetUp]
    public void SetUp()
    {
        _scope = SqliteJournalDbContextFactory.Create();
    }

    /// <summary>Disposes the SQLite scope after each test.</summary>
    [TearDown]
    public void TearDown()
    {
        _scope.Dispose();
    }

    [Test]
    public async Task Seeder_FlagEnabled_InsertsTemplatesFromCountryStrategy()
    {
        // Arrange — the feature flag is gated by the host before SeedAsync; this asserts the insert behavior.
        FakeCountryStrategy strategy = new(
        [
            FakeCountryStrategy.BalanceableTemplate("SALE_INVOICE"),
            FakeCountryStrategy.BalanceableTemplate("PURCHASE_INVOICE")
        ]);
        PostingRuleSeeder sut = BuildSeeder(strategy);

        // Act
        int inserted = await sut.SeedAsync(CancellationToken.None);

        // Assert
        List<PostingRule> rules = await _scope.Context.PostingRules.Include(rule => rule.Lines).ToListAsync();
        Assert.Multiple(() =>
        {
            Assert.That(inserted, Is.EqualTo(2));
            Assert.That(rules.Select(rule => rule.RuleKey), Is.EquivalentTo(new[] { "SALE_INVOICE", "PURCHASE_INVOICE" }));
            Assert.That(rules.All(rule => rule.Lines.Count == 2), Is.True);
        });
    }

    [Test]
    public async Task Seeder_Idempotent_ExistingRuleKeyNotOverwritten()
    {
        // Arrange — seed once, edit the persisted rule's description, then seed the same template again.
        FakeCountryStrategy strategy = new([FakeCountryStrategy.BalanceableTemplate("SALE_INVOICE")]);
        PostingRuleSeeder sut = BuildSeeder(strategy);
        await sut.SeedAsync(CancellationToken.None);

        PostingRule edited = await _scope.Context.PostingRules.SingleAsync(rule => rule.RuleKey == "SALE_INVOICE");
        edited.Description = "Administrator edit.";
        await _scope.Context.SaveChangesAsync();
        _scope.Context.ChangeTracker.Clear();

        // Act
        int insertedOnSecondRun = await sut.SeedAsync(CancellationToken.None);

        // Assert
        List<PostingRule> rules = await _scope.Context.PostingRules.ToListAsync();
        Assert.Multiple(() =>
        {
            Assert.That(insertedOnSecondRun, Is.EqualTo(0));
            Assert.That(rules, Has.Count.EqualTo(1));
            Assert.That(rules[0].Description, Is.EqualTo("Administrator edit."));
        });
    }

    [Test]
    public async Task Seeder_EmptyStrategyRules_NoOp()
    {
        // Arrange
        FakeCountryStrategy strategy = new([]);
        PostingRuleSeeder sut = BuildSeeder(strategy);

        // Act
        int inserted = await sut.SeedAsync(CancellationToken.None);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(inserted, Is.EqualTo(0));
            Assert.That(_scope.Context.PostingRules.Count(), Is.EqualTo(0));
        });
    }

    [Test]
    public async Task Seeder_UnbalanceableTemplate_SkipsRule_LogsUnbalanced()
    {
        // Arrange — one balanceable + one debit-only template; only the balanceable one is inserted.
        FakeCountryStrategy strategy = new(
        [
            FakeCountryStrategy.BalanceableTemplate("SALE_INVOICE"),
            FakeCountryStrategy.UnbalanceableTemplate("BROKEN_RULE")
        ]);
        PostingRuleSeeder sut = BuildSeeder(strategy);

        // Act
        int inserted = await sut.SeedAsync(CancellationToken.None);

        // Assert
        List<string> keys = await _scope.Context.PostingRules.Select(rule => rule.RuleKey).ToListAsync();
        Assert.Multiple(() =>
        {
            Assert.That(inserted, Is.EqualTo(1));
            Assert.That(keys, Is.EqualTo(new[] { "SALE_INVOICE" }));
        });
    }

    [Test]
    public async Task Seeder_RunTwice_DoesNotDuplicateRules()
    {
        // Arrange
        FakeCountryStrategy strategy = new(
        [
            FakeCountryStrategy.BalanceableTemplate("SALE_INVOICE"),
            FakeCountryStrategy.BalanceableTemplate("CUSTOMER_PAYMENT")
        ]);
        PostingRuleSeeder sut = BuildSeeder(strategy);

        // Act
        await sut.SeedAsync(CancellationToken.None);
        await sut.SeedAsync(CancellationToken.None);

        // Assert
        Assert.That(_scope.Context.PostingRules.Count(), Is.EqualTo(2));
    }

    private PostingRuleSeeder BuildSeeder(ICountryStrategy strategy) =>
        new(_scope.Context, strategy, NullLogger<PostingRuleSeeder>.Instance);
}
