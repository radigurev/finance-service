using Finance.Common.Results;
using Finance.ServiceModel.Nomenclature;

namespace Finance.Nomenclature.API.Interfaces;

/// <summary>
/// Application service that proxies country / state / city reference data from Warehouse Nomenclature
/// (SDD-NOM-001 §2.3). Results are cached for 30 minutes keyed by query (SDD-INFRA-004); on upstream
/// failure every method returns a <c>WAREHOUSE_NOMENCLATURE_UNREACHABLE</c> failure (mapped to 503).
/// Each method returns a <see cref="Result{T}"/>; failures are never signalled via exceptions.
/// </summary>
public interface IWarehouseProxyService
{
    /// <summary>Returns the full country list, served from cache when warm (SDD-NOM-001 §2.3).</summary>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>A success result carrying the countries, or an unreachable failure.</returns>
    Task<Result<IReadOnlyList<CountryDto>>> GetCountriesAsync(CancellationToken cancellationToken);

    /// <summary>Returns the states / provinces for a country, served from cache when warm.</summary>
    /// <param name="countryIso">The ISO 3166-1 alpha-2 country code.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>A success result carrying the states, or an unreachable failure.</returns>
    Task<Result<IReadOnlyList<StateDto>>> GetStatesAsync(string countryIso, CancellationToken cancellationToken);

    /// <summary>Returns the cities for a state / province, served from cache when warm.</summary>
    /// <param name="stateId">The Warehouse identifier of the owning state / province.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>A success result carrying the cities, or an unreachable failure.</returns>
    Task<Result<IReadOnlyList<CityDto>>> GetCitiesAsync(int stateId, CancellationToken cancellationToken);
}
