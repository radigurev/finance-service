using System.Globalization;

namespace Finance.Periods.API.Caching;

/// <summary>
/// Cache-key conventions for the Periods service reference reads (SDD-INFRA-004 §2.1, SDD-FIN-004 §2.8).
/// Only reference status data is cached: the date→period lookup. All keys are bounded by the
/// <see cref="ServicePrefix"/> so pattern-based invalidation never runs an unbounded scan.
/// </summary>
public static class PeriodCacheKeys
{
    /// <summary>The registered kebab-case service prefix for every Periods cache key.</summary>
    public const string ServicePrefix = "finance-periods";

    /// <summary>The bounded pattern matching every Periods cache key, used on write invalidation.</summary>
    public const string InvalidationPattern = ServicePrefix + ":*";

    /// <summary>Builds the date→period cache key for the supplied date (day-granular).</summary>
    /// <param name="date">The lookup date.</param>
    /// <returns>The cache key <c>finance-periods:by-date:{yyyy-MM-dd}</c>.</returns>
    public static string ByDate(DateTimeOffset date) =>
        string.Create(CultureInfo.InvariantCulture, $"{ServicePrefix}:by-date:{date:yyyy-MM-dd}");
}
