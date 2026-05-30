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
/// Unit tests for <see cref="ExchangeRatesController"/> result-to-HTTP mapping (SDD-NOM-001 §2.2,
/// SDD-INFRA-001). The service is mocked and the real <see cref="DefaultErrorCodeToStatusMap"/> drives
/// status mapping; no HTTP host is started.
/// </summary>
[TestFixture]
[Category("SDD-NOM-001")]
public sealed class ExchangeRatesControllerTests
{
    private static readonly DateTimeOffset May1 = new(2026, 5, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset June1 = new(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);

    private Mock<IExchangeRateService> _serviceMock = null!;
    private ExchangeRatesController _sut = null!;

    /// <summary>Creates a fresh controller backed by a mocked service before each test.</summary>
    [SetUp]
    public void SetUp()
    {
        _serviceMock = new Mock<IExchangeRateService>();
        _sut = new ExchangeRatesController(_serviceMock.Object, new DefaultErrorCodeToStatusMap());
    }

    /// <summary>A successful latest-rate lookup returns 200 with the rate.</summary>
    [Test]
    public async Task GetLatest_Returns200_WhenRateExists()
    {
        // Arrange
        _serviceMock
            .Setup(s => s.GetLatestRateAsync("USD", May1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ExchangeRateDto>.Success(BuildRate()));

        // Act
        ActionResult<ExchangeRateDto> result = await _sut.GetLatest("USD", May1, CancellationToken.None);

        // Assert
        Assert.That(result.Result, Is.TypeOf<OkObjectResult>());
        Assert.That(((OkObjectResult)result.Result!).StatusCode, Is.EqualTo(200));
    }

    /// <summary>A missing rate maps EXCHANGE_RATE_NOT_FOUND to a 404 ProblemDetails.</summary>
    [Test]
    public async Task GetLatest_Returns404_WhenRateNotFound()
    {
        // Arrange
        _serviceMock
            .Setup(s => s.GetLatestRateAsync("USD", May1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ExchangeRateDto>.Failure("EXCHANGE_RATE_NOT_FOUND"));

        // Act
        ActionResult<ExchangeRateDto> result = await _sut.GetLatest("USD", May1, CancellationToken.None);

        // Assert
        Assert.That(result.Result, Is.TypeOf<ObjectResult>());
        Assert.That(((ObjectResult)result.Result!).StatusCode, Is.EqualTo(404));
    }

    /// <summary>An unknown currency maps CURRENCY_NOT_FOUND to a 404 ProblemDetails.</summary>
    [Test]
    public async Task GetLatest_Returns404_WhenCurrencyNotFound()
    {
        // Arrange
        _serviceMock
            .Setup(s => s.GetLatestRateAsync("ZZZ", May1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ExchangeRateDto>.Failure("CURRENCY_NOT_FOUND"));

        // Act
        ActionResult<ExchangeRateDto> result = await _sut.GetLatest("ZZZ", May1, CancellationToken.None);

        // Assert
        Assert.That(result.Result, Is.TypeOf<ObjectResult>());
        Assert.That(((ObjectResult)result.Result!).StatusCode, Is.EqualTo(404));
    }

    /// <summary>A successful range lookup returns 200 with the rates.</summary>
    [Test]
    public async Task GetRange_Returns200_WhenServiceSucceeds()
    {
        // Arrange
        IReadOnlyList<ExchangeRateDto> rates = [BuildRate()];
        _serviceMock
            .Setup(s => s.GetRateRangeAsync("USD", May1, June1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<ExchangeRateDto>>.Success(rates));

        // Act
        ActionResult<IReadOnlyList<ExchangeRateDto>> result =
            await _sut.GetRange("USD", May1, June1, CancellationToken.None);

        // Assert
        Assert.That(result.Result, Is.TypeOf<OkObjectResult>());
        Assert.That(((OkObjectResult)result.Result!).StatusCode, Is.EqualTo(200));
    }

    /// <summary>An invalid date range maps INVALID_DATE_RANGE to a 400 ProblemDetails.</summary>
    [Test]
    public async Task GetRange_Returns400_WhenInvalidDateRange()
    {
        // Arrange
        _serviceMock
            .Setup(s => s.GetRateRangeAsync("USD", June1, May1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<ExchangeRateDto>>.Failure("INVALID_DATE_RANGE"));

        // Act
        ActionResult<IReadOnlyList<ExchangeRateDto>> result =
            await _sut.GetRange("USD", June1, May1, CancellationToken.None);

        // Assert
        Assert.That(result.Result, Is.TypeOf<ObjectResult>());
        Assert.That(((ObjectResult)result.Result!).StatusCode, Is.EqualTo(400));
    }

    private static ExchangeRateDto BuildRate() => new()
    {
        CurrencyIsoCode = "USD",
        Rate = 1.800000m,
        RateDate = May1
    };
}
