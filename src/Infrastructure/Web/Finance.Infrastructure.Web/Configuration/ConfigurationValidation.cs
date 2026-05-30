using Microsoft.Extensions.Configuration;

namespace Finance.Infrastructure.Web.Configuration;

/// <summary>
/// Startup configuration-validation helpers. Used by services to fail fast with a clear message when a
/// required configuration key is missing or empty (SDD-INFRA-001 §3) or when the shared JWT configuration
/// is incomplete (SDD-INT-AUTH-001). Each service calls these next to <c>AddWarehouseAuthentication</c>.
/// </summary>
public static class ConfigurationValidation
{
    private const string JwtSecretKey = "Jwt:SecretKey";
    private const string JwtIssuerKey = "Jwt:Issuer";
    private const string JwtAudienceKey = "Jwt:Audience";
    private const int MinimumSecretKeyLength = 32;

    /// <summary>
    /// Validates the shared Finance JWT configuration and throws a clear startup error when
    /// <c>Jwt:SecretKey</c> is missing or shorter than 32 characters, or <c>Jwt:Issuer</c> / <c>Jwt:Audience</c>
    /// is empty (SDD-INT-AUTH-001). Called by each service alongside <c>AddWarehouseAuthentication</c>; the
    /// gateway never validates JWT because it does not decode tokens (SDD-INFRA-002 §2.5).
    /// </summary>
    /// <param name="configuration">The application configuration.</param>
    /// <exception cref="InvalidOperationException">When the JWT configuration is missing or invalid.</exception>
    public static void ValidateFinanceJwtConfiguration(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        string? secretKey = configuration[JwtSecretKey];
        if (string.IsNullOrWhiteSpace(secretKey))
        {
            throw new InvalidOperationException(
                $"JWT configuration '{JwtSecretKey}' is missing or empty. Provide a signing key of at least " +
                $"{MinimumSecretKeyLength} characters before starting the service.");
        }

        if (secretKey.Length < MinimumSecretKeyLength)
        {
            throw new InvalidOperationException(
                $"JWT configuration '{JwtSecretKey}' must be at least {MinimumSecretKeyLength} characters but was " +
                $"{secretKey.Length}. Use a longer signing key before starting the service.");
        }

        EnsureRequiredConfiguration(configuration, JwtIssuerKey, JwtAudienceKey);
    }

    /// <summary>
    /// Verifies every key in <paramref name="requiredKeys"/> resolves to a non-empty value, throwing an
    /// <see cref="InvalidOperationException"/> naming the first missing key otherwise.
    /// </summary>
    /// <param name="configuration">The application configuration.</param>
    /// <param name="requiredKeys">The configuration keys that MUST be present and non-empty.</param>
    /// <exception cref="InvalidOperationException">When a required key is missing or empty.</exception>
    public static void EnsureRequiredConfiguration(IConfiguration configuration, params string[] requiredKeys)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(requiredKeys);

        foreach (string key in requiredKeys)
        {
            string? value = configuration[key];
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException(
                    $"Required configuration key '{key}' is missing or empty. Set it before starting the service.");
            }
        }
    }
}
