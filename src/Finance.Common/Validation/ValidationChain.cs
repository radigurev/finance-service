namespace Finance.Common.Validation;

/// <summary>
/// Composes the DI-registered <see cref="IChainValidator{TRequest}"/> set for a request type,
/// running them in registration order and short-circuiting on the first failure.
/// <para>Registered via <see cref="ValidationChainServiceCollectionExtensions.AddValidationChain{TRequest}"/>.</para>
/// </summary>
/// <typeparam name="TRequest">The request type being validated.</typeparam>
public sealed class ValidationChain<TRequest>
{
    private readonly IReadOnlyList<IChainValidator<TRequest>> _validators;

    /// <summary>Initializes the chain with the DI-resolved validators in registration order.</summary>
    /// <param name="validators">The ordered set of validators for <typeparamref name="TRequest"/>.</param>
    public ValidationChain(IEnumerable<IChainValidator<TRequest>> validators)
    {
        ArgumentNullException.ThrowIfNull(validators);
        _validators = validators.ToList();
    }

    /// <summary>
    /// Runs each registered validator in order, returning the first failure or success
    /// when all validators pass.
    /// </summary>
    /// <param name="request">The request to validate.</param>
    /// <param name="ct">A token to observe for cancellation.</param>
    /// <returns>The first failing <see cref="ChainValidationResult"/>, or success.</returns>
    public async Task<ChainValidationResult> ValidateAsync(TRequest request, CancellationToken ct)
    {
        foreach (IChainValidator<TRequest> validator in _validators)
        {
            ChainValidationResult result = await validator.ValidateAsync(request, ct).ConfigureAwait(false);
            if (!result.IsValid)
            {
                return result;
            }
        }

        return ChainValidationResult.Success();
    }
}
