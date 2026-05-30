using Finance.Common.ErrorCodes;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Finance.Infrastructure.Web.ProblemDetails;

/// <summary>
/// A <see cref="ProblemDetailsFactory"/> that applies the Finance conventions of SDD-INFRA-001 §2.2:
/// the <c>type</c> field is set to <c>https://finance.local/errors/{title}</c> and validation
/// ProblemDetails carry <see cref="CommonErrorCodes.VALIDATION_FAILED"/> as the title.
/// </summary>
public sealed class CustomProblemDetailsFactory : ProblemDetailsFactory
{
    /// <inheritdoc />
    public override Microsoft.AspNetCore.Mvc.ProblemDetails CreateProblemDetails(
        HttpContext httpContext,
        int? statusCode = null,
        string? title = null,
        string? type = null,
        string? detail = null,
        string? instance = null)
    {
        int status = statusCode ?? StatusCodes.Status500InternalServerError;
        string resolvedTitle = title ?? CommonErrorCodes.GENERIC_ERROR;
        Microsoft.AspNetCore.Mvc.ProblemDetails problem = new()
        {
            Status = status,
            Title = resolvedTitle,
            Detail = detail ?? FinanceProblemDetailsBuilder.Humanize(resolvedTitle),
            Type = type ?? FinanceProblemDetailsBuilder.ErrorTypeBaseUri + resolvedTitle,
            Instance = instance
        };
        return problem;
    }

    /// <inheritdoc />
    public override ValidationProblemDetails CreateValidationProblemDetails(
        HttpContext httpContext,
        ModelStateDictionary modelStateDictionary,
        int? statusCode = null,
        string? title = null,
        string? type = null,
        string? detail = null,
        string? instance = null)
    {
        ArgumentNullException.ThrowIfNull(modelStateDictionary);

        int status = statusCode ?? StatusCodes.Status400BadRequest;
        ValidationProblemDetails problem = new(modelStateDictionary)
        {
            Status = status,
            Title = title ?? CommonErrorCodes.VALIDATION_FAILED,
            Detail = detail,
            Type = type ?? FinanceProblemDetailsBuilder.ErrorTypeBaseUri + CommonErrorCodes.VALIDATION_FAILED,
            Instance = instance
        };
        return problem;
    }
}
