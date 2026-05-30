namespace Finance.Nomenclature.API.Caching;

/// <summary>
/// Cache-key conventions for the Nomenclature service reference reads (SDD-INFRA-004 §2.1,
/// SDD-NOM-001 §2.1). Only reference data is cached: the full active-currency list used to populate
/// dropdowns. Exchange-rate reads are transactional and are never cached. All keys are bounded by the
/// <see cref="ServicePrefix"/> so pattern-based invalidation never runs an unbounded scan.
/// </summary>
public static class CurrencyCacheKeys
{
    /// <summary>The registered kebab-case service prefix for every Nomenclature cache key.</summary>
    public const string ServicePrefix = "finance-nomenclature";

    /// <summary>The full active-currency list key used to populate dropdowns.</summary>
    public const string ActiveCurrencies = ServicePrefix + ":currencies:all";

    /// <summary>The bounded pattern matching every Nomenclature cache key, used on write invalidation.</summary>
    public const string InvalidationPattern = ServicePrefix + ":*";
}
