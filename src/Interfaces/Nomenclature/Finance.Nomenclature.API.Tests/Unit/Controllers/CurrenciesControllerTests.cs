using Finance.Common.Results;
using Finance.GenericFiltering.Models;
using Finance.Infrastructure.Web.ErrorMapping;
using Finance.Nomenclature.API.Controllers;
using Finance.Nomenclature.API.Interfaces;
using Finance.Nomenclature.API.Tests.Builders;
using Finance.ServiceModel.Nomenclature;
using Microsoft.AspNetCore.Mvc;
using Moq;
using NUnit.Framework;

namespace Finance.Nomenclature.API.Tests.Unit.Controllers;

/// <summary>
/// Unit tests for <see cref="CurrenciesController"/> result-to-HTTP mapping (SDD-NOM-001 §2.1,
/// SDD-INFRA-001). The service is mocked and the real <see cref="DefaultErrorCodeToStatusMap"/> drives
/// status mapping; no HTTP host is started, so these are pure controller-translation unit tests.
/// </summary>
[TestFixture]
[Category("SDD-NOM-001")]
public sealed class CurrenciesControllerTests
{
    private Mock<ICurrencyService> _serviceMock = null!;
    private CurrenciesController _sut = null!;

    /// <summary>Creates a fresh controller backed by a mocked service before each test.</summary>
    [SetUp]
    public void SetUp()
    {
        _serviceMock = new Mock<ICurrencyService>();
        _sut = new CurrenciesController(_serviceMock.Object, new DefaultErrorCodeToStatusMap());
    }

    /// <summary>A successful list returns 200 with the paged envelope.</summary>
    [Test]
    public async Task List_Returns200_WhenServiceSucceeds()
    {
        // Arrange
        PagedResult<CurrencyDto> page = new() { Items = [], TotalCount = 0, Page = 1, PageSize = 50 };
        _serviceMock
            .Setup(s => s.SearchAsync(It.IsAny<FilterRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<PagedResult<CurrencyDto>>.Success(page));

        // Act
        ActionResult<PagedResult<CurrencyDto>> result =
            await _sut.List(new FilterRequest(), CancellationToken.None);

        // Assert
        Assert.That(result.Result, Is.TypeOf<OkObjectResult>());
        Assert.That(((OkObjectResult)result.Result!).StatusCode, Is.EqualTo(200));
    }

    /// <summary>A list filter failure maps to a 400 ProblemDetails.</summary>
    [Test]
    public async Task List_Returns400_WhenFilterFails()
    {
        // Arrange
        _serviceMock
            .Setup(s => s.SearchAsync(It.IsAny<FilterRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<PagedResult<CurrencyDto>>.Failure("PAGE_SIZE_TOO_LARGE"));

        // Act
        ActionResult<PagedResult<CurrencyDto>> result =
            await _sut.List(new FilterRequest(), CancellationToken.None);

        // Assert
        Assert.That(result.Result, Is.TypeOf<ObjectResult>());
        Assert.That(((ObjectResult)result.Result!).StatusCode, Is.EqualTo(400));
    }

    /// <summary>A successful get returns 200 with the currency.</summary>
    [Test]
    public async Task Get_Returns200_WhenCurrencyExists()
    {
        // Arrange
        _serviceMock
            .Setup(s => s.GetByIsoCodeAsync("BGN", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<CurrencyDto>.Success(BuildDto("BGN")));

        // Act
        ActionResult<CurrencyDto> result = await _sut.Get("BGN", CancellationToken.None);

        // Assert
        Assert.That(result.Result, Is.TypeOf<OkObjectResult>());
        Assert.That(((OkObjectResult)result.Result!).StatusCode, Is.EqualTo(200));
    }

    /// <summary>A missing currency maps CURRENCY_NOT_FOUND to a 404 ProblemDetails.</summary>
    [Test]
    public async Task Get_Returns404_WhenCurrencyNotFound()
    {
        // Arrange
        _serviceMock
            .Setup(s => s.GetByIsoCodeAsync("ZZZ", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<CurrencyDto>.Failure("CURRENCY_NOT_FOUND"));

        // Act
        ActionResult<CurrencyDto> result = await _sut.Get("ZZZ", CancellationToken.None);

        // Assert
        Assert.That(result.Result, Is.TypeOf<ObjectResult>());
        Assert.That(((ObjectResult)result.Result!).StatusCode, Is.EqualTo(404));
    }

    /// <summary>A successful create returns 201 Created pointing at the Get action.</summary>
    [Test]
    public async Task Create_Returns201_WhenServiceSucceeds()
    {
        // Arrange
        _serviceMock
            .Setup(s => s.CreateAsync(It.IsAny<CreateCurrencyRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<CurrencyDto>.Success(BuildDto("USD")));

        // Act
        ActionResult<CurrencyDto> result = await _sut.Create(
            CreateCurrencyRequestBuilder.Create().WithIsoCode("USD").Build(), CancellationToken.None);

        // Assert
        Assert.That(result.Result, Is.TypeOf<CreatedAtActionResult>());
        Assert.That(((CreatedAtActionResult)result.Result!).StatusCode, Is.EqualTo(201));
    }

    /// <summary>A duplicate code maps DUPLICATE_CURRENCY_CODE to a 409 ProblemDetails.</summary>
    [Test]
    public async Task Create_Returns409_WhenDuplicateCode()
    {
        // Arrange
        _serviceMock
            .Setup(s => s.CreateAsync(It.IsAny<CreateCurrencyRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<CurrencyDto>.Failure("DUPLICATE_CURRENCY_CODE"));

        // Act
        ActionResult<CurrencyDto> result = await _sut.Create(
            CreateCurrencyRequestBuilder.Create().Build(), CancellationToken.None);

        // Assert
        Assert.That(result.Result, Is.TypeOf<ObjectResult>());
        Assert.That(((ObjectResult)result.Result!).StatusCode, Is.EqualTo(409));
    }

    /// <summary>An invalid currency code maps INVALID_CURRENCY_CODE to a 400 ProblemDetails.</summary>
    [Test]
    public async Task Create_Returns400_WhenInvalidCode()
    {
        // Arrange
        _serviceMock
            .Setup(s => s.CreateAsync(It.IsAny<CreateCurrencyRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<CurrencyDto>.Failure("INVALID_CURRENCY_CODE"));

        // Act
        ActionResult<CurrencyDto> result = await _sut.Create(
            CreateCurrencyRequestBuilder.Create().Build(), CancellationToken.None);

        // Assert
        Assert.That(result.Result, Is.TypeOf<ObjectResult>());
        Assert.That(((ObjectResult)result.Result!).StatusCode, Is.EqualTo(400));
    }

    /// <summary>A successful update returns 200 with the updated currency.</summary>
    [Test]
    public async Task Update_Returns200_WhenServiceSucceeds()
    {
        // Arrange
        _serviceMock
            .Setup(s => s.UpdateAsync("BGN", It.IsAny<UpdateCurrencyRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<CurrencyDto>.Success(BuildDto("BGN")));

        // Act
        ActionResult<CurrencyDto> result = await _sut.Update(
            "BGN", UpdateCurrencyRequestBuilder.Create().Build(), CancellationToken.None);

        // Assert
        Assert.That(result.Result, Is.TypeOf<OkObjectResult>());
        Assert.That(((OkObjectResult)result.Result!).StatusCode, Is.EqualTo(200));
    }

    /// <summary>An update on a missing currency maps CURRENCY_NOT_FOUND to a 404 ProblemDetails.</summary>
    [Test]
    public async Task Update_Returns404_WhenCurrencyNotFound()
    {
        // Arrange
        _serviceMock
            .Setup(s => s.UpdateAsync("ZZZ", It.IsAny<UpdateCurrencyRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<CurrencyDto>.Failure("CURRENCY_NOT_FOUND"));

        // Act
        ActionResult<CurrencyDto> result = await _sut.Update(
            "ZZZ", UpdateCurrencyRequestBuilder.Create().Build(), CancellationToken.None);

        // Assert
        Assert.That(result.Result, Is.TypeOf<ObjectResult>());
        Assert.That(((ObjectResult)result.Result!).StatusCode, Is.EqualTo(404));
    }

    /// <summary>A stale row version maps CONCURRENT_MODIFICATION to a 409 ProblemDetails.</summary>
    [Test]
    public async Task Update_Returns409_WhenConcurrentModification()
    {
        // Arrange
        _serviceMock
            .Setup(s => s.UpdateAsync("BGN", It.IsAny<UpdateCurrencyRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<CurrencyDto>.Failure("CONCURRENT_MODIFICATION"));

        // Act
        ActionResult<CurrencyDto> result = await _sut.Update(
            "BGN", UpdateCurrencyRequestBuilder.Create().Build(), CancellationToken.None);

        // Assert
        Assert.That(result.Result, Is.TypeOf<ObjectResult>());
        Assert.That(((ObjectResult)result.Result!).StatusCode, Is.EqualTo(409));
    }

    private static CurrencyDto BuildDto(string isoCode) => new()
    {
        Id = 1,
        IsoCode = isoCode,
        Name = "Test Currency",
        Symbol = "X",
        IsActive = true,
        RowVersion = "AAAAAAAAAAE="
    };
}
