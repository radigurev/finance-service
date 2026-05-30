using System.Globalization;

namespace Finance.Nomenclature.API.Caching;

/// <summary>
/// Cache-key conventions for the Warehouse country / state / city proxy reads (SDD-INFRA-004 §2.1,
/// SDD-NOM-001 §2.3). Proxy results are reference data cached for 30 minutes keyed by the query. All
/// keys are bounded by the shared <see cref="CurrencyCacheKeys.ServicePrefix"/> so the write-path
/// pattern invalidation (<see cref="CurrencyCacheKeys.InvalidationPattern"/>) also clears proxy entries.
/// </summary>
public static class WarehouseProxyCacheKeys
{
    /// <summary>The full country list key.</summary>
    public const string Countries = CurrencyCacheKeys.ServicePrefix + ":countries:all";

    /// <summary>Builds the per-country states cache key keyed by the ISO 3166-1 alpha-2 code.</summary>
    /// <param name="countryIso">The owning country's ISO 3166-1 alpha-2 code.</param>
    /// <returns>The bounded states cache key.</returns>
    public static string StatesForCountry(string countryIso) =>
        $"{CurrencyCacheKeys.ServicePrefix}:states:{countryIso}";

    /// <summary>Builds the per-state cities cache key keyed by the Warehouse state identifier.</summary>
    /// <param name="stateId">The owning state / province identifier.</param>
    /// <returns>The bounded cities cache key.</returns>
    public static string CitiesForState(int stateId) =>
        $"{CurrencyCacheKeys.ServicePrefix}:cities:{stateId.ToString(CultureInfo.InvariantCulture)}";
}
