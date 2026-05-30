using Microsoft.Extensions.Configuration;

namespace Finance.Infrastructure.Web.Configuration;

/// <summary>
/// Startup configuration-validation helpers. Used by services to fail fast with a clear message when a
/// required configuration key is missing or empty (SDD-INFRA-001 §3). Jwt-specific validation is
/// deferred to Batch 7 (SDD-INT-AUTH-001).
/// </summary>
public static class ConfigurationValidation
{
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
