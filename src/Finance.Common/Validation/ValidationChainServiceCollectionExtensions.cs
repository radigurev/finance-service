using Microsoft.Extensions.DependencyInjection;

namespace Finance.Common.Validation;

/// <summary>
/// Dependency-injection registration helpers for the validation chain.
/// <para>See <see cref="ValidationChain{TRequest}"/> and <see cref="IChainValidator{TRequest}"/>.</para>
/// </summary>
public static class ValidationChainServiceCollectionExtensions
{
    /// <summary>
    /// Registers the <see cref="ValidationChain{TRequest}"/> composer as scoped together with
    /// the supplied <see cref="IChainValidator{TRequest}"/> implementations, in the order given.
    /// </summary>
    /// <typeparam name="TRequest">The request type the chain validates.</typeparam>
    /// <param name="services">The service collection to register into.</param>
    /// <param name="validatorTypes">The validator implementation types, registered in order.</param>
    /// <returns>The same <see cref="IServiceCollection"/> for chaining.</returns>
    public static IServiceCollection AddValidationChain<TRequest>(
        this IServiceCollection services,
        params Type[] validatorTypes)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(validatorTypes);

        foreach (Type validatorType in validatorTypes)
        {
            GuardValidatorType<TRequest>(validatorType);
            services.AddScoped(typeof(IChainValidator<TRequest>), validatorType);
        }

        services.AddScoped<ValidationChain<TRequest>>();
        return services;
    }

    /// <summary>Verifies the supplied type implements <see cref="IChainValidator{TRequest}"/>.</summary>
    /// <param name="validatorType">The candidate validator type.</param>
    private static void GuardValidatorType<TRequest>(Type validatorType)
    {
        ArgumentNullException.ThrowIfNull(validatorType);

        if (!typeof(IChainValidator<TRequest>).IsAssignableFrom(validatorType))
        {
            throw new ArgumentException(
                $"Type '{validatorType.FullName}' does not implement IChainValidator<{typeof(TRequest).Name}>.",
                nameof(validatorType));
        }
    }
}
