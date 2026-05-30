using Asp.Versioning;
using Finance.Common.Results;
using Finance.Infrastructure.Web.Controllers;
using Finance.Infrastructure.Web.ErrorMapping;
using Finance.Nomenclature.API.Interfaces;
using Finance.ServiceModel.Nomenclature;
using Microsoft.AspNetCore.Mvc;
using Warehouse.Auth.AspNetCore.Authorization;

namespace Finance.Nomenclature.API.Controllers;

/// <summary>
/// REST endpoints that proxy country / state / city reference data from Warehouse Nomenclature
/// (SDD-NOM-001 §2.3). Finance does not own the country catalogue; these reads are forwarded to
/// Warehouse via the resilient Refit client, cached for 30 minutes per query, and return
/// <c>503 WAREHOUSE_NOMENCLATURE_UNREACHABLE</c> when the upstream service is down.
/// </summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}")]
[Produces("application/json")]
public sealed class NomenclatureProxyController : BaseApiController
{
    private readonly IWarehouseProxyService _proxy;

    /// <summary>Creates a new <see cref="NomenclatureProxyController"/>.</summary>
    /// <param name="proxy">The Warehouse country/state/city proxy service.</param>
    /// <param name="statusMap">The error-code → HTTP-status map used by the base controller.</param>
    public NomenclatureProxyController(IWarehouseProxyService proxy, IErrorCodeToStatusMap statusMap)
        : base(statusMap)
    {
        _proxy = proxy;
    }

    /// <summary>Returns the country list proxied from Warehouse Nomenclature.</summary>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>The countries, or a 503 ProblemDetails when upstream is unreachable.</returns>
    [HttpGet("countries")]
    [RequirePermission("finance.nomenclature:read")]
    [ProducesResponseType(typeof(IReadOnlyList<CountryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<IReadOnlyList<CountryDto>>> GetCountries(CancellationToken cancellationToken)
    {
        Result<IReadOnlyList<CountryDto>> result =
            await _proxy.GetCountriesAsync(cancellationToken).ConfigureAwait(false);
        return ToActionResult(result);
    }

    /// <summary>Returns the states / provinces of a country, proxied from Warehouse Nomenclature.</summary>
    /// <param name="country">The ISO 3166-1 alpha-2 country code.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>The states, or a 503 ProblemDetails when upstream is unreachable.</returns>
    [HttpGet("states")]
    [RequirePermission("finance.nomenclature:read")]
    [ProducesResponseType(typeof(IReadOnlyList<StateDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<IReadOnlyList<StateDto>>> GetStates(
        [FromQuery] string country,
        CancellationToken cancellationToken)
    {
        Result<IReadOnlyList<StateDto>> result =
            await _proxy.GetStatesAsync(country, cancellationToken).ConfigureAwait(false);
        return ToActionResult(result);
    }

    /// <summary>Returns the cities of a state / province, proxied from Warehouse Nomenclature.</summary>
    /// <param name="stateId">The Warehouse identifier of the owning state / province.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>The cities, or a 503 ProblemDetails when upstream is unreachable.</returns>
    [HttpGet("cities")]
    [RequirePermission("finance.nomenclature:read")]
    [ProducesResponseType(typeof(IReadOnlyList<CityDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<IReadOnlyList<CityDto>>> GetCities(
        [FromQuery] int stateId,
        CancellationToken cancellationToken)
    {
        Result<IReadOnlyList<CityDto>> result =
            await _proxy.GetCitiesAsync(stateId, cancellationToken).ConfigureAwait(false);
        return ToActionResult(result);
    }
}
