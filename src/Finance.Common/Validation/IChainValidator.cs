namespace Finance.Common.Validation;

/// <summary>
/// A single cross-cutting, stateful validation step for a request type. Implementations
/// inject their own data dependencies and MUST be pure functions of (request, current state).
/// <para>Composed and run in registration order by <see cref="ValidationChain{TRequest}"/>.</para>
/// </summary>
/// <typeparam name="TRequest">The request type being validated.</typeparam>
public interface IChainValidator<in TRequest>
{
    /// <summary>Validates the supplied request against current state without mutating anything.</summary>
    /// <param name="request">The request to validate.</param>
    /// <param name="ct">A token to observe for cancellation.</param>
    /// <returns>A <see cref="ChainValidationResult"/> describing success or the failing error code.</returns>
    Task<ChainValidationResult> ValidateAsync(TRequest request, CancellationToken ct);
}
