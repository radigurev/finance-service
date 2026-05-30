namespace Finance.Infrastructure.Caching;

/// <summary>
/// The v1 default time-to-live values and the absolute TTL bounds enforced by the cache layer
/// (SDD-INFRA-004 §2.2, §3). Callers may pass an explicit TTL; when omitted, reference-data
/// defaults apply.
/// </summary>
public static class CacheTtl
{
    /// <summary>The minimum allowed TTL. A shorter TTL is rejected (SDD-INFRA-004 §3).</summary>
    public static readonly TimeSpan MinimumTtl = TimeSpan.FromSeconds(1);

    /// <summary>The maximum allowed TTL. A longer TTL is rejected (SDD-INFRA-004 §3).</summary>
    public static readonly TimeSpan MaximumTtl = TimeSpan.FromHours(24);

    /// <summary>Default TTL applied to reference data (chart of accounts, currencies, periods, posting rules).</summary>
    public static readonly TimeSpan ReferenceData = TimeSpan.FromMinutes(30);

    /// <summary>Default TTL applied to per-user permission lookups.</summary>
    public static readonly TimeSpan Permissions = TimeSpan.FromMinutes(5);

    /// <summary>Default TTL applied to the latest exchange rate per currency.</summary>
    public static readonly TimeSpan LatestRates = TimeSpan.FromMinutes(5);

    /// <summary>Default TTL applied to cross-service reads fetched via Refit clients.</summary>
    public static readonly TimeSpan CrossServiceReads = TimeSpan.FromSeconds(60);

    /// <summary>
    /// The TTL applied when a caller does not pass an explicit value. Reference-data default per
    /// SDD-INFRA-004 §2.2.
    /// </summary>
    public static readonly TimeSpan Default = ReferenceData;
}
