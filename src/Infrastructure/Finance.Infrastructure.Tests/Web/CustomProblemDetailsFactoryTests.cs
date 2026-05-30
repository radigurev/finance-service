using Finance.Common.ErrorCodes;
using Finance.Infrastructure.Web.ProblemDetails;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using NUnit.Framework;

namespace Finance.Infrastructure.Tests.Web;

/// <summary>
/// Unit tests for <see cref="CustomProblemDetailsFactory"/> covering the SDD-INFRA-001 §2.2 rules:
/// validation ProblemDetails carry <see cref="CommonErrorCodes.VALIDATION_FAILED"/> as the title, the
/// finance.local type, and the FluentValidation-style error codes in the <c>errors</c> dictionary.
/// </summary>
[TestFixture]
[Category("SDD-INFRA-001")]
public sealed class CustomProblemDetailsFactoryTests
{
    private CustomProblemDetailsFactory _factory = null!;
    private DefaultHttpContext _httpContext = null!;

    /// <summary>Creates a fresh factory and HTTP context before each test.</summary>
    [SetUp]
    public void SetUp()
    {
        _factory = new CustomProblemDetailsFactory();
        _httpContext = new DefaultHttpContext();
    }

    /// <summary>A validation ProblemDetails defaults to 400 with the VALIDATION_FAILED title and finance.local type.</summary>
    [Test]
    public void CreateValidationProblemDetails_PopulatedModelState_SetsTitleStatusAndType()
    {
        // Arrange
        ModelStateDictionary modelState = new();
        modelState.AddModelError("Code", FilterErrorCodes.INVALID_FILTER_FIELD);

        // Act
        ValidationProblemDetails problem = _factory.CreateValidationProblemDetails(
            _httpContext, modelState, StatusCodes.Status400BadRequest, CommonErrorCodes.VALIDATION_FAILED);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(problem.Status, Is.EqualTo(400));
            Assert.That(problem.Title, Is.EqualTo(CommonErrorCodes.VALIDATION_FAILED));
            Assert.That(
                problem.Type,
                Is.EqualTo(FinanceProblemDetailsBuilder.ErrorTypeBaseUri + CommonErrorCodes.VALIDATION_FAILED));
        });
    }

    /// <summary>The FluentValidation-style error codes land in the ProblemDetails <c>errors</c> dictionary keyed by field.</summary>
    [Test]
    public void CreateValidationProblemDetails_WithErrorCodes_PlacesCodesInErrorsDictionary()
    {
        // Arrange
        ModelStateDictionary modelState = new();
        modelState.AddModelError("Code", FilterErrorCodes.INVALID_FILTER_FIELD);
        modelState.AddModelError("PageSize", FilterErrorCodes.PAGE_SIZE_TOO_LARGE);

        // Act
        ValidationProblemDetails problem = _factory.CreateValidationProblemDetails(
            _httpContext, modelState, StatusCodes.Status400BadRequest, CommonErrorCodes.VALIDATION_FAILED);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(problem.Errors, Contains.Key("Code"));
            Assert.That(problem.Errors["Code"], Contains.Item(FilterErrorCodes.INVALID_FILTER_FIELD));
            Assert.That(problem.Errors, Contains.Key("PageSize"));
            Assert.That(problem.Errors["PageSize"], Contains.Item(FilterErrorCodes.PAGE_SIZE_TOO_LARGE));
        });
    }

    /// <summary>When no title is supplied, the validation factory defaults the title to VALIDATION_FAILED.</summary>
    [Test]
    public void CreateValidationProblemDetails_NoTitleSupplied_DefaultsToValidationFailed()
    {
        // Arrange
        ModelStateDictionary modelState = new();
        modelState.AddModelError("Code", FilterErrorCodes.INVALID_FILTER_FIELD);

        // Act
        ValidationProblemDetails problem =
            _factory.CreateValidationProblemDetails(_httpContext, modelState);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(problem.Status, Is.EqualTo(400));
            Assert.That(problem.Title, Is.EqualTo(CommonErrorCodes.VALIDATION_FAILED));
            Assert.That(
                problem.Type,
                Is.EqualTo(FinanceProblemDetailsBuilder.ErrorTypeBaseUri + CommonErrorCodes.VALIDATION_FAILED));
        });
    }

    /// <summary>A null model-state dictionary is rejected with an <see cref="ArgumentNullException"/>.</summary>
    [Test]
    public void CreateValidationProblemDetails_NullModelState_ThrowsArgumentNullException()
    {
        // Arrange & Act & Assert
        Assert.That(
            () => _factory.CreateValidationProblemDetails(_httpContext, null!),
            Throws.TypeOf<ArgumentNullException>());
    }

    /// <summary>A non-validation ProblemDetails defaults to 500 with the GENERIC_ERROR title and finance.local type.</summary>
    [Test]
    public void CreateProblemDetails_NoArguments_DefaultsToGenericError500()
    {
        // Arrange & Act
        Microsoft.AspNetCore.Mvc.ProblemDetails problem = _factory.CreateProblemDetails(_httpContext);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(problem.Status, Is.EqualTo(500));
            Assert.That(problem.Title, Is.EqualTo(CommonErrorCodes.GENERIC_ERROR));
            Assert.That(
                problem.Type,
                Is.EqualTo(FinanceProblemDetailsBuilder.ErrorTypeBaseUri + CommonErrorCodes.GENERIC_ERROR));
            Assert.That(
                problem.Detail,
                Is.EqualTo(FinanceProblemDetailsBuilder.Humanize(CommonErrorCodes.GENERIC_ERROR)));
        });
    }

    /// <summary>An explicit error code becomes the title and the type suffix of the ProblemDetails.</summary>
    [Test]
    public void CreateProblemDetails_ExplicitCodeAndStatus_SetsTitleStatusAndType()
    {
        // Arrange & Act
        Microsoft.AspNetCore.Mvc.ProblemDetails problem = _factory.CreateProblemDetails(
            _httpContext, StatusCodes.Status404NotFound, "ACCOUNT_NOT_FOUND", detail: "Account 7 was not found.");

        // Assert
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
}
