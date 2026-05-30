using System.Globalization;

namespace Finance.Accounts.API.Caching;

/// <summary>
/// Cache-key conventions for the Accounts service reference reads (SDD-INFRA-004 §2.1, SDD-ACCT-001 §2.7).
/// Only reference data is cached: get-by-id and the full active-chart list. All keys are bounded by the
/// <see cref="ServicePrefix"/> so pattern-based invalidation never runs an unbounded scan.
/// </summary>
public static class AccountCacheKeys
{
    /// <summary>The registered kebab-case service prefix for every Accounts cache key.</summary>
    public const string ServicePrefix = "finance-accounts";

    /// <summary>The full active-chart list key used to populate dropdowns.</summary>
    public const string ActiveChart = ServicePrefix + ":chart:all";

    /// <summary>The bounded pattern matching every Accounts cache key, used on write invalidation.</summary>
    public const string InvalidationPattern = ServicePrefix + ":*";

    /// <summary>Builds the single-account-by-id cache key.</summary>
    /// <param name="id">The surrogate account identifier.</param>
    /// <returns>The cache key <c>finance-accounts:account:{id}</c>.</returns>
    public static string Account(int id) =>
        string.Create(CultureInfo.InvariantCulture, $"{ServicePrefix}:account:{id}");
}
