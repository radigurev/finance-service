using AutoMapper;
using Finance.Common.Enums;
using Finance.Periods.API.Mapping;
using Finance.Periods.API.Tests.Builders;
using Finance.Periods.DBModel.Models;
using Finance.ServiceModel.Periods;
using NUnit.Framework;

namespace Finance.Periods.API.Tests.Unit.Mapping;

/// <summary>
/// Unit tests for <see cref="PeriodMappingProfile"/> (SDD-FIN-004 §6.3). Verifies the AutoMapper
/// configuration is valid and that the FiscalPeriod → DTO mapping encodes the rowversion as base64.
/// </summary>
[TestFixture]
[Category("SDD-FIN-004")]
public sealed class PeriodMappingProfileTests
{
    private IMapper _mapper = null!;

    /// <summary>Builds a fresh mapper from the Periods profile before each test.</summary>
    [SetUp]
    public void SetUp()
    {
        MapperConfiguration configuration = new(cfg => cfg.AddProfile<PeriodMappingProfile>());
        _mapper = configuration.CreateMapper();
    }

    /// <summary>The Periods AutoMapper profile is internally valid (§6.3).</summary>
    [Test]
    public void Profile_Configuration_IsValid()
    {
        // Arrange
        MapperConfiguration configuration = new(cfg => cfg.AddProfile<PeriodMappingProfile>());

        // Act & Assert
        Assert.That(() => configuration.AssertConfigurationIsValid(), Throws.Nothing);
    }

    /// <summary>FiscalPeriod maps to FiscalPeriodDto with the rowversion encoded as base64 (§2.12, §6.3).</summary>
    [Test]
    public void Map_FiscalPeriod_EncodesRowVersionAsBase64()
    {
        // Arrange
        FiscalPeriod period = FiscalPeriodBuilder.Create()
            .WithFiscalYear(2026)
            .WithPeriodNumber(4)
            .WithStatus(FiscalPeriodStatus.Closed)
            .Build();
        period.RowVersion = [1, 2, 3, 4, 5, 6, 7, 8];

        // Act
        FiscalPeriodDto dto = _mapper.Map<FiscalPeriodDto>(period);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(dto.FiscalYear, Is.EqualTo(2026));
            Assert.That(dto.PeriodNumber, Is.EqualTo(4));
            Assert.That(dto.Status, Is.EqualTo(FiscalPeriodStatus.Closed));
            Assert.That(dto.RowVersion, Is.EqualTo(Convert.ToBase64String(period.RowVersion)));
        });
    }
}
