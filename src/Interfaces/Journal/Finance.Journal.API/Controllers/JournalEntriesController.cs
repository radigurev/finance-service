using Asp.Versioning;
using Finance.Common.Results;
using Finance.GenericFiltering.Models;
using Finance.Infrastructure.Web.Controllers;
using Finance.Infrastructure.Web.ErrorMapping;
using Finance.Journal.API.Interfaces;
using Finance.ServiceModel.Journal;
using Microsoft.AspNetCore.Mvc;
using Warehouse.Auth.AspNetCore.Authorization;

namespace Finance.Journal.API.Controllers;

/// <summary>
/// REST endpoints for the journal-entry lifecycle (SDD-FIN-001, SDD-FIN-002). Inherits
/// <see cref="BaseApiController"/> so every action translates a service <see cref="Result"/> /
/// <see cref="Result{T}"/> into an RFC 7807 ProblemDetails-aware <see cref="ActionResult"/>. Posted
/// entries are immutable: there is no edit/delete path for them — correct via reversal.
/// </summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/journal-entries")]
[Produces("application/json")]
public sealed class JournalEntriesController : BaseApiController
{
    private const string DefaultBaseCurrency = "BGN";

    private readonly IJournalEntryService _entries;
    private readonly IConfiguration _configuration;

    /// <summary>Creates a new <see cref="JournalEntriesController"/>.</summary>
    /// <param name="entries">The journal-entry application service.</param>
    /// <param name="configuration">Configuration carrying <c>Country:BaseCurrency</c>.</param>
    /// <param name="statusMap">The error-code → HTTP-status map used by the base controller.</param>
    public JournalEntriesController(
        IJournalEntryService entries,
        IConfiguration configuration,
        IErrorCodeToStatusMap statusMap)
        : base(statusMap)
    {
        _entries = entries;
        _configuration = configuration;
    }

    /// <summary>Lists journal entries as a filtered, sorted, and paged envelope (SDD-FIN-002 §2.9).</summary>
    /// <param name="request">The filter, sort, and pagination request from the query string.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>A <see cref="PagedResult{T}"/> of <see cref="JournalEntryDto"/>.</returns>
    [HttpGet]
    [RequirePermission("finance.journal:read")]
    [ProducesResponseType(typeof(PagedResult<JournalEntryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PagedResult<JournalEntryDto>>> List(
        [FromQuery] FilterRequest request,
        CancellationToken cancellationToken)
    {
        Result<PagedResult<JournalEntryDto>> result =
            await _entries.SearchAsync(request, cancellationToken).ConfigureAwait(false);
        return ToActionResult(result);
    }

    /// <summary>Returns a single journal entry with its lines (SDD-FIN-002 §2.9).</summary>
    /// <param name="id">The entry identifier.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>The matching <see cref="JournalEntryDto"/>, or a 404 ProblemDetails.</returns>
    [HttpGet("{id:guid}")]
    [RequirePermission("finance.journal:read")]
    [ProducesResponseType(typeof(JournalEntryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<JournalEntryDto>> Get(Guid id, CancellationToken cancellationToken)
    {
        Result<JournalEntryDto> result = await _entries.GetAsync(id, cancellationToken).ConfigureAwait(false);
        return ToActionResult(result);
    }

    /// <summary>Creates a balanced draft journal entry (SDD-FIN-002 §2.3).</summary>
    /// <param name="request">The create-draft request body.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>The created <see cref="JournalEntryDto"/>, or a validation/conflict ProblemDetails.</returns>
    [HttpPost]
    [RequirePermission("finance.journal:create")]
    [ProducesResponseType(typeof(JournalEntryDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<JournalEntryDto>> Create(
        CreateJournalEntryRequest request,
        CancellationToken cancellationToken)
    {
        string baseCurrency = _configuration["Country:BaseCurrency"] ?? DefaultBaseCurrency;
        Result<JournalEntryDto> result =
            await _entries.CreateDraftAsync(request, baseCurrency, cancellationToken).ConfigureAwait(false);

        if (!result.IsSuccess)
        {
            return ToActionResult(result);
        }

        return CreatedAtAction(nameof(Get), new { id = result.Value!.Id, version = "1" }, result.Value);
    }

    /// <summary>Updates a draft journal entry (SDD-FIN-002 §2.5).</summary>
    /// <param name="id">The entry identifier.</param>
    /// <param name="request">The update request body.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>The updated <see cref="JournalEntryDto"/>, or a 404 / 409 ProblemDetails.</returns>
    [HttpPut("{id:guid}")]
    [RequirePermission("finance.journal:create")]
    [ProducesResponseType(typeof(JournalEntryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<JournalEntryDto>> Update(
        Guid id,
        UpdateJournalEntryRequest request,
        CancellationToken cancellationToken)
    {
        Result<JournalEntryDto> result =
            await _entries.UpdateDraftAsync(id, request, cancellationToken).ConfigureAwait(false);
        return ToActionResult(result);
    }

    /// <summary>Deletes a draft journal entry (SDD-FIN-002 §2.5).</summary>
    /// <param name="id">The entry identifier.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns><c>200 OK</c> on success, or a 404 / 409 ProblemDetails.</returns>
    [HttpDelete("{id:guid}")]
    [RequirePermission("finance.journal:delete")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        Result result = await _entries.DeleteDraftAsync(id, cancellationToken).ConfigureAwait(false);
        return ToActionResult(result);
    }

    /// <summary>Posts a draft journal entry (Draft → Posted) (SDD-FIN-002 §2.4).</summary>
    /// <param name="id">The entry identifier.</param>
    /// <param name="request">The post request carrying the row version.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>The posted <see cref="JournalEntryDto"/>, or a state / validation / concurrency ProblemDetails.</returns>
    [HttpPost("{id:guid}/post")]
    [RequirePermission("finance.journal:post")]
    [ProducesResponseType(typeof(JournalEntryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<JournalEntryDto>> Post(
        Guid id,
        PostJournalEntryRequest request,
        CancellationToken cancellationToken)
    {
        Result<JournalEntryDto> result = await _entries.PostAsync(id, request, cancellationToken).ConfigureAwait(false);
        return ToActionResult(result);
    }

    /// <summary>Reverses a posted journal entry (Posted → Reversed) (SDD-FIN-002 §2.6).</summary>
    /// <param name="id">The identifier of the posted entry to reverse.</param>
    /// <param name="request">The reverse request carrying the mandatory reason and row version.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>The new reversal <see cref="JournalEntryDto"/>, or a state / validation / concurrency ProblemDetails.</returns>
    [HttpPost("{id:guid}/reverse")]
    [RequirePermission("finance.journal:reverse")]
    [ProducesResponseType(typeof(JournalEntryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<JournalEntryDto>> Reverse(
        Guid id,
        ReverseJournalEntryRequest request,
        CancellationToken cancellationToken)
    {
        Result<JournalEntryDto> result =
            await _entries.ReverseAsync(id, request, cancellationToken).ConfigureAwait(false);
        return ToActionResult(result);
    }
}
