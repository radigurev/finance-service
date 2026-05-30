using Finance.Common.Results;
using Finance.Infrastructure.Web.ErrorMapping;
using Finance.Infrastructure.Web.ProblemDetails;
using Microsoft.AspNetCore.Mvc;

namespace Finance.Infrastructure.Web.Controllers;

/// <summary>
/// Base controller translating service-layer <see cref="Result"/> / <see cref="Result{T}"/> outcomes
/// into <see cref="ActionResult"/>s, mapping failure codes to HTTP statuses via the registered
/// <see cref="IErrorCodeToStatusMap"/> and rendering RFC 7807 ProblemDetails (SDD-INFRA-009 §2.4).
/// </summary>
[ApiController]
public abstract class BaseApiController : ControllerBase
{
    private readonly IErrorCodeToStatusMap _statusMap;

    /// <summary>Initializes the controller with the error-code-to-status mapping.</summary>
    /// <param name="statusMap">The DI-registered error-code → HTTP-status map.</param>
    protected BaseApiController(IErrorCodeToStatusMap statusMap)
    {
        _statusMap = statusMap;
    }

    /// <summary>Translates a value-bearing <see cref="Result{T}"/> into an <see cref="ActionResult{T}"/>.</summary>
    /// <typeparam name="T">The value type carried on success.</typeparam>
    /// <param name="result">The service outcome to translate.</param>
    /// <returns><c>200 OK</c> with the value on success; a ProblemDetails on failure.</returns>
    protected ActionResult<T> ToActionResult<T>(Result<T> result)
    {
        if (result.IsSuccess)
        {
            return Ok(result.Value);
        }

        return BuildProblem(result.ErrorCode!, result.Detail);
    }

    /// <summary>Translates a void <see cref="Result"/> into an <see cref="ActionResult"/>.</summary>
    /// <param name="result">The service outcome to translate.</param>
    /// <returns><c>200 OK</c> on success; a ProblemDetails on failure.</returns>
    protected ActionResult ToActionResult(Result result)
    {
        if (result.IsSuccess)
        {
            return Ok();
        }

        return BuildProblem(result.ErrorCode!, result.Detail);
    }

    private ObjectResult BuildProblem(string errorCode, string? detail)
    {
        int status = _statusMap.MapToStatus(errorCode);
        Microsoft.AspNetCore.Mvc.ProblemDetails problem =
            FinanceProblemDetailsBuilder.Build(status, errorCode, detail);
        return new ObjectResult(problem) { StatusCode = status };
    }
}
