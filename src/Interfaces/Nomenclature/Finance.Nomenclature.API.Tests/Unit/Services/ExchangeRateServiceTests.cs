using AutoMapper;
using Finance.Common.Results;
using Finance.Nomenclature.API.Mapping;
using Finance.Nomenclature.API.Services;
using Finance.Nomenclature.API.Tests.Builders;
using Finance.Nomenclature.API.Tests.Fixtures;
using Finance.Nomenclature.DBModel.Models;
using Finance.ServiceModel.Nomenclature;
using NUnit.Framework;

namespace Finance.Nomenclature.API.Tests.Unit.Services;

/// <summary>
/// Unit tests for <see cref="ExchangeRateService"/> covering latest-on-or-before-date, ordered range,
/// invalid date range, unknown currency, and the no-rate-found path (SDD-NOM-001 §2.2, §2.6, §3, §6).
/// The service takes no cache dependency by construction, which is itself the SDD-INFRA-004 guarantee that
/// these transactional reads are never cached. Runs offline against a SQLite in-memory
/// <c>NomenclatureDbContext</c>.
/// </summary>
[TestFixture]
[Category("SDD-NOM-001")]
public sealed class ExchangeRateServiceTests
{
    private static readonly DateTimeOffset April1 = new(2026, 4, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset May1 = new(2026, 5, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset June1 = new(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);

    private SqliteNomenclatureDbContextScope _scope = null!;
    private ExchangeRateService _sut = null!;

    /// <summary>Creates a fresh SQLite-backed service before each test.</summary>
    [SetUp]
    public void SetUp()
    {
        _scope = SqliteNomenclatureDbContextFactory.Create();
        IMapper mapper = new MapperConfiguration(cfg => cfg.AddProfile<NomenclatureMappingProfile>())
            .CreateMapper();
        _sut = new ExchangeRateService(_scope.Context, mapper);
    }

    /// <summary>Disposes the SQLite scope after each test.</summary>
    [TearDown]
    public void TearDown()
    {
        _scope.Dispose();
    }

    /// <summary>GetLatestRate returns the most recent rate on or before the requested date (§2.2).</summary>
    [Test]
    public async Task GetLatestRateAsync_ReturnsLatestRateOnOrBeforeDate()
    {
        // Arrange
        await SeedCurrencyAsync("USD");
        await SeedRateAsync("USD", April1, 1.700000m);
        await SeedRateAsync("USD", May1, 1.800000m);
        await SeedRateAsync("USD", June1, 1.900000m);

        // Act
        Result<ExchangeRateDto> result =
            await _sut.GetLatestRateAsync("USD", May1, CancellationToken.None);

        // Assert
        Assert.That(result.IsSuccess, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(result.Value!.Rate, Is.EqualTo(1.800000m));
            Assert.That(result.Value.RateDate, Is.EqualTo(May1));
        });
    }

    /// <summary>GetLatestRate for an unknown currency returns CURRENCY_NOT_FOUND (§2.2, §3).</summary>
    [Test]
    public async Task GetLatestRateAsync_UnknownCurrency_ReturnsCurrencyNotFound()
    {
        // Arrange & Act
        Result<ExchangeRateDto> result =
            await _sut.GetLatestRateAsync("ZZZ", May1, CancellationToken.None);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo("CURRENCY_NOT_FOUND"));
        });
    }

    /// <summary>GetLatestRate with no rate on or before the date returns EXCHANGE_RATE_NOT_FOUND (§2.6).</summary>
    [Test]
    public async Task GetLatestRateAsync_NoRateOnOrBeforeDate_ReturnsExchangeRateNotFound()
    {
        // Arrange
        await SeedCurrencyAsync("USD");
        await SeedRateAsync("USD", June1, 1.900000m);

        // Act
        Result<ExchangeRateDto> result =
            await _sut.GetLatestRateAsync("USD", April1, CancellationToken.None);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo("EXCHANGE_RATE_NOT_FOUND"));
        });
    }

    /// <summary>GetRateRange returns rates ordered ascending by RateDate (§2.2).</summary>
    [Test]
    public async Task GetRateRangeAsync_ReturnsRatesOrderedByRateDate()
    {
        // Arrange
        await SeedCurrencyAsync("USD");
        await SeedRateAsync("USD", June1, 1.900000m);
        await SeedRateAsync("USD", April1, 1.700000m);
        await SeedRateAsync("USD", May1, 1.800000m);

        // Act
        Result<IReadOnlyList<ExchangeRateDto>> result =
            await _sut.GetRateRangeAsync("USD", April1, June1, CancellationToken.None);

        // Assert
        Assert.That(result.IsSuccess, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(result.Value, Has.Count.EqualTo(3));
            Assert.That(result.Value![0].RateDate, Is.EqualTo(April1));
            Assert.That(result.Value[1].RateDate, Is.EqualTo(May1));
            Assert.That(result.Value[2].RateDate, Is.EqualTo(June1));
        });
    }

    /// <summary>GetRateRange restricts results to the inclusive from/to window (§2.2).</summary>
    [Test]
    public async Task GetRateRangeAsync_RestrictsToInclusiveWindow()
    {
        // Arrange
        await SeedCurrencyAsync("USD");
        await SeedRateAsync("USD", April1, 1.700000m);
        await SeedRateAsync("USD", May1, 1.800000m);
        await SeedRateAsync("USD", June1, 1.900000m);

        // Act
        Result<IReadOnlyList<ExchangeRateDto>> result =
            await _sut.GetRateRangeAsync("USD", May1, June1, CancellationToken.None);

        // Assert
        Assert.That(result.IsSuccess, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(result.Value, Has.Count.EqualTo(2));
            Assert.That(result.Value![0].RateDate, Is.EqualTo(May1));
            Assert.That(result.Value[1].RateDate, Is.EqualTo(June1));
        });
    }

    /// <summary>GetRateRange with from later than to returns INVALID_DATE_RANGE (§2.2, §3).</summary>
    [Test]
    public async Task GetRateRangeAsync_FromAfterTo_ReturnsInvalidDateRange()
    {
        // Arrange
        await SeedCurrencyAsync("USD");

        // Act
        Result<IReadOnlyList<ExchangeRateDto>> result =
            await _sut.GetRateRangeAsync("USD", June1, April1, CancellationToken.None);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo("INVALID_DATE_RANGE"));
        });
    }

    /// <summary>GetRateRange for an unknown currency returns CURRENCY_NOT_FOUND (§2.2, §3).</summary>
    [Test]
    public async Task GetRateRangeAsync_UnknownCurrency_ReturnsCurrencyNotFound()
    {
        // Arrange & Act
        Result<IReadOnlyList<ExchangeRateDto>> result =
            await _sut.GetRateRangeAsync("ZZZ", April1, June1, CancellationToken.None);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo("CURRENCY_NOT_FOUND"));
        });
    }

    /// <summary>
    /// The exchange-rate service exposes no cache dependency, satisfying the SDD-INFRA-004 rule that these
    /// transactional reads MUST hit the database every time and are never cached (§2.2).
    /// </summary>
    [Test]
    public void GetLatestRateAsync_DoesNotCallCache()
    {
        // Arrange
        Type serviceType = typeof(ExchangeRateService);

        // Act
        bool hasCacheDependency = Array.Exists(
            serviceType.GetConstructors()[0].GetParameters(),
            parameter => parameter.ParameterType.Name.Contains("ICacheService", StringComparison.Ordinal));

        // Assert
        Assert.That(hasCacheDependency, Is.False);
    }

    private async Task SeedCurrencyAsync(string isoCode)
    {
        Currency currency = CurrencyBuilder.Create().WithIsoCode(isoCode).Build();
        _scope.Context.Currencies.Add(currency);
        await _scope.Context.SaveChangesAsync(CancellationToken.None);
    }

    private async Task SeedRateAsync(string isoCode, DateTimeOffset rateDate, decimal rate)
    {
        ExchangeRate exchangeRate = ExchangeRateBuilder.Create()
            .WithCurrencyIsoCode(isoCode)
            .WithRateDate(rateDate)
            .WithRate(rate)
            .Build();
        _scope.Context.ExchangeRates.Add(exchangeRate);
        await _scope.Context.SaveChangesAsync(CancellationToken.None);
    }
}
