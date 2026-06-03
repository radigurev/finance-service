using Finance.Common.ErrorCodes;
using Finance.Common.Results;
using Finance.Infrastructure.Web.ErrorMapping;
using Finance.Journal.API.Controllers;
using Finance.Journal.API.Interfaces;
using Finance.ServiceModel.Journal;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using NUnit.Framework;

namespace Finance.Journal.API.Tests.Unit.Services;

/// <summary>
/// Unit tests for the request-shape validation owned by <see cref="GeneralLedgerController"/> and the Journal
/// error-code surface required by SDD-FIN-003 §6.3: a missing <c>asOfDate</c> short-circuits to
/// <c>INVALID_DATE_RANGE</c> (400) before the service is touched, and the new <c>INVALID_ACCOUNT_ID</c>
/// constant exists. The controller is exercised with a mocked <see cref="IGeneralLedgerService"/> so the
/// short-circuit is verifiable in isolation; the status map maps <c>INVALID_DATE_RANGE</c> to 400.
/// </summary>
[TestFixture]
[Category("SDD-FIN-003")]
public sealed class GeneralLedgerControllerValidationTests
{
    private Mock<IGeneralLedgerService> _serviceMock = null!;
    private Mock<IErrorCodeToStatusMap> _statusMapMock = null!;
    private GeneralLedgerController _controller = null!;

    /// <summary>Creates fresh mocks and a controller before each test.</summary>
    [SetUp]
    public void SetUp()
    {
        _serviceMock = new Mock<IGeneralLedgerService>();
        _statusMapMock = new Mock<IErrorCodeToStatusMap>();
        _statusMapMock
            .Setup(map => map.MapToStatus(It.IsAny<string>()))
            .Returns(StatusCodes.Status400BadRequest);
        _controller = new GeneralLedgerController(_serviceMock.Object, _statusMapMock.Object);
    }

    /// <summary>A trial-balance request with no asOfDate returns INVALID_DATE_RANGE without calling the service (§3.1, §4, §6.3).</summary>
    [Test]
    public async Task Validate_MissingAsOfDate_ReturnsInvalidDateRange()
    {
        // Arrange — no asOfDate supplied.

        // Act
        ActionResult<TrialBalanceDto> action = await _controller.GetTrialBalance(
            null, null, CancellationToken.None);

        // Assert — a 400 ProblemDetails carrying INVALID_DATE_RANGE, with the service untouched.
        ObjectResult objectResult = (ObjectResult)action.Result!;
        ProblemDetails problem = (ProblemDetails)objectResult.Value!;
        Assert.Multiple(() =>
        {
            Assert.That(objectResult.StatusCode, Is.EqualTo(StatusCodes.Status400BadRequest));
            Assert.That(problem.Title, Is.EqualTo(JournalErrorCodes.INVALID_DATE_RANGE));
        });
        _serviceMock.Verify(
            service => service.GetTrialBalanceAsync(
                It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>A present asOfDate forwards the request to the service (§2.2, §6.3).</summary>
    [Test]
    public async Task GetTrialBalance_WithAsOfDate_ForwardsToService()
    {
        // Arrange
        DateTimeOffset asOf = new(2026, 12, 31, 0, 0, 0, TimeSpan.Zero);
        TrialBalanceDto dto = new()
        {
            AsOfDate = asOf,
            Rows = [],
            GrandTotalDebit = 0m,
            GrandTotalCredit = 0m,
            Balanced = true
        };
        _serviceMock
            .Setup(service => service.GetTrialBalanceAsync(asOf, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<TrialBalanceDto>.Success(dto));

        // Act
        ActionResult<TrialBalanceDto> action = await _controller.GetTrialBalance(
            asOf, null, CancellationToken.None);

        // Assert
        OkObjectResult okResult = (OkObjectResult)action.Result!;
        Assert.Multiple(() =>
        {
            Assert.That(okResult.StatusCode, Is.EqualTo(StatusCodes.Status200OK));
            Assert.That(okResult.Value, Is.SameAs(dto));
        });
        _serviceMock.Verify(
            service => service.GetTrialBalanceAsync(asOf, null, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>The INVALID_ACCOUNT_ID error-code constant required by SDD-FIN-003 §4 exists (§6.3).</summary>
    [Test]
    public void JournalErrorCodes_DefinesInvalidAccountId()
    {
        // Arrange & Act & Assert
        Assert.That(JournalErrorCodes.INVALID_ACCOUNT_ID, Is.EqualTo("INVALID_ACCOUNT_ID"));
    }
}
