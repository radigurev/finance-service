using Microsoft.Extensions.Configuration;

namespace Finance.Accounts.API.Tests.Fixtures;

/// <summary>
/// Builds an in-memory <see cref="IConfiguration"/> carrying the owning <c>Country:Code</c> used by the
/// chain validators (SDD-ACCT-001 §2.6).
/// </summary>
public static class TestConfiguration
{
    /// <summary>Creates a configuration with the supplied owning country code.</summary>
    /// <param name="countryCode">The ISO 3166-1 alpha-2 country code to expose under <c>Country:Code</c>.</param>
    /// <returns>An in-memory configuration root.</returns>
    public static IConfiguration WithCountry(string countryCode)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Country:Code"] = countryCode })
            .Build();
    }
}
