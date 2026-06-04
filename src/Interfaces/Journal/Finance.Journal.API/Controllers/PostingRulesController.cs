using Asp.Versioning;
using Finance.Common.Results;
using Finance.GenericFiltering.Models;
using Finance.Infrastructure.Web.Controllers;
using Finance.Infrastructure.Web.ErrorMapping;
using Finance.Journal.API.Interfaces;
using Finance.ServiceModel.Posting;
using Microsoft.AspNetCore.Mvc;
using Warehouse.Auth.AspNetCore.Authorization;

namespace Finance.Journal.API.Controllers;

/// <summary>
/// REST endpoints for managing posting rules — editable reference data (SDD-FIN-006 §2.1). Inherits
/// <see cref="BaseApiController"/> so every action translates a service <see cref="Result"/> /
/// <see cref="Result{T}"/> into an RFC 7807 ProblemDetails-aware <see cref="ActionResult"/>.
/// </summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/posting-rules")]
[Produces("application/json")]
public sealed class PostingRulesController : BaseApiController
{
    private const string DefaultCountryCode = "BG";

    private readonly IPostingRuleService _postingRules;
    private readonly IConfiguration _configuration;

    /// <summary>Creates a new <see cref="PostingRulesController"/>.</summary>
    /// <param name="postingRules">The posting-rule application service.</param>
    /// <param name="configuration">Configuration carrying the owning <c>Country:Code</c>.</param>
    /// <param name="statusMap">The error-code → HTTP-status map used by the base controller.</param>
    public PostingRulesController(
        IPostingRuleService postingRules,
        IConfiguration configuration,
        IErrorCodeToStatusMap statusMap)
        : base(statusMap)
    {
        _postingRules = postingRules;
        _configuration = configuration;
    }

    /// <summary>Lists posting rules as a filtered, sorted, and paged envelope.</summary>
    /// <param name="request">The filter, sort, and pagination request from the query string.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>A <see cref="PagedResult{T}"/> of <see cref="PostingRuleDto"/>.</returns>
    [HttpGet]
    [RequirePermission("finance.posting-rule:read")]
    [ProducesResponseType(typeof(PagedResult<PostingRuleDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PagedResult<PostingRuleDto>>> List(
        [FromQuery] FilterRequest request,
        CancellationToken cancellationToken)
    {
        Result<PagedResult<PostingRuleDto>> result =
            await _postingRules.SearchAsync(request, cancellationToken).ConfigureAwait(false);
        return ToActionResult(result);
    }

    /// <summary>Returns a single posting rule by id with its ordered lines.</summary>
    /// <param name="id">The surrogate rule identifier.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>The matching <see cref="PostingRuleDto"/>, or a 404 ProblemDetails.</returns>
    [HttpGet("{id:int}")]
    [RequirePermission("finance.posting-rule:read")]
    [ProducesResponseType(typeof(PostingRuleDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PostingRuleDto>> Get(int id, CancellationToken cancellationToken)
    {
        Result<PostingRuleDto> result = await _postingRules.GetAsync(id, cancellationToken).ConfigureAwait(false);
        return ToActionResult(result);
    }

    /// <summary>Creates a new posting rule for the configured country.</summary>
    /// <param name="request">The create request body.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>The created <see cref="PostingRuleDto"/>, or a validation/conflict ProblemDetails.</returns>
    [HttpPost]
    [RequirePermission("finance.posting-rule:write")]
    [ProducesResponseType(typeof(PostingRuleDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<PostingRuleDto>> Create(
        CreatePostingRuleRequest request,
        CancellationToken cancellationToken)
    {
        string countryCode = _configuration["Country:Code"] ?? DefaultCountryCode;
        Result<PostingRuleDto> result =
            await _postingRules.CreateAsync(request, countryCode, cancellationToken).ConfigureAwait(false);

        if (!result.IsSuccess)
        {
            return ToActionResult(result);
        }

        return CreatedAtAction(nameof(Get), new { id = result.Value!.Id, version = "1" }, result.Value);
    }

    /// <summary>Updates a posting rule (description, active flag, lines) under optimistic concurrency.</summary>
    /// <param name="id">The surrogate rule identifier.</param>
    /// <param name="request">The update request body.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>The updated <see cref="PostingRuleDto"/>, or a 400 / 404 / 409 ProblemDetails.</returns>
    [HttpPut("{id:int}")]
    [RequirePermission("finance.posting-rule:write")]
    [ProducesResponseType(typeof(PostingRuleDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<PostingRuleDto>> Update(
        int id,
        UpdatePostingRuleRequest request,
        CancellationToken cancellationToken)
    {
        Result<PostingRuleDto> result =
            await _postingRules.UpdateAsync(id, request, cancellationToken).ConfigureAwait(false);
        return ToActionResult(result);
    }
}
