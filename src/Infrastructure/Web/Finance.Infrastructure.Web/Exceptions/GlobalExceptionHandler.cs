using Finance.Common.ErrorCodes;
using Finance.GenericFiltering.Exceptions;
using Finance.Infrastructure.Web.ErrorMapping;
using Finance.Infrastructure.Web.ProblemDetails;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Finance.Infrastructure.Web.Exceptions;

/// <summary>
/// Global exception handler (SDD-INFRA-001 §1). Maps <see cref="FilterValidationException"/> to a
/// <c>400</c> ProblemDetails carrying its error code, and every other unhandled exception to a
/// <c>500</c> ProblemDetails titled <see cref="CommonErrorCodes.GENERIC_ERROR"/> — never leaking the
/// stack trace or exception detail to the client.
/// </summary>
public sealed class GlobalExceptionHandler : IExceptionHandler
{
    private readonly IErrorCodeToStatusMap _statusMap;
    private readonly ILogger<GlobalExceptionHandler> _logger;

    /// <summary>Initializes the handler with the status map and a logger.</summary>
    /// <param name="statusMap">The DI-registered error-code → HTTP-status map.</param>
    /// <param name="logger">The structured logger for the request path.</param>
    public GlobalExceptionHandler(IErrorCodeToStatusMap statusMap, ILogger<GlobalExceptionHandler> logger)
    {
        _statusMap = statusMap;
        _logger = logger;
    }

    /// <inheritdoc />
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(exception);

        Microsoft.AspNetCore.Mvc.ProblemDetails problem = BuildProblem(exception);
        httpContext.Response.StatusCode = problem.Status!.Value;
        await httpContext.Response
            .WriteAsJsonAsync(problem, cancellationToken)
            .ConfigureAwait(false);
        return true;
    }

    private Microsoft.AspNetCore.Mvc.ProblemDetails BuildProblem(Exception exception)
    {
        if (exception is FilterValidationException filterException)
        {
            int status = _statusMap.MapToStatus(filterException.ErrorCode);
            _logger.LogWarning(filterException, "Filter validation failed with code {ErrorCode}", filterException.ErrorCode);
            return FinanceProblemDetailsBuilder.Build(status, filterException.ErrorCode, filterException.Detail);
        }

        _logger.LogError(exception, "Unhandled exception mapped to {ErrorCode}", CommonErrorCodes.GENERIC_ERROR);
        return FinanceProblemDetailsBuilder.Build(
            StatusCodes.Status500InternalServerError, CommonErrorCodes.GENERIC_ERROR, null);
    }
}
