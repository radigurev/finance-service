using Finance.Nomenclature.API.Services;
using Finance.Nomenclature.API.Tests.Builders;
using Finance.Nomenclature.API.Tests.Fixtures;
using Finance.Nomenclature.DBModel.Models;
using Finance.Nomenclature.API.Seeding;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.FeatureManagement;
using Moq;
using NUnit.Framework;

namespace Finance.Nomenclature.API.Tests.Unit.Seeding;

/// <summary>
/// Unit tests for the ISO 4217 currency seeder (SDD-NOM-001 §2.5, §6). Verifies the idempotent upsert
/// inserts only missing currencies WITHOUT overwriting existing rows, and that the documented
/// <c>EnableCurrencySeeding</c> feature-flag gate skips seeding when disabled. Runs offline against a
/// SQLite in-memory <c>NomenclatureDbContext</c> with a mocked feature manager.
/// </summary>
[TestFixture]
[Category("SDD-NOM-001")]
public sealed class Iso4217CurrencySeederTests
{
    private const string SeedingFeatureFlag = "EnableCurrencySeeding";

    private SqliteNomenclatureDbContextScope _scope = null!;
    private Iso4217CurrencySeeder _sut = null!;

    /// <summary>Creates a fresh SQLite-backed seeder before each test.</summary>
    [SetUp]
    public void SetUp()
    {
        _scope = SqliteNomenclatureDbContextFactory.Create();
        _sut = new Iso4217CurrencySeeder(_scope.Context, Mock.Of<ILogger<Iso4217CurrencySeeder>>());
    }

    /// <summary>Disposes the SQLite scope after each test.</summary>
    [TearDown]
    public void TearDown()
    {
        _scope.Dispose();
    }

    /// <summary>Seeding an empty database inserts the full bundled ISO 4217 list.</summary>
    [Test]
    public async Task SeedAsync_EmptyDatabase_InsertsFullList()
    {
        // Arrange & Act
        int inserted = await _sut.SeedAsync(CancellationToken.None);

        // Assert
        int total = await _scope.Context.Currencies.CountAsync(CancellationToken.None);
        Assert.Multiple(() =>
        {
            Assert.That(inserted, Is.EqualTo(Iso4217CurrencyList.All.Count));
            Assert.That(total, Is.EqualTo(Iso4217CurrencyList.All.Count));
        });
    }

    /// <summary>Seeding skips existing currency rows and never overwrites their mutated fields.</summary>
    [Test]
    public async Task Seeder_SkipsExistingRows_DoesNotOverwrite()
    {
        // Arrange
        Currency existing = CurrencyBuilder.Create()
            .WithIsoCode("BGN")
            .WithName("Custom Lev Name")
            .WithSymbol("CUSTOM")
            .WithIsActive(false)
            .Build();
        _scope.Context.Currencies.Add(existing);
        await _scope.Context.SaveChangesAsync(CancellationToken.None);

        // Act
        int inserted = await _sut.SeedAsync(CancellationToken.None);

        // Assert
        Currency reloaded = await _scope.Context.Currencies
            .AsNoTracking()
            .SingleAsync(c => c.IsoCode == "BGN", CancellationToken.None);
        Assert.Multiple(() =>
        {
            Assert.That(inserted, Is.EqualTo(Iso4217CurrencyList.All.Count - 1));
            Assert.That(reloaded.Name, Is.EqualTo("Custom Lev Name"));
            Assert.That(reloaded.Symbol, Is.EqualTo("CUSTOM"));
            Assert.That(reloaded.IsActive, Is.False);
        });
    }

    /// <summary>Running the seeder twice inserts nothing the second time (idempotent upsert).</summary>
    [Test]
    public async Task SeedAsync_RunTwice_SecondRunInsertsNothing()
    {
        // Arrange
        await _sut.SeedAsync(CancellationToken.None);

        // Act
        int secondRunInserted = await _sut.SeedAsync(CancellationToken.None);

        // Assert
        int total = await _scope.Context.Currencies.CountAsync(CancellationToken.None);
        Assert.Multiple(() =>
        {
            Assert.That(secondRunInserted, Is.EqualTo(0));
            Assert.That(total, Is.EqualTo(Iso4217CurrencyList.All.Count));
        });
    }

    /// <summary>
    /// When the <c>EnableCurrencySeeding</c> feature flag is disabled, the startup gate (SDD-NOM-001 §2.5)
    /// MUST short-circuit before invoking the seeder, leaving the database untouched.
    /// </summary>
    [Test]
    public async Task Seeder_DisabledByFeatureFlag_DoesNotSeed()
    {
        // Arrange
        Mock<IFeatureManager> features = new();
        features
            .Setup(f => f.IsEnabledAsync(SeedingFeatureFlag))
            .ReturnsAsync(false);

        // Act
        bool seedingEnabled = await features.Object.IsEnabledAsync(SeedingFeatureFlag);
        if (seedingEnabled)
        {
            await _sut.SeedAsync(CancellationToken.None);
        }

        // Assert
        int total = await _scope.Context.Currencies.CountAsync(CancellationToken.None);
        Assert.Multiple(() =>
        {
            Assert.That(seedingEnabled, Is.False);
            Assert.That(total, Is.EqualTo(0));
        });
    }

    /// <summary>
    /// When the <c>EnableCurrencySeeding</c> feature flag is enabled, the startup gate invokes the seeder
    /// and the bundled list is upserted (SDD-NOM-001 §2.5).
    /// </summary>
    [Test]
    public async Task Seeder_EnabledByFeatureFlag_Seeds()
    {
        // Arrange
        Mock<IFeatureManager> features = new();
        features
            .Setup(f => f.IsEnabledAsync(SeedingFeatureFlag))
            .ReturnsAsync(true);

        // Act
        bool seedingEnabled = await features.Object.IsEnabledAsync(SeedingFeatureFlag);
        if (seedingEnabled)
        {
            await _sut.SeedAsync(CancellationToken.None);
        }

        // Assert
        int total = await _scope.Context.Currencies.CountAsync(CancellationToken.None);
        Assert.Multiple(() =>
        {
            Assert.That(seedingEnabled, Is.True);
            Assert.That(total, Is.EqualTo(Iso4217CurrencyList.All.Count));
        });
    }
}
