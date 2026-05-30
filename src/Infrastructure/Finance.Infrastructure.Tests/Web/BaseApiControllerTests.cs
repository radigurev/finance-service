using Finance.Common.Results;
using Finance.Infrastructure.Web.ErrorMapping;
using Finance.Infrastructure.Web.ProblemDetails;
using Microsoft.AspNetCore.Mvc;
using NUnit.Framework;

namespace Finance.Infrastructure.Tests.Web;

/// <summary>
/// Unit tests for <see cref="Finance.Infrastructure.Web.Controllers.BaseApiController"/> covering the
/// success path, status mapping, and ProblemDetails shape per SDD-INFRA-009 §2.4.
/// </summary>
[TestFixture]
[Category("SDD-INFRA-009")]
public sealed class BaseApiControllerTests
{
    private TestableApiController _controller = null!;

    /// <summary>Creates a controller backed by the default status map before each test.</summary>
    [SetUp]
    public void SetUp()
    {
        _controller = new TestableApiController(new DefaultErrorCodeToStatusMap());
    }

    /// <summary>A successful value-bearing result returns 200 with the value.</summary>
    [Test]
    public void ToActionResult_Success_Returns200WithValue()
    {
        // Arrange
        Result<string> result = Result<string>.Success("hello");

        // Act
        ActionResult<string> actionResult = _controller.Translate(result);

        // Assert
        Assert.That(actionResult.Result, Is.TypeOf<OkObjectResult>());
        OkObjectResult okResult = (OkObjectResult)actionResult.Result!;
        Assert.Multiple(() =>
        {
            Assert.That(okResult.StatusCode, Is.EqualTo(200));
            Assert.That(okResult.Value, Is.EqualTo("hello"));
        });
    }

    /// <summary>A successful void result returns 200.</summary>
    [Test]
    public void ToActionResult_VoidSuccess_Returns200()
    {
        // Arrange & Act
        ActionResult actionResult = _controller.Translate(Result.Success());

        // Assert
        Assert.That(actionResult, Is.TypeOf<OkResult>());
        OkResult okResult = (OkResult)actionResult;
        Assert.That(okResult.StatusCode, Is.EqualTo(200));
    }

    /// <summary>A not-found failure code is mapped to 404 by the registered status map.</summary>
    [Test]
    public void ToActionResult_MapsNotFoundCodeTo404()
    {
        // Arrange
        Result<string> result = Result<string>.Failure("ACCOUNT_NOT_FOUND");

        // Act
        ActionResult<string> actionResult = _controller.Translate(result);

        // Assert
        Assert.That(actionResult.Result, Is.TypeOf<ObjectResult>());
        ObjectResult objectResult = (ObjectResult)actionResult.Result!;
        Assert.That(objectResult.StatusCode, Is.EqualTo(404));
    }

    /// <summary>The failure ProblemDetails carries the code as title, the detail, and the finance.local type.</summary>
    [Test]
    public void ToActionResult_BuildsProblemDetailsWithTitleDetailAndType()
    {
        // Arrange
        Result<string> result = Result<string>.Failure("ACCOUNT_NOT_FOUND", "Account 7 was not found.");

        // Act
        ActionResult<string> actionResult = _controller.Translate(result);

        // Assert
        Assert.That(actionResult.Result, Is.TypeOf<ObjectResult>());
        ObjectResult objectResult = (ObjectResult)actionResult.Result!;
        Assert.That(objectResult.Value, Is.TypeOf<Microsoft.AspNetCore.Mvc.ProblemDetails>());
        Microsoft.AspNetCore.Mvc.ProblemDetails problem =
            (Microsoft.AspNetCore.Mvc.ProblemDetails)objectResult.Value!;
        Assert.Multiple(() =>
        {
            Assert.That(problem.Status, Is.EqualTo(404));
            Assert.That(problem.Title, Is.EqualTo("ACCOUNT_NOT_FOUND"));
            Assert.That(problem.Detail, Is.EqualTo("Account 7 was not found."));
            Assert.That(
                problem.Type,
                Is.EqualTo(FinanceProblemDetailsBuilder.ErrorTypeBaseUri + "ACCOUNT_NOT_FOUND"));
        });
    }

    /// <summary>A failure with no detail humanizes the error code into the ProblemDetails detail.</summary>
    [Test]
    public void ToActionResult_HumanizesDetail_WhenNoDetailSupplied()
    {
        // Arrange
        Result<string> result = Result<string>.Failure("ACCOUNT_NOT_FOUND");

        // Act
        ActionResult<string> actionResult = _controller.Translate(result);

        // Assert
        ObjectResult objectResult = (ObjectResult)actionResult.Result!;
        Microsoft.AspNetCore.Mvc.ProblemDetails problem =
            (Microsoft.AspNetCore.Mvc.ProblemDetails)objectResult.Value!;
        Assert.That(problem.Detail, Is.EqualTo("Account not found."));
    }
}
