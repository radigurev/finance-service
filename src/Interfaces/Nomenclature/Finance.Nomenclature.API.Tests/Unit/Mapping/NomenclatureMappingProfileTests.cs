using AutoMapper;
using Finance.Nomenclature.API.Mapping;
using Finance.Nomenclature.API.Tests.Builders;
using Finance.Nomenclature.DBModel.Models;
using Finance.ServiceModel.Nomenclature;
using NUnit.Framework;

namespace Finance.Nomenclature.API.Tests.Unit.Mapping;

/// <summary>
/// Unit tests for <see cref="NomenclatureMappingProfile"/> (SDD-NOM-001 §2.0, §2.2). Verifies the
/// configuration is internally valid, that the Currency RowVersion byte array is projected to a base64
/// string for round-tripping, and that ExchangeRate scalars map straight through.
/// </summary>
[TestFixture]
[Category("SDD-NOM-001")]
public sealed class NomenclatureMappingProfileTests
{
    private IMapper _mapper = null!;

    /// <summary>Builds a mapper from the profile under test before each test.</summary>
    [SetUp]
    public void SetUp()
    {
        _mapper = new MapperConfiguration(cfg => cfg.AddProfile<NomenclatureMappingProfile>()).CreateMapper();
    }

    /// <summary>The AutoMapper configuration is internally consistent.</summary>
    [Test]
    public void Configuration_IsValid()
    {
        // Arrange & Act & Assert
        Assert.That(() => _mapper.ConfigurationProvider.AssertConfigurationIsValid(), Throws.Nothing);
    }

    /// <summary>A currency maps to a DTO with matching scalars and a base64 RowVersion.</summary>
    [Test]
    public void Map_CurrencyToDto_MapsScalarsAndBase64RowVersion()
    {
        // Arrange
        Currency currency = CurrencyBuilder.Create()
            .WithIsoCode("BGN")
            .WithName("Bulgarian Lev")
            .WithSymbol("лв")
            .Build();
        currency.Id = 42;
        currency.RowVersion = [1, 2, 3, 4, 5, 6, 7, 8];

        // Act
        CurrencyDto dto = _mapper.Map<CurrencyDto>(currency);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(dto.Id, Is.EqualTo(42));
            Assert.That(dto.IsoCode, Is.EqualTo("BGN"));
            Assert.That(dto.Name, Is.EqualTo("Bulgarian Lev"));
            Assert.That(dto.Symbol, Is.EqualTo("лв"));
            Assert.That(dto.RowVersion, Is.EqualTo(Convert.ToBase64String(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 })));
        });
    }

    /// <summary>An exchange rate maps to a DTO preserving the six-decimal rate and the rate date.</summary>
    [Test]
    public void Map_ExchangeRateToDto_MapsScalars()
    {
        // Arrange
        DateTimeOffset rateDate = new(2026, 5, 1, 0, 0, 0, TimeSpan.Zero);
        ExchangeRate rate = ExchangeRateBuilder.Create()
            .WithCurrencyIsoCode("USD")
            .WithRate(1.812345m)
            .WithRateDate(rateDate)
            .Build();

        // Act
        ExchangeRateDto dto = _mapper.Map<ExchangeRateDto>(rate);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(dto.CurrencyIsoCode, Is.EqualTo("USD"));
            Assert.That(dto.Rate, Is.EqualTo(1.812345m));
            Assert.That(dto.RateDate, Is.EqualTo(rateDate));
        });
    }
}
