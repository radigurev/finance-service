using Finance.Common.ErrorCodes;
using Finance.Infrastructure.Web.ErrorMapping;
using Finance.Infrastructure.Web.Exceptions;
using Finance.Infrastructure.Web.ProblemDetails;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Finance.Infrastructure.Web.Extensions;

/// <summary>
/// DI registration of the Finance ProblemDetails baseline: the error-code → status map, the custom
/// <see cref="ProblemDetailsFactory"/>, the FluentValidation model-state response factory, and the
/// global exception handler (SDD-INFRA-001 §1, §2.2).
/// </summary>
public static class ProblemDetailsServiceCollectionExtensions
{
    /// <summary>
    /// Wires the ProblemDetails customization, the default <see cref="IErrorCodeToStatusMap"/>
    /// (overridable), the validation model-state response factory, and the
    /// <see cref="GlobalExceptionHandler"/> with the platform <see cref="IProblemDetailsService"/>.
    /// </summary>
    /// <param name="services">The service collection to add registrations to.</param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    public static IServiceCollection AddFinanceProblemDetails(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<IErrorCodeToStatusMap, DefaultErrorCodeToStatusMap>();
        services.AddProblemDetails();
        services.Replace(ServiceDescriptor.Singleton<ProblemDetailsFactory, CustomProblemDetailsFactory>());
        services.AddExceptionHandler<GlobalExceptionHandler>();

        services.Configure<ApiBehaviorOptions>(options =>
            options.InvalidModelStateResponseFactory = BuildValidationResponse);

        return services;
    }

    private static IActionResult BuildValidationResponse(ActionContext context)
    {
        ProblemDetailsFactory factory = context.HttpContext.RequestServices
            .GetRequiredService<ProblemDetailsFactory>();

        ValidationProblemDetails problem = factory.CreateValidationProblemDetails(
            context.HttpContext,
            context.ModelState,
            StatusCodes.Status400BadRequest,
            CommonErrorCodes.VALIDATION_FAILED);

        return new BadRequestObjectResult(problem);
    }
}
