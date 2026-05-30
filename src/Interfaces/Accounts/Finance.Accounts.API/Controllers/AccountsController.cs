using Asp.Versioning;
using Finance.Accounts.API.Interfaces;
using Finance.Common.Results;
using Finance.GenericFiltering.Models;
using Finance.Infrastructure.Web.Controllers;
using Finance.Infrastructure.Web.ErrorMapping;
using Finance.ServiceModel.Accounts;
using Microsoft.AspNetCore.Mvc;
using Warehouse.Auth.AspNetCore.Authorization;

namespace Finance.Accounts.API.Controllers;

/// <summary>
/// REST endpoints for managing the chart of accounts (SDD-ACCT-001). Inherits
/// <see cref="BaseApiController"/> so every action translates a service <see cref="Result"/>
/// / <see cref="Result{T}"/> into an RFC 7807 ProblemDetails-aware <see cref="ActionResult"/>.
/// </summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/accounts")]
[Produces("application/json")]
public sealed class AccountsController : BaseApiController
{
    private const string DefaultCountryCode = "BG";

    private readonly IAccountService _accounts;
    private readonly IConfiguration _configuration;

    /// <summary>Creates a new <see cref="AccountsController"/>.</summary>
    /// <param name="accounts">The chart-of-accounts application service.</param>
    /// <param name="configuration">Configuration carrying the owning <c>Country:Code</c>.</param>
    /// <param name="statusMap">The error-code → HTTP-status map used by the base controller.</param>
    public AccountsController(
        IAccountService accounts,
        IConfiguration configuration,
        IErrorCodeToStatusMap statusMap)
        : base(statusMap)
    {
        _accounts = accounts;
        _configuration = configuration;
    }

    /// <summary>Lists accounts in the chart as a filtered, sorted, and paged envelope.</summary>
    /// <param name="request">The filter, sort, and pagination request from the query string.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>A <see cref="PagedResult{T}"/> of <see cref="AccountDto"/>.</returns>
    [HttpGet]
    [RequirePermission("finance.account:read")]
    [ProducesResponseType(typeof(PagedResult<AccountDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PagedResult<AccountDto>>> List(
        [FromQuery] FilterRequest request,
        CancellationToken cancellationToken)
    {
        Result<PagedResult<AccountDto>> result =
            await _accounts.SearchAsync(request, cancellationToken).ConfigureAwait(false);
        return ToActionResult(result);
    }

    /// <summary>Returns a single account by ID.</summary>
    /// <param name="id">The surrogate account identifier.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>The matching <see cref="AccountDto"/>, or a 404 ProblemDetails.</returns>
    [HttpGet("{id:int}")]
    [RequirePermission("finance.account:read")]
    [ProducesResponseType(typeof(AccountDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AccountDto>> Get(int id, CancellationToken cancellationToken)
    {
        Result<AccountDto> result = await _accounts.GetAsync(id, cancellationToken).ConfigureAwait(false);
        return ToActionResult(result);
    }

    /// <summary>Creates a new account in the chart for the configured country.</summary>
    /// <param name="request">The create request body.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>The created <see cref="AccountDto"/>, or a validation/conflict ProblemDetails.</returns>
    [HttpPost]
    [RequirePermission("finance.account:write")]
    [ProducesResponseType(typeof(AccountDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AccountDto>> Create(
        CreateAccountRequest request,
        CancellationToken cancellationToken)
    {
        string countryCode = _configuration["Country:Code"] ?? DefaultCountryCode;
        Result<AccountDto> result =
            await _accounts.CreateAsync(request, countryCode, cancellationToken).ConfigureAwait(false);

        if (!result.IsSuccess)
        {
            return ToActionResult(result);
        }

        return CreatedAtAction(nameof(Get), new { id = result.Value!.Id, version = "1" }, result.Value);
    }

    /// <summary>Updates the mutable fields on an existing account.</summary>
    /// <param name="id">The surrogate account identifier.</param>
    /// <param name="request">The update request body.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>The updated <see cref="AccountDto"/>, or a 404 / 409 ProblemDetails.</returns>
    [HttpPut("{id:int}")]
    [RequirePermission("finance.account:write")]
    [ProducesResponseType(typeof(AccountDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AccountDto>> Update(
        int id,
        UpdateAccountRequest request,
        CancellationToken cancellationToken)
    {
        Result<AccountDto> result =
            await _accounts.UpdateAsync(id, request, cancellationToken).ConfigureAwait(false);
        return ToActionResult(result);
    }
}
