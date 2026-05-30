using Finance.Infrastructure.Caching.Configuration;
using Finance.Infrastructure.Caching.Interfaces;
using Microsoft.Extensions.Options;

namespace Finance.Infrastructure.Caching;

/// <summary>
/// Default <see cref="ICacheKeyValidator"/> that checks keys and scan patterns against the
/// registered <c>{service}:</c> prefixes from <see cref="FinanceCacheOptions"/> (SDD-INFRA-004 §3).
/// </summary>
public sealed class CacheKeyValidator : ICacheKeyValidator
{
    private readonly IReadOnlyCollection<string> _registeredPrefixes;

    /// <summary>
    /// Initializes a new instance of the <see cref="CacheKeyValidator"/> class.
    /// </summary>
    /// <param name="options">The cache options carrying the registered service prefixes.</param>
    public CacheKeyValidator(IOptions<FinanceCacheOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _registeredPrefixes = options.Value.RegisteredServicePrefixes;
    }

    /// <inheritdoc />
    public void ValidateKey(string key)
    {
        EnsureRegisteredPrefix(key, nameof(key));
    }

    /// <inheritdoc />
    public void ValidatePattern(string pattern)
    {
        EnsureRegisteredPrefix(pattern, nameof(pattern));
    }

    /// <summary>
    /// Throws <see cref="CacheKeyPatternViolationException"/> when <paramref name="value"/> is empty
    /// or does not begin with one of the registered <c>{service}:</c> prefixes.
    /// </summary>
    /// <param name="value">The key or pattern under validation.</param>
    /// <param name="argumentName">The originating argument name, used in the error message.</param>
    private void EnsureRegisteredPrefix(string value, string argumentName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new CacheKeyPatternViolationException(
                $"Cache {argumentName} must be a non-empty value prefixed by a registered service segment.");
        }

        foreach (string prefix in _registeredPrefixes)
        {
            if (value.StartsWith(prefix + ":", StringComparison.Ordinal))
            {
                return;
            }
        }

        throw new CacheKeyPatternViolationException(
            $"Cache {argumentName} '{value}' is not prefixed by any registered service segment "
            + $"({string.Join(", ", _registeredPrefixes)}).");
    }
}
