using System.Net;
using Finance.Common.Results;
using Finance.Infrastructure.Caching.Interfaces;
using Finance.Nomenclature.API.Caching;
using Finance.Nomenclature.API.Interfaces;
using Finance.Nomenclature.API.Services;
using Finance.ServiceModel.Nomenclature;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using Refit;

namespace Finance.Nomenclature.API.Tests.Unit.Services;

/// <summary>
/// Unit tests for <see cref="WarehouseProxyService"/> covering the success path and the
/// upstream-unreachable degradation that maps to <c>WAREHOUSE_NOMENCLATURE_UNREACHABLE</c> (503) when the
/// mocked Refit client throws (SDD-NOM-001 §2.3, §6). The Refit client, three caches, and the logger are
/// mocked; the cache mocks are pass-throughs so the upstream call is exercised.
/// </summary>
[TestFixture]
[Category("SDD-NOM-001")]
public sealed class WarehouseProxyServiceTests
{
    private Mock<IWarehouseNomenclatureClient> _clientMock = null!;
    private Mock<ICacheService<IReadOnlyList<CountryDto>>> _countryCacheMock = null!;
    private Mock<ICacheService<IReadOnlyList<StateDto>>> _stateCacheMock = null!;
    private Mock<ICacheService<IReadOnlyList<CityDto>>> _cityCacheMock = null!;
    private WarehouseProxyService _sut = null!;

    /// <summary>Creates a fresh proxy service with pass-through caches before each test.</summary>
    [SetUp]
    public void SetUp()
    {
        _clientMock = new Mock<IWarehouseNomenclatureClient>();

        _countryCacheMock = new Mock<ICacheService<IReadOnlyList<CountryDto>>>();
        ConfigurePassThrough(_countryCacheMock);

        _stateCacheMock = new Mock<ICacheService<IReadOnlyList<StateDto>>>();
        ConfigurePassThrough(_stateCacheMock);

        _cityCacheMock = new Mock<ICacheService<IReadOnlyList<CityDto>>>();
        ConfigurePassThrough(_cityCacheMock);

        _sut = new WarehouseProxyService(
            _clientMock.Object,
            _countryCacheMock.Object,
            _stateCacheMock.Object,
            _cityCacheMock.Object,
            Mock.Of<ILogger<WarehouseProxyService>>());
    }

    /// <summary>GetCountries returns the upstream country list on success (§2.3).</summary>
    [Test]
    public async Task GetCountriesAsync_ReturnsUpstreamCountries_OnSuccess()
    {
        // Arrange
        IReadOnlyList<CountryDto> upstream =
        [
            new() { Id = 1, IsoCode = "BG", Name = "Bulgaria" }
        ];
        _clientMock
            .Setup(c => c.GetCountriesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(upstream);

        // Act
        Result<IReadOnlyList<CountryDto>> result =
            await _sut.GetCountriesAsync(CancellationToken.None);

        // Assert
        Assert.That(result.IsSuccess, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(result.Value, Has.Count.EqualTo(1));
            Assert.That(result.Value![0].IsoCode, Is.EqualTo("BG"));
        });
    }

    /// <summary>GetCountries caches the response under the documented key (§2.3, SDD-INFRA-004).</summary>
    [Test]
    public async Task GetCountriesAsync_UsesCountriesCacheKey()
    {
        // Arrange
        _clientMock
            .Setup(c => c.GetCountriesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        // Act
        await _sut.GetCountriesAsync(CancellationToken.None);

        // Assert
        _countryCacheMock.Verify(
            c => c.GetOrSetAsync(
                WarehouseProxyCacheKeys.Countries,
                It.IsAny<Func<CancellationToken, Task<IReadOnlyList<CountryDto>?>>>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>GetCountries returns WAREHOUSE_NOMENCLATURE_UNREACHABLE when the client throws ApiException (§2.3).</summary>
    [Test]
    public async Task GetCountriesAsync_ReturnsUnreachable_WhenClientThrowsApiException()
    {
        // Arrange
        ApiException apiException = await ApiException.Create(
            new HttpRequestMessage(HttpMethod.Get, "https://warehouse.local/countries"),
            HttpMethod.Get,
            new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
            new RefitSettings());
        _clientMock
            .Setup(c => c.GetCountriesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(apiException);

        // Act
        Result<IReadOnlyList<CountryDto>> result =
            await _sut.GetCountriesAsync(CancellationToken.None);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo("WAREHOUSE_NOMENCLATURE_UNREACHABLE"));
        });
    }

    /// <summary>GetCountries returns WAREHOUSE_NOMENCLATURE_UNREACHABLE when the client throws HttpRequestException (§2.3).</summary>
    [Test]
    public async Task GetCountriesAsync_ReturnsUnreachable_WhenClientThrowsHttpRequestException()
    {
        // Arrange
        _clientMock
            .Setup(c => c.GetCountriesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("connection refused"));

        // Act
        Result<IReadOnlyList<CountryDto>> result =
            await _sut.GetCountriesAsync(CancellationToken.None);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo("WAREHOUSE_NOMENCLATURE_UNREACHABLE"));
        });
    }

    /// <summary>GetStates returns the upstream states for a country on success (§2.3).</summary>
    [Test]
    public async Task GetStatesAsync_ReturnsUpstreamStates_OnSuccess()
    {
        // Arrange
        IReadOnlyList<StateDto> upstream =
        [
            new() { Id = 10, Name = "Sofia-City", CountryIsoCode = "BG" }
        ];
        _clientMock
            .Setup(c => c.GetStatesAsync("BG", It.IsAny<CancellationToken>()))
            .ReturnsAsync(upstream);

        // Act
        Result<IReadOnlyList<StateDto>> result =
            await _sut.GetStatesAsync("BG", CancellationToken.None);

        // Assert
        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value![0].Name, Is.EqualTo("Sofia-City"));
    }

    /// <summary>GetStates returns WAREHOUSE_NOMENCLATURE_UNREACHABLE when the client throws (§2.3).</summary>
    [Test]
    public async Task GetStatesAsync_ReturnsUnreachable_WhenClientThrows()
    {
        // Arrange
        _clientMock
            .Setup(c => c.GetStatesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("connection refused"));

        // Act
        Result<IReadOnlyList<StateDto>> result =
            await _sut.GetStatesAsync("BG", CancellationToken.None);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo("WAREHOUSE_NOMENCLATURE_UNREACHABLE"));
        });
    }

    /// <summary>GetCities returns the upstream cities for a state on success (§2.3).</summary>
    [Test]
    public async Task GetCitiesAsync_ReturnsUpstreamCities_OnSuccess()
    {
        // Arrange
        IReadOnlyList<CityDto> upstream =
        [
            new() { Id = 100, Name = "Sofia", StateId = 10 }
        ];
        _clientMock
            .Setup(c => c.GetCitiesAsync(10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(upstream);

        // Act
        Result<IReadOnlyList<CityDto>> result =
            await _sut.GetCitiesAsync(10, CancellationToken.None);

        // Assert
        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value![0].Name, Is.EqualTo("Sofia"));
    }

    /// <summary>GetCities returns WAREHOUSE_NOMENCLATURE_UNREACHABLE when the client throws TaskCanceledException (§2.3).</summary>
    [Test]
    public async Task GetCitiesAsync_ReturnsUnreachable_WhenClientThrowsTaskCanceled()
    {
        // Arrange
        _clientMock
            .Setup(c => c.GetCitiesAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TaskCanceledException("timed out"));

        // Act
        Result<IReadOnlyList<CityDto>> result =
            await _sut.GetCitiesAsync(10, CancellationToken.None);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo("WAREHOUSE_NOMENCLATURE_UNREACHABLE"));
        });
    }

    private static void ConfigurePassThrough<T>(Mock<ICacheService<IReadOnlyList<T>>> cacheMock)
    {
        cacheMock
            .Setup(c => c.GetOrSetAsync(
                It.IsAny<string>(),
                It.IsAny<Func<CancellationToken, Task<IReadOnlyList<T>?>>>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<CancellationToken>()))
            .Returns<string, Func<CancellationToken, Task<IReadOnlyList<T>?>>, TimeSpan?, CancellationToken>(
                (_, factory, _, ct) => factory(ct));
    }
}
