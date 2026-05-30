using Finance.Common.ErrorCodes;
using Finance.Common.Results;
using Finance.EventLog.API.Controllers;
using Finance.EventLog.API.Interfaces;
using Finance.GenericFiltering.Models;
using Finance.Infrastructure.Web.ErrorMapping;
using Finance.Infrastructure.Web.ProblemDetails;
using Finance.ServiceModel.EventLog;
using Microsoft.AspNetCore.Mvc;
using Moq;
using NUnit.Framework;

namespace Finance.EventLog.API.Tests.Unit.Controllers;

/// <summary>
/// Unit tests for <see cref="EventsController"/> verifying the <c>BaseApiController.ToActionResult</c>
/// translation (SDD-EVTLOG-001 §2.4, SDD-INFRA-009 §2.4): a success <see cref="Result{T}"/> becomes
/// <c>200 OK</c> carrying the page, while a failure code maps through the real
/// <see cref="DefaultErrorCodeToStatusMap"/> to the correct HTTP status and an RFC 7807 ProblemDetails.
/// Runs fully offline with a mocked query service.
/// </summary>
[TestFixture]
[Category("SDD-EVTLOG-001")]
public sealed class EventsControllerTests
{
    private Mock<IEventQueryService> _queryServiceMock = null!;
    private EventsController _controller = null!;

    /// <summary>Creates fresh mocks and the controller under test before each test.</summary>
    [SetUp]
    public void SetUp()
    {
        _queryServiceMock = new Mock<IEventQueryService>();
        _controller = new EventsController(_queryServiceMock.Object, new DefaultErrorCodeToStatusMap());
    }

    /// <summary>A success result is translated into a 200 OK carrying the page (SDD-EVTLOG-001 §2.4).</summary>
    [Test]
    public async Task List_ServiceSucceeds_ReturnsOkWithPagedResult()
    {
        // Arrange
        PagedResult<EventLogEntryDto> page = new()
        {
            Items = [],
            TotalCount = 0,
            Page = 1,
            PageSize = 50
        };
        _queryServiceMock
            .Setup(s => s.SearchAsync(
                It.IsAny<FilterRequest>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<PagedResult<EventLogEntryDto>>.Success(page));

        // Act
        ActionResult<PagedResult<EventLogEntryDto>> actionResult =
            await _controller.List(new FilterRequest(), null, CancellationToken.None);

        // Assert
        OkObjectResult? ok = actionResult.Result as OkObjectResult;
        Assert.Multiple(() =>
        {
            Assert.That(ok, Is.Not.Null);
            Assert.That(ok!.StatusCode, Is.EqualTo(200));
            Assert.That(ok.Value, Is.SameAs(page));
        });
    }

    /// <summary>An INVALID_DATE_RANGE failure maps to a 400 ProblemDetails (SDD-EVTLOG-001 §3, §4).</summary>
    [Test]
    public async Task List_InvalidDateRange_ReturnsBadRequestProblemDetails()
    {
        // Arrange
        _queryServiceMock
            .Setup(s => s.SearchAsync(
                It.IsAny<FilterRequest>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<PagedResult<EventLogEntryDto>>.Failure(
                EventLogErrorCodes.INVALID_DATE_RANGE,
                "The supplied 'from' date is after the 'to' date."));

        // Act
        ActionResult<PagedResult<EventLogEntryDto>> actionResult =
            await _controller.List(new FilterRequest(), null, CancellationToken.None);

        // Assert
        ObjectResult? problemResult = actionResult.Result as ObjectResult;
        ProblemDetails? problem = problemResult?.Value as ProblemDetails;
        Assert.Multiple(() =>
        {
            Assert.That(problemResult, Is.Not.Null);
            Assert.That(problemResult!.StatusCode, Is.EqualTo(400));
            Assert.That(problem, Is.Not.Null);
            Assert.That(problem!.Title, Is.EqualTo(EventLogErrorCodes.INVALID_DATE_RANGE));
            Assert.That(
                problem.Type,
                Is.EqualTo(FinanceProblemDetailsBuilder.ErrorTypeBaseUri + EventLogErrorCodes.INVALID_DATE_RANGE));
        });
    }

    /// <summary>A PAGE_SIZE_TOO_LARGE failure maps to a 400 ProblemDetails (SDD-EVTLOG-001 §3, §4).</summary>
    [Test]
    public async Task List_PageSizeTooLarge_ReturnsBadRequestProblemDetails()
    {
        // Arrange
        _queryServiceMock
            .Setup(s => s.SearchAsync(
                It.IsAny<FilterRequest>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<PagedResult<EventLogEntryDto>>.Failure(
                FilterErrorCodes.PAGE_SIZE_TOO_LARGE,
                "The requested page size exceeds the maximum of 200."));

        // Act
        ActionResult<PagedResult<EventLogEntryDto>> actionResult =
            await _controller.List(new FilterRequest { PageSize = 500 }, null, CancellationToken.None);

        // Assert
        ObjectResult? problemResult = actionResult.Result as ObjectResult;
        ProblemDetails? problem = problemResult?.Value as ProblemDetails;
        Assert.Multiple(() =>
        {
            Assert.That(problemResult, Is.Not.Null);
            Assert.That(problemResult!.StatusCode, Is.EqualTo(400));
            Assert.That(problem, Is.Not.Null);
            Assert.That(problem!.Title, Is.EqualTo(FilterErrorCodes.PAGE_SIZE_TOO_LARGE));
        });
    }
}
