using Asp.Versioning;
using Finance.Common.Results;
using Finance.Infrastructure.Web.Controllers;
using Finance.Infrastructure.Web.ErrorMapping;
using Finance.Journal.API.Interfaces;
using Finance.ServiceModel.Journal;
using Finance.ServiceModel.Posting;
using Microsoft.AspNetCore.Mvc;
using Warehouse.Auth.AspNetCore.Authorization;

namespace Finance.Journal.API.Controllers;

/// <summary>
/// REST endpoint for the Posting Engine apply operation (SDD-FIN-006 §2.3). Inherits
/// <see cref="BaseApiController"/> so the service <see cref="Result{T}"/> is translated into an RFC 7807
/// ProblemDetails-aware <see cref="ActionResult"/>.
/// </summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/posting")]
[Produces("application/json")]
public sealed class PostingController : BaseApiController
{
    private readonly IPostingEngine _postingEngine;

    /// <summary>Creates a new <see cref="PostingController"/>.</summary>
    /// <param name="postingEngine">The posting engine that applies a rule to an amount context.</param>
    /// <param name="statusMap">The error-code → HTTP-status map used by the base controller.</param>
    public PostingController(IPostingEngine postingEngine, IErrorCodeToStatusMap statusMap)
        : base(statusMap)
    {
        _postingEngine = postingEngine;
    }

    /// <summary>
    /// Applies a named posting rule to the supplied amount context, producing a balanced journal entry
    /// (created as a draft and, unless suppressed, posted immediately).
    /// </summary>
    /// <param name="request">The apply request body.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>The resulting <see cref="JournalEntryDto"/>, or a validation / not-found / balance ProblemDetails.</returns>
    [HttpPost("apply")]
    [RequirePermission("finance.posting:apply")]
    [ProducesResponseType(typeof(JournalEntryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<JournalEntryDto>> Apply(
        ApplyPostingRuleRequest request,
        CancellationToken cancellationToken)
    {
        Result<JournalEntryDto> result =
            await _postingEngine.ApplyAsync(request, cancellationToken).ConfigureAwait(false);
        return ToActionResult(result);
    }
}
