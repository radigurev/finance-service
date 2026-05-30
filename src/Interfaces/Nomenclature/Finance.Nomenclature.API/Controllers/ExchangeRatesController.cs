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
/// Read-only REST endpoints for currency exchange rates (SDD-NOM-001 §2.2). These reads are
/// transactional and never cached: every request hits the database. A <c>date</c> query returns the
/// latest rate on or before the date; a <c>from</c>/<c>to</c> query returns the range ordered by date.
/// </summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/exchange-rates")]
[Produces("application/json")]
public sealed class ExchangeRatesController : BaseApiController
{
    private readonly IExchangeRateService _exchangeRates;

    /// <summary>Creates a new <see cref="ExchangeRatesController"/>.</summary>
    /// <param name="exchangeRates">The read-only exchange-rate application service.</param>
    /// <param name="statusMap">The error-code → HTTP-status map used by the base controller.</param>
    public ExchangeRatesController(IExchangeRateService exchangeRates, IErrorCodeToStatusMap statusMap)
        : base(statusMap)
    {
        _exchangeRates = exchangeRates;
    }

    /// <summary>Returns the latest exchange rate on or before <paramref name="date"/> for a currency.</summary>
    /// <param name="currency">The ISO 4217 alphabetic code of the currency.</param>
    /// <param name="date">The inclusive upper-bound date.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>The latest <see cref="ExchangeRateDto"/>, or a 404 ProblemDetails.</returns>
    [HttpGet("latest")]
    [RequirePermission("finance.nomenclature:read")]
    [ProducesResponseType(typeof(ExchangeRateDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ExchangeRateDto>> GetLatest(
        [FromQuery] string currency,
        [FromQuery] DateTimeOffset date,
        CancellationToken cancellationToken)
    {
        Result<ExchangeRateDto> result =
            await _exchangeRates.GetLatestRateAsync(currency, date, cancellationToken).ConfigureAwait(false);
        return ToActionResult(result);
    }

    /// <summary>Returns the exchange-rate range for a currency, ordered ascending by date.</summary>
    /// <param name="currency">The ISO 4217 alphabetic code of the currency.</param>
    /// <param name="from">The inclusive lower-bound date.</param>
    /// <param name="to">The inclusive upper-bound date.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>The ordered <see cref="ExchangeRateDto"/> range, or a 400 / 404 ProblemDetails.</returns>
    [HttpGet("range")]
    [RequirePermission("finance.nomenclature:read")]
    [ProducesResponseType(typeof(IReadOnlyList<ExchangeRateDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<ExchangeRateDto>>> GetRange(
        [FromQuery] string currency,
        [FromQuery] DateTimeOffset from,
        [FromQuery] DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        Result<IReadOnlyList<ExchangeRateDto>> result =
            await _exchangeRates.GetRateRangeAsync(currency, from, to, cancellationToken).ConfigureAwait(false);
        return ToActionResult(result);
    }
}
