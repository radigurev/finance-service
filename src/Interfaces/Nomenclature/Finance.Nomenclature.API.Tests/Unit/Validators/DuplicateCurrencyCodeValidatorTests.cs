using Finance.Common.Validation;
using Finance.Nomenclature.API.Tests.Builders;
using Finance.Nomenclature.API.Tests.Fixtures;
using Finance.Nomenclature.API.Validators;
using Finance.Nomenclature.DBModel.Models;
using Finance.ServiceModel.Nomenclature;
using NUnit.Framework;

namespace Finance.Nomenclature.API.Tests.Unit.Validators;

/// <summary>
/// Unit tests for <see cref="DuplicateCurrencyCodeValidator"/> (SDD-NOM-001 §2.1, §3). Runs against a
/// SQLite in-memory <c>NomenclatureDbContext</c> so the uniqueness rule is exercised over real data.
/// </summary>
[TestFixture]
[Category("SDD-NOM-001")]
public sealed class DuplicateCurrencyCodeValidatorTests
{
    private SqliteNomenclatureDbContextScope _scope = null!;
    private DuplicateCurrencyCodeValidator _sut = null!;

    /// <summary>Creates a fresh SQLite-backed validator before each test.</summary>
    [SetUp]
    public void SetUp()
    {
        _scope = SqliteNomenclatureDbContextFactory.Create();
        _sut = new DuplicateCurrencyCodeValidator(_scope.Context);
    }

    /// <summary>Disposes the SQLite scope after each test.</summary>
    [TearDown]
    public void TearDown()
    {
        _scope.Dispose();
    }

    /// <summary>An ISO code already present yields DUPLICATE_CURRENCY_CODE.</summary>
    [Test]
    public async Task DuplicateCurrencyCodeValidator_ExistingIso_ReturnsDuplicateCurrencyCode()
    {
        // Arrange
        await SeedAsync(CurrencyBuilder.Create().WithIsoCode("USD"));
        CreateCurrencyRequest request = CreateCurrencyRequestBuilder.Create().WithIsoCode("USD").Build();

        // Act
        ChainValidationResult result = await _sut.ValidateAsync(request, CancellationToken.None);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsValid, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo("DUPLICATE_CURRENCY_CODE"));
        });
    }

    /// <summary>An unused ISO code passes the uniqueness check.</summary>
    [Test]
    public async Task DuplicateCurrencyCodeValidator_UnusedIso_Passes()
    {
        // Arrange
        await SeedAsync(CurrencyBuilder.Create().WithIsoCode("USD"));
        CreateCurrencyRequest request = CreateCurrencyRequestBuilder.Create().WithIsoCode("EUR").Build();

        // Act
        ChainValidationResult result = await _sut.ValidateAsync(request, CancellationToken.None);

        // Assert
        Assert.That(result.IsValid, Is.True);
    }

    private async Task SeedAsync(CurrencyBuilder builder)
    {
        Currency currency = builder.Build();
        _scope.Context.Currencies.Add(currency);
        await _scope.Context.SaveChangesAsync(CancellationToken.None);
    }
}
