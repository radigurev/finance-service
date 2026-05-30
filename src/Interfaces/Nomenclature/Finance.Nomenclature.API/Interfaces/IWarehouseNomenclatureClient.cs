using Finance.ServiceModel.Nomenclature;
using Refit;

namespace Finance.Nomenclature.API.Interfaces;

/// <summary>
/// Refit contract for the Warehouse Nomenclature service consumed through the Warehouse Gateway
/// (SDD-NOM-001 §2.3). Finance does not own the country / state / city catalogue, so country, state, and
/// city lookups are proxied to Warehouse. The contract is defined here because the cross-cutting
/// Finance → Warehouse client spec (SDD-INT-WH-002) is not yet drafted.
/// <para>The client is registered with the shared correlation-id delegating handler, an inbound
/// bearer-token forwarding handler (S2S JWT is deferred), and <c>AddStandardResilienceHandler</c>
/// (SDD-INFRA-001).</para>
/// </summary>
public interface IWarehouseNomenclatureClient
{
    /// <summary>Returns the full list of countries from Warehouse Nomenclature.</summary>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>The countries reported by the upstream service.</returns>
    [Get("/countries")]
    Task<IReadOnlyList<CountryDto>> GetCountriesAsync(CancellationToken cancellationToken);

    /// <summary>Returns the states / provinces belonging to a country.</summary>
    /// <param name="country">The ISO 3166-1 alpha-2 country code.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>The states reported by the upstream service for the country.</returns>
    [Get("/states")]
    Task<IReadOnlyList<StateDto>> GetStatesAsync(
        [AliasAs("country")] string country,
        CancellationToken cancellationToken);

    /// <summary>Returns the cities belonging to a state / province.</summary>
    /// <param name="stateId">The Warehouse identifier of the owning state / province.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>The cities reported by the upstream service for the state.</returns>
    [Get("/cities")]
    Task<IReadOnlyList<CityDto>> GetCitiesAsync(
        [AliasAs("stateId")] int stateId,
        CancellationToken cancellationToken);
}
