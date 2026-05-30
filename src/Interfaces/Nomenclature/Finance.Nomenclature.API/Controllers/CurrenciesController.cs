using Asp.Versioning;
using Finance.Common.Results;
using Finance.GenericFiltering.Models;
using Finance.Infrastructure.Web.Controllers;
using Finance.Infrastructure.Web.ErrorMapping;
using Finance.Nomenclature.API.Interfaces;
using Finance.ServiceModel.Nomenclature;
using Microsoft.AspNetCore.Mvc;
using Warehouse.Auth.AspNetCore.Authorization;

namespace Finance.Nomenclature.API.Controllers;

/// <summary>
/// REST endpoints for managing ISO 4217 currencies (SDD-NOM-001 §2.1). Inherits
/// <see cref="BaseApiController"/> so every action translates a service <see cref="Result"/> /
/// <see cref="Result{T}"/> into an RFC 7807 ProblemDetails-aware <see cref="ActionResult"/>. There is no
/// DELETE; deactivation is performed via <c>IsActive = false</c> (soft delete).
/// </summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/currencies")]
[Produces("application/json")]
public sealed class CurrenciesController : BaseApiController
{
    private readonly ICurrencyService _currencies;

    /// <summary>Creates a new <see cref="CurrenciesController"/>.</summary>
    /// <param name="currencies">The currency application service.</param>
    /// <param name="statusMap">The error-code → HTTP-status map used by the base controller.</param>
    public CurrenciesController(ICurrencyService currencies, IErrorCodeToStatusMap statusMap)
        : base(statusMap)
    {
        _currencies = currencies;
    }

    /// <summary>Lists currencies as a filtered, sorted, and paged envelope (active and inactive).</summary>
    /// <param name="request">The filter, sort, and pagination request from the query string.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>A <see cref="PagedResult{T}"/> of <see cref="CurrencyDto"/>.</returns>
    [HttpGet]
    [RequirePermission("finance.nomenclature:read")]
    [ProducesResponseType(typeof(PagedResult<CurrencyDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PagedResult<CurrencyDto>>> List(
        [FromQuery] FilterRequest request,
        CancellationToken cancellationToken)
    {
        Result<PagedResult<CurrencyDto>> result =
            await _currencies.SearchAsync(request, cancellationToken).ConfigureAwait(false);
        return ToActionResult(result);
    }

    /// <summary>Returns a single currency by its ISO code.</summary>
    /// <param name="isoCode">The ISO 4217 alphabetic code.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>The matching <see cref="CurrencyDto"/>, or a 404 ProblemDetails.</returns>
    [HttpGet("{isoCode}")]
    [RequirePermission("finance.nomenclature:read")]
    [ProducesResponseType(typeof(CurrencyDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CurrencyDto>> Get(string isoCode, CancellationToken cancellationToken)
    {
        Result<CurrencyDto> result =
            await _currencies.GetByIsoCodeAsync(isoCode, cancellationToken).ConfigureAwait(false);
        return ToActionResult(result);
    }

    /// <summary>Creates a new currency.</summary>
    /// <param name="request">The create request body.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>The created <see cref="CurrencyDto"/>, or a validation/conflict ProblemDetails.</returns>
    [HttpPost]
    [RequirePermission("finance.nomenclature:write")]
    [ProducesResponseType(typeof(CurrencyDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CurrencyDto>> Create(
        CreateCurrencyRequest request,
        CancellationToken cancellationToken)
    {
        Result<CurrencyDto> result =
            await _currencies.CreateAsync(request, cancellationToken).ConfigureAwait(false);

        if (!result.IsSuccess)
        {
            return ToActionResult(result);
        }

        return CreatedAtAction(
            nameof(Get),
            new { isoCode = result.Value!.IsoCode, version = "1" },
            result.Value);
    }

    /// <summary>Updates the mutable fields on an existing currency. The ISO code is immutable.</summary>
    /// <param name="isoCode">The immutable ISO 4217 alphabetic code identifying the currency.</param>
    /// <param name="request">The update request body.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>The updated <see cref="CurrencyDto"/>, or a 404 / 409 ProblemDetails.</returns>
    [HttpPut("{isoCode}")]
    [RequirePermission("finance.nomenclature:write")]
    [ProducesResponseType(typeof(CurrencyDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CurrencyDto>> Update(
        string isoCode,
        UpdateCurrencyRequest request,
        CancellationToken cancellationToken)
    {
        Result<CurrencyDto> result =
            await _currencies.UpdateAsync(isoCode, request, cancellationToken).ConfigureAwait(false);
        return ToActionResult(result);
    }
}
