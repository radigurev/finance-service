using Asp.Versioning;
using Finance.Accounts.API.Interfaces;
using Finance.ServiceModel.Accounts;
using Microsoft.AspNetCore.Mvc;
using Warehouse.Auth.AspNetCore.Authorization;

namespace Finance.Accounts.API.Controllers;

/// <summary>
/// REST endpoints for managing the chart of accounts.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/accounts")]
[Produces("application/json")]
public sealed class AccountsController : ControllerBase
{
    private readonly IAccountService _accounts;
    private readonly IConfiguration _configuration;

    /// <summary>Creates a new <see cref="AccountsController"/>.</summary>
    public AccountsController(IAccountService accounts, IConfiguration configuration)
    {
        _accounts = accounts;
        _configuration = configuration;
    }

    /// <summary>Lists all accounts in the chart.</summary>
    [HttpGet]
    [RequirePermission("finance.account:read")]
    [ProducesResponseType(typeof(IReadOnlyList<AccountDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<AccountDto>>> List(CancellationToken cancellationToken)
    {
        IReadOnlyList<AccountDto> accounts = await _accounts.ListAsync(cancellationToken).ConfigureAwait(false);
        return Ok(accounts);
    }

    /// <summary>Returns a single account by ID.</summary>
    [HttpGet("{id:int}")]
    [RequirePermission("finance.account:read")]
    [ProducesResponseType(typeof(AccountDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AccountDto>> Get(int id, CancellationToken cancellationToken)
    {
        AccountDto? account = await _accounts.GetAsync(id, cancellationToken).ConfigureAwait(false);
        return account is null ? NotFound() : Ok(account);
    }

    /// <summary>Creates a new account in the chart.</summary>
    [HttpPost]
    [RequirePermission("finance.account:write")]
    [ProducesResponseType(typeof(AccountDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<AccountDto>> Create(
        CreateAccountRequest request,
        CancellationToken cancellationToken)
    {
        string countryCode = _configuration["Country:Code"] ?? "BG";
        AccountDto created = await _accounts.CreateAsync(request, countryCode, cancellationToken).ConfigureAwait(false);
        return CreatedAtAction(nameof(Get), new { id = created.Id, version = "1" }, created);
    }

    /// <summary>Updates the mutable fields on an existing account.</summary>
    [HttpPut("{id:int}")]
    [RequirePermission("finance.account:write")]
    [ProducesResponseType(typeof(AccountDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AccountDto>> Update(
        int id,
        UpdateAccountRequest request,
        CancellationToken cancellationToken)
    {
        AccountDto? updated = await _accounts.UpdateAsync(id, request, cancellationToken).ConfigureAwait(false);
        return updated is null ? NotFound() : Ok(updated);
    }
}
