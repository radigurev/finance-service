using System.Text.Json;
using Finance.Common.ErrorCodes;
using Finance.GenericFiltering.Exceptions;
using Finance.Infrastructure.Web.ErrorMapping;
using Finance.Infrastructure.Web.Exceptions;
using Finance.Infrastructure.Web.ProblemDetails;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace Finance.Infrastructure.Tests.Web;

/// <summary>
/// Unit tests for <see cref="GlobalExceptionHandler"/> covering the SDD-INFRA-001 §1 / §2.2 rules:
/// a <see cref="FilterValidationException"/> renders a 400 ProblemDetails carrying its error code,
/// and any other exception renders a 500 ProblemDetails titled <see cref="CommonErrorCodes.GENERIC_ERROR"/>
/// without leaking the exception message or stack trace.
/// </summary>
[TestFixture]
[Category("SDD-INFRA-001")]
public sealed class GlobalExceptionHandlerTests
{
    private GlobalExceptionHandler _handler = null!;

    /// <summary>Creates a handler backed by the default status map and a no-op logger before each test.</summary>
    [SetUp]
    public void SetUp()
    {
        _handler = new GlobalExceptionHandler(
            new DefaultErrorCodeToStatusMap(),
            NullLogger<GlobalExceptionHandler>.Instance);
    }

    /// <summary>A handled <see cref="FilterValidationException"/> is rendered as a 400 ProblemDetails.</summary>
    [Test]
    public async Task TryHandleAsync_FilterValidationException_WritesStatus400()
    {
        // Arrange
        DefaultHttpContext httpContext = CreateHttpContext();
        FilterValidationException exception = new(
            FilterErrorCodes.INVALID_FILTER_FIELD, "Field 'foo' is not filterable.");

        // Act
        bool handled = await _handler.TryHandleAsync(httpContext, exception, CancellationToken.None);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(handled, Is.True);
            Assert.That(httpContext.Response.StatusCode, Is.EqualTo(400));
        });
    }

    /// <summary>The FilterValidationException ProblemDetails carries the error code as title, the detail, and the finance.local type.</summary>
    [Test]
    public async Task TryHandleAsync_FilterValidationException_WritesErrorCodeTitleDetailAndType()
    {
        // Arrange
        DefaultHttpContext httpContext = CreateHttpContext();
        FilterValidationException exception = new(
            FilterErrorCodes.INVALID_FILTER_FIELD, "Field 'foo' is not filterable.");

        // Act
        await _handler.TryHandleAsync(httpContext, exception, CancellationToken.None);

        // Assert
        Microsoft.AspNetCore.Mvc.ProblemDetails problem = await ReadProblemAsync(httpContext);
        Assert.Multiple(() =>
        {
            Assert.That(problem.Status, Is.EqualTo(400));
            Assert.That(problem.Title, Is.EqualTo(FilterErrorCodes.INVALID_FILTER_FIELD));
            Assert.That(problem.Detail, Is.EqualTo("Field 'foo' is not filterable."));
            Assert.That(
                problem.Type,
                Is.EqualTo(FinanceProblemDetailsBuilder.ErrorTypeBaseUri + FilterErrorCodes.INVALID_FILTER_FIELD));
        });
    }

    /// <summary>A page-size FilterValidationException maps through the status map to a 400 with the matching code.</summary>
    [Test]
    public async Task TryHandleAsync_PageSizeTooLargeException_WritesMatchingCodeAt400()
    {
        // Arrange
        DefaultHttpContext httpContext = CreateHttpContext();
        FilterValidationException exception = new(
            FilterErrorCodes.PAGE_SIZE_TOO_LARGE, "Page size exceeds the allowed maximum of 200.");

        // Act
        await _handler.TryHandleAsync(httpContext, exception, CancellationToken.None);

        // Assert
        Microsoft.AspNetCore.Mvc.ProblemDetails problem = await ReadProblemAsync(httpContext);
        Assert.Multiple(() =>
        {
            Assert.That(httpContext.Response.StatusCode, Is.EqualTo(400));
            Assert.That(problem.Title, Is.EqualTo(FilterErrorCodes.PAGE_SIZE_TOO_LARGE));
        });
    }

    /// <summary>An unexpected exception is rendered as a 500 ProblemDetails titled GENERIC_ERROR.</summary>
    [Test]
    public async Task TryHandleAsync_UnexpectedException_WritesGenericError500()
    {
        // Arrange
        DefaultHttpContext httpContext = CreateHttpContext();
        InvalidOperationException exception = new("boom: secret connection string leaked here");

        // Act
        bool handled = await _handler.TryHandleAsync(httpContext, exception, CancellationToken.None);

        // Assert
        Microsoft.AspNetCore.Mvc.ProblemDetails problem = await ReadProblemAsync(httpContext);
        Assert.Multiple(() =>
        {
            Assert.That(handled, Is.True);
            Assert.That(httpContext.Response.StatusCode, Is.EqualTo(500));
            Assert.That(problem.Status, Is.EqualTo(500));
            Assert.That(problem.Title, Is.EqualTo(CommonErrorCodes.GENERIC_ERROR));
            Assert.That(
                problem.Type,
                Is.EqualTo(FinanceProblemDetailsBuilder.ErrorTypeBaseUri + CommonErrorCodes.GENERIC_ERROR));
        });
    }

    /// <summary>An unexpected exception MUST NOT leak its message or stack trace into the ProblemDetails detail.</summary>
    [Test]
    public async Task TryHandleAsync_UnexpectedException_DoesNotLeakMessageOrStack()
    {
        // Arrange
        DefaultHttpContext httpContext = CreateHttpContext();
        const string secret = "boom: secret connection string leaked here";
        InvalidOperationException exception = new(secret);

        // Act
        await _handler.TryHandleAsync(httpContext, exception, CancellationToken.None);

        // Assert
        Microsoft.AspNetCore.Mvc.ProblemDetails problem = await ReadProblemAsync(httpContext);
        Assert.Multiple(() =>
        {
            Assert.That(problem.Detail, Does.Not.Contain(secret));
            Assert.That(problem.Detail, Does.Not.Contain("InvalidOperationException"));
            Assert.That(problem.Detail, Does.Not.Contain("at Finance"));
            Assert.That(
                problem.Detail,
                Is.EqualTo(FinanceProblemDetailsBuilder.Humanize(CommonErrorCodes.GENERIC_ERROR)));
        });
    }

    private static DefaultHttpContext CreateHttpContext()
    {
        DefaultHttpContext httpContext = new();
        httpContext.Response.Body = new MemoryStream();
        return httpContext;
    }

    private static async Task<Microsoft.AspNetCore.Mvc.ProblemDetails> ReadProblemAsync(HttpContext httpContext)
    {
        httpContext.Response.Body.Seek(0, SeekOrigin.Begin);
        Microsoft.AspNetCore.Mvc.ProblemDetails? problem =
            await JsonSerializer.DeserializeAsync<Microsoft.AspNetCore.Mvc.ProblemDetails>(
                httpContext.Response.Body,
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.That(problem, Is.Not.Null);
        return problem!;
    }
}
