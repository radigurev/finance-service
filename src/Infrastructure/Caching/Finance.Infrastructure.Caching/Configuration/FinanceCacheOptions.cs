namespace Finance.Infrastructure.Caching.Configuration;

/// <summary>
/// Configuration for the Finance Redis cache layer (SDD-INFRA-004). Holds the Redis connection
/// string and the set of registered <c>{service}:</c> prefixes that cache keys are validated against.
/// </summary>
public sealed class FinanceCacheOptions
{
    /// <summary>The configuration section name bound from <c>ConnectionStrings</c> / app settings.</summary>
    public const string SectionName = "FinanceCache";

    /// <summary>
    /// The StackExchange.Redis connection string. Resolved from <c>ConnectionStrings:Redis</c> and
    /// validated as present at startup (SDD-INFRA-004 §3).
    /// </summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// The kebab-case <c>{service}:</c> prefixes that keys and scan patterns are validated against
    /// (SDD-INFRA-004 §2.1, §3). A key not starting with one of these is rejected.
    /// </summary>
    public IReadOnlyCollection<string> RegisteredServicePrefixes { get; set; } =
    [
        "finance-accounts",
        "finance-currency",
        "finance-periods",
        "finance-nomenclature",
    ];
}
