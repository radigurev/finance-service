using Finance.Common.Results;
using Finance.Infrastructure.Web.ErrorMapping;
using Finance.Nomenclature.API.Controllers;
using Finance.Nomenclature.API.Interfaces;
using Finance.ServiceModel.Nomenclature;
using Microsoft.AspNetCore.Mvc;
using Moq;
using NUnit.Framework;

namespace Finance.Nomenclature.API.Tests.Unit.Controllers;

/// <summary>
/// Unit tests for <see cref="NomenclatureProxyController"/> result-to-HTTP mapping (SDD-NOM-001 §2.3,
/// SDD-INFRA-001). The proxy service is mocked and the real <see cref="DefaultErrorCodeToStatusMap"/>
/// maps the upstream-unreachable failure to 503; no HTTP host is started.
/// </summary>
[TestFixture]
[Category("SDD-NOM-001")]
public sealed class NomenclatureProxyControllerTests
{
    private Mock<IWarehouseProxyService> _proxyMock = null!;
    private NomenclatureProxyController _sut = null!;

    /// <summary>Creates a fresh controller backed by a mocked proxy service before each test.</summary>
    [SetUp]
    public void SetUp()
    {
        _proxyMock = new Mock<IWarehouseProxyService>();
        _sut = new NomenclatureProxyController(_proxyMock.Object, new DefaultErrorCodeToStatusMap());
    }

    /// <summary>A successful country proxy returns 200 with the countries.</summary>
    [Test]
    public async Task GetCountries_Returns200_WhenServiceSucceeds()
    {
        // Arrange
        IReadOnlyList<CountryDto> countries = [new() { Id = 1, IsoCode = "BG", Name = "Bulgaria" }];
        _proxyMock
            .Setup(p => p.GetCountriesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<CountryDto>>.Success(countries));

        // Act
        ActionResult<IReadOnlyList<CountryDto>> result = await _sut.GetCountries(CancellationToken.None);

        // Assert
        Assert.That(result.Result, Is.TypeOf<OkObjectResult>());
        Assert.That(((OkObjectResult)result.Result!).StatusCode, Is.EqualTo(200));
    }

    /// <summary>An unreachable upstream maps WAREHOUSE_NOMENCLATURE_UNREACHABLE to a 503 ProblemDetails.</summary>
    [Test]
    public async Task GetCountries_Returns503_WhenUpstreamUnreachable()
    {
        // Arrange
        _proxyMock
            .Setup(p => p.GetCountriesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<CountryDto>>.Failure("WAREHOUSE_NOMENCLATURE_UNREACHABLE"));

        // Act
        ActionResult<IReadOnlyList<CountryDto>> result = await _sut.GetCountries(CancellationToken.None);

        // Assert
        Assert.That(result.Result, Is.TypeOf<ObjectResult>());
        Assert.That(((ObjectResult)result.Result!).StatusCode, Is.EqualTo(503));
    }

    /// <summary>A successful state proxy returns 200 with the states.</summary>
    [Test]
    public async Task GetStates_Returns200_WhenServiceSucceeds()
    {
        // Arrange
        IReadOnlyList<StateDto> states = [new() { Id = 10, Name = "Sofia-City", CountryIsoCode = "BG" }];
        _proxyMock
            .Setup(p => p.GetStatesAsync("BG", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<StateDto>>.Success(states));

        // Act
        ActionResult<IReadOnlyList<StateDto>> result = await _sut.GetStates("BG", CancellationToken.None);

        // Assert
        Assert.That(result.Result, Is.TypeOf<OkObjectResult>());
        Assert.That(((OkObjectResult)result.Result!).StatusCode, Is.EqualTo(200));
    }

    /// <summary>An unreachable upstream on the cities proxy maps to a 503 ProblemDetails.</summary>
    [Test]
    public async Task GetCities_Returns503_WhenUpstreamUnreachable()
    {
        // Arrange
        _proxyMock
            .Setup(p => p.GetCitiesAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<CityDto>>.Failure("WAREHOUSE_NOMENCLATURE_UNREACHABLE"));

        // Act
        ActionResult<IReadOnlyList<CityDto>> result = await _sut.GetCities(10, CancellationToken.None);

        // Assert
        Assert.That(result.Result, Is.TypeOf<ObjectResult>());
        Assert.That(((ObjectResult)result.Result!).StatusCode, Is.EqualTo(503));
    }
}
