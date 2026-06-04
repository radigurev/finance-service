using System.Globalization;

namespace Finance.Journal.API.Caching;

/// <summary>
/// Cache-key conventions for the posting-rule reference reads (SDD-FIN-006 §2.7, SDD-INFRA-004). Only the
/// bounded single-rule (by id / by key) keys are cached — the filtered/paged list is never cached on
/// arbitrary filter combinations. All keys are bounded by the <see cref="ServicePrefix"/> so pattern-based
/// invalidation never runs an unbounded scan.
/// </summary>
public static class PostingRuleCacheKeys
{
    /// <summary>The registered kebab-case service prefix for every Journal cache key.</summary>
    public const string ServicePrefix = "finance-journal";

    /// <summary>The bounded pattern matching every posting-rule cache key, used on write invalidation.</summary>
    public const string InvalidationPattern = ServicePrefix + ":posting-rule:*";

    /// <summary>Builds the single-rule-by-id cache key.</summary>
    /// <param name="id">The surrogate rule identifier.</param>
    /// <returns>The cache key <c>finance-journal:posting-rule:id:{id}</c>.</returns>
    public static string ById(int id) =>
        string.Create(CultureInfo.InvariantCulture, $"{ServicePrefix}:posting-rule:id:{id}");

    /// <summary>Builds the single-rule-by-key cache key used by the apply path resolution.</summary>
    /// <param name="ruleKey">The stable rule key.</param>
    /// <returns>The cache key <c>finance-journal:posting-rule:key:{ruleKey}</c>.</returns>
    public static string ByKey(string ruleKey) =>
        string.Create(CultureInfo.InvariantCulture, $"{ServicePrefix}:posting-rule:key:{ruleKey}");
}
