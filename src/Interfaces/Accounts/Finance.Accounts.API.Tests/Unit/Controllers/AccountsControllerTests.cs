using Finance.Accounts.API.Controllers;
using Finance.Accounts.API.Interfaces;
using Finance.Accounts.API.Tests.Builders;
using Finance.Common.Enums;
using Finance.Common.Results;
using Finance.GenericFiltering.Models;
using Finance.Infrastructure.Web.ErrorMapping;
using Finance.ServiceModel.Accounts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Moq;
using NUnit.Framework;

namespace Finance.Accounts.API.Tests.Unit.Controllers;

/// <summary>
/// Unit tests for <see cref="AccountsController"/> result-to-HTTP mapping (SDD-ACCT-001 §2, SDD-INFRA-001).
/// The service is mocked and the real <see cref="DefaultErrorCodeToStatusMap"/> drives status mapping; no
/// HTTP host is started, so these are pure controller-translation unit tests.
/// </summary>
[TestFixture]
[Category("SDD-ACCT-001")]
public sealed class AccountsControllerTests
{
    private Mock<IAccountService> _serviceMock = null!;
    private AccountsController _sut = null!;

    /// <summary>Creates a fresh controller backed by a mocked service before each test.</summary>
    [SetUp]
    public void SetUp()
    {
        _serviceMock = new Mock<IAccountService>();

        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Country:Code"] = "BG" })
            .Build();

        _sut = new AccountsController(_serviceMock.Object, configuration, new DefaultErrorCodeToStatusMap());
    }

    /// <summary>A successful list returns 200 with the paged envelope.</summary>
    [Test]
    public async Task List_Returns200_WhenServiceSucceeds()
    {
        // Arrange
        PagedResult<AccountDto> page = new() { Items = [], TotalCount = 0, Page = 1, PageSize = 50 };
        _serviceMock
            .Setup(s => s.SearchAsync(It.IsAny<FilterRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<PagedResult<AccountDto>>.Success(page));

        // Act
        ActionResult<PagedResult<AccountDto>> result = await _sut.List(new FilterRequest(), CancellationToken.None);

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
            .ReturnsAsync(Result<PagedResult<AccountDto>>.Failure("PAGE_SIZE_TOO_LARGE"));

        // Act
        ActionResult<PagedResult<AccountDto>> result = await _sut.List(new FilterRequest(), CancellationToken.None);

        // Assert
        Assert.That(result.Result, Is.TypeOf<ObjectResult>());
        Assert.That(((ObjectResult)result.Result!).StatusCode, Is.EqualTo(400));
    }

    /// <summary>A successful get returns 200 with the account.</summary>
    [Test]
    public async Task Get_Returns200_WhenAccountExists()
    {
        // Arrange
        _serviceMock
            .Setup(s => s.GetAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<AccountDto>.Success(BuildDto(1)));

        // Act
        ActionResult<AccountDto> result = await _sut.Get(1, CancellationToken.None);

        // Assert
        Assert.That(result.Result, Is.TypeOf<OkObjectResult>());
        Assert.That(((OkObjectResult)result.Result!).StatusCode, Is.EqualTo(200));
    }

    /// <summary>A missing account maps ACCOUNT_NOT_FOUND to a 404 ProblemDetails.</summary>
    [Test]
    public async Task Get_Returns404_WhenAccountNotFound()
    {
        // Arrange
        _serviceMock
            .Setup(s => s.GetAsync(99, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<AccountDto>.Failure("ACCOUNT_NOT_FOUND"));

        // Act
        ActionResult<AccountDto> result = await _sut.Get(99, CancellationToken.None);

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
            .Setup(s => s.CreateAsync(It.IsAny<CreateAccountRequest>(), "BG", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<AccountDto>.Success(BuildDto(7)));

        // Act
        ActionResult<AccountDto> result = await _sut.Create(
            CreateAccountRequestBuilder.Create().Build(), CancellationToken.None);

        // Assert
        Assert.That(result.Result, Is.TypeOf<CreatedAtActionResult>());
        Assert.That(((CreatedAtActionResult)result.Result!).StatusCode, Is.EqualTo(201));
    }

    /// <summary>A duplicate code maps DUPLICATE_ACCOUNT_CODE to a 409 ProblemDetails.</summary>
    [Test]
    public async Task Create_Returns409_WhenDuplicateCode()
    {
        // Arrange
        _serviceMock
            .Setup(s => s.CreateAsync(It.IsAny<CreateAccountRequest>(), "BG", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<AccountDto>.Failure("DUPLICATE_ACCOUNT_CODE"));

        // Act
        ActionResult<AccountDto> result = await _sut.Create(
            CreateAccountRequestBuilder.Create().Build(), CancellationToken.None);

        // Assert
        Assert.That(result.Result, Is.TypeOf<ObjectResult>());
        Assert.That(((ObjectResult)result.Result!).StatusCode, Is.EqualTo(409));
    }

    /// <summary>An invalid parent maps INVALID_PARENT_ACCOUNT to a 400 ProblemDetails.</summary>
    [Test]
    public async Task Create_Returns400_WhenParentInvalid()
    {
        // Arrange
        _serviceMock
            .Setup(s => s.CreateAsync(It.IsAny<CreateAccountRequest>(), "BG", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<AccountDto>.Failure("INVALID_PARENT_ACCOUNT"));

        // Act
        ActionResult<AccountDto> result = await _sut.Create(
            CreateAccountRequestBuilder.Create().Build(), CancellationToken.None);

        // Assert
        Assert.That(result.Result, Is.TypeOf<ObjectResult>());
        Assert.That(((ObjectResult)result.Result!).StatusCode, Is.EqualTo(400));
    }

    /// <summary>A successful update returns 200 with the updated account.</summary>
    [Test]
    public async Task Update_Returns200_WhenServiceSucceeds()
    {
        // Arrange
        _serviceMock
            .Setup(s => s.UpdateAsync(5, It.IsAny<UpdateAccountRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<AccountDto>.Success(BuildDto(5)));

        // Act
        ActionResult<AccountDto> result = await _sut.Update(
            5, UpdateAccountRequestBuilder.Create().WithRowVersion("AAAAAAAAAAE=").Build(), CancellationToken.None);

        // Assert
        Assert.That(result.Result, Is.TypeOf<OkObjectResult>());
        Assert.That(((OkObjectResult)result.Result!).StatusCode, Is.EqualTo(200));
    }

    /// <summary>An update on a missing account maps ACCOUNT_NOT_FOUND to a 404 ProblemDetails.</summary>
    [Test]
    public async Task Update_Returns404_WhenAccountNotFound()
    {
        // Arrange
        _serviceMock
            .Setup(s => s.UpdateAsync(99, It.IsAny<UpdateAccountRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<AccountDto>.Failure("ACCOUNT_NOT_FOUND"));

        // Act
        ActionResult<AccountDto> result = await _sut.Update(
            99, UpdateAccountRequestBuilder.Create().WithRowVersion("AAAAAAAAAAE=").Build(), CancellationToken.None);

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
            .Setup(s => s.UpdateAsync(5, It.IsAny<UpdateAccountRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<AccountDto>.Failure("CONCURRENT_MODIFICATION"));

        // Act
        ActionResult<AccountDto> result = await _sut.Update(
            5, UpdateAccountRequestBuilder.Create().WithRowVersion("AAAAAAAAAAE=").Build(), CancellationToken.None);

        // Assert
        Assert.That(result.Result, Is.TypeOf<ObjectResult>());
        Assert.That(((ObjectResult)result.Result!).StatusCode, Is.EqualTo(409));
    }

    private static AccountDto BuildDto(int id) => new()
    {
        Id = id,
        Code = "401",
        Name = "Доставчици",
        Type = AccountType.Liability,
        ParentId = null,
        IsActive = true,
        CountryCode = "BG",
        RowVersion = "AAAAAAAAAAE="
    };
}
