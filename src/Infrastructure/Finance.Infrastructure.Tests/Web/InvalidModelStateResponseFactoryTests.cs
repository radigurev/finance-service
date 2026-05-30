using Finance.Common.ErrorCodes;
using Finance.Infrastructure.Web.Extensions;
using Finance.Infrastructure.Web.ProblemDetails;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NUnit.Framework;

namespace Finance.Infrastructure.Tests.Web;

/// <summary>
/// Unit tests for the <c>InvalidModelStateResponseFactory</c> wired by
/// <see cref="ProblemDetailsServiceCollectionExtensions.AddFinanceProblemDetails"/>. Drives the exact
/// production path: a populated <see cref="ModelStateDictionary"/> is rendered as a 400
/// ProblemDetails with codes in the <c>errors</c> dictionary and <c>Title = VALIDATION_FAILED</c>
/// (SDD-INFRA-001 §2.2).
/// </summary>
[TestFixture]
[Category("SDD-INFRA-001")]
public sealed class InvalidModelStateResponseFactoryTests
{
    private ServiceProvider _provider = null!;

    /// <summary>Builds a DI container with the Finance ProblemDetails baseline wired.</summary>
    [SetUp]
    public void SetUp()
    {
        ServiceCollection services = new();
        services.AddLogging();
        services.AddFinanceProblemDetails();
        _provider = services.BuildServiceProvider();
    }

    /// <summary>Disposes the DI container after each test.</summary>
    [TearDown]
    public void TearDown()
    {
        _provider.Dispose();
    }

    /// <summary>The wired factory renders a populated model state as a 400 BadRequest ValidationProblemDetails.</summary>
    [Test]
    public void InvalidModelStateResponseFactory_PopulatedModelState_ReturnsBadRequest400()
    {
        // Arrange
        Func<ActionContext, IActionResult> factory = ResolveFactory();
        ActionContext actionContext = CreateActionContext(("Code", FilterErrorCodes.INVALID_FILTER_FIELD));

        // Act
        IActionResult result = factory(actionContext);

        // Assert
        Assert.That(result, Is.TypeOf<BadRequestObjectResult>());
        BadRequestObjectResult badRequest = (BadRequestObjectResult)result;
        Assert.Multiple(() =>
        {
            Assert.That(badRequest.StatusCode, Is.EqualTo(400));
            Assert.That(badRequest.Value, Is.TypeOf<ValidationProblemDetails>());
        });
    }

    /// <summary>The rendered ProblemDetails carries the VALIDATION_FAILED title, the finance.local type, and codes in the errors dictionary.</summary>
    [Test]
    public void InvalidModelStateResponseFactory_WithErrorCodes_BuildsValidationProblemDetails()
    {
        // Arrange
        Func<ActionContext, IActionResult> factory = ResolveFactory();
        ActionContext actionContext = CreateActionContext(
            ("Code", FilterErrorCodes.INVALID_FILTER_FIELD),
            ("PageSize", FilterErrorCodes.PAGE_SIZE_TOO_LARGE));

        // Act
        IActionResult result = factory(actionContext);

        // Assert
        BadRequestObjectResult badRequest = (BadRequestObjectResult)result;
        ValidationProblemDetails problem = (ValidationProblemDetails)badRequest.Value!;
        Assert.Multiple(() =>
        {
            Assert.That(problem.Status, Is.EqualTo(400));
            Assert.That(problem.Title, Is.EqualTo(CommonErrorCodes.VALIDATION_FAILED));
            Assert.That(
                problem.Type,
                Is.EqualTo(FinanceProblemDetailsBuilder.ErrorTypeBaseUri + CommonErrorCodes.VALIDATION_FAILED));
            Assert.That(problem.Errors["Code"], Contains.Item(FilterErrorCodes.INVALID_FILTER_FIELD));
            Assert.That(problem.Errors["PageSize"], Contains.Item(FilterErrorCodes.PAGE_SIZE_TOO_LARGE));
        });
    }

    /// <summary>The replaced <see cref="ProblemDetailsFactory"/> registration resolves to the Finance custom factory.</summary>
    [Test]
    public void AddFinanceProblemDetails_RegistersCustomProblemDetailsFactory()
    {
        // Arrange & Act
        ProblemDetailsFactory factory = _provider.GetRequiredService<ProblemDetailsFactory>();

        // Assert
        Assert.That(factory, Is.TypeOf<CustomProblemDetailsFactory>());
    }

    private Func<ActionContext, IActionResult> ResolveFactory()
    {
        ApiBehaviorOptions options =
            _provider.GetRequiredService<IOptions<ApiBehaviorOptions>>().Value;
        Assert.That(options.InvalidModelStateResponseFactory, Is.Not.Null);
        return options.InvalidModelStateResponseFactory;
    }

    private ActionContext CreateActionContext(params (string Field, string Code)[] errors)
    {
        DefaultHttpContext httpContext = new() { RequestServices = _provider };
        ModelStateDictionary modelState = new();
        foreach ((string field, string code) in errors)
        {
            modelState.AddModelError(field, code);
        }

        return new ActionContext(httpContext, new RouteData(), new ActionDescriptor(), modelState);
    }
}
