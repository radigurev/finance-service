using Asp.Versioning;
using Finance.Common.Results;
using Finance.GenericFiltering.Models;
using Finance.Infrastructure.Web.Controllers;
using Finance.Infrastructure.Web.ErrorMapping;
using Finance.Payments.API.Interfaces;
using Finance.ServiceModel.Payments;
using Microsoft.AspNetCore.Mvc;
using Warehouse.Auth.AspNetCore.Authorization;

namespace Finance.Payments.API.Controllers;

/// <summary>
/// REST endpoints for the payment lifecycle (SDD-PAY-001 §2, §5). Inherits <see cref="BaseApiController"/> so
/// every action translates a service <see cref="Result"/> / <see cref="Result{T}"/> into an RFC 7807
/// ProblemDetails-aware <see cref="ActionResult"/>. Confirmed and later states are immutable: a posted payment
/// is corrected by reversal, never by editing or cancelling.
/// </summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/payments")]
[Produces("application/json")]
public sealed class PaymentsController : BaseApiController
{
    private readonly IPaymentService _payments;

    /// <summary>Creates a new <see cref="PaymentsController"/>.</summary>
    /// <param name="payments">The payment application service.</param>
    /// <param name="statusMap">The error-code → HTTP-status map used by the base controller.</param>
    public PaymentsController(IPaymentService payments, IErrorCodeToStatusMap statusMap)
        : base(statusMap)
    {
        _payments = payments;
    }

    /// <summary>Lists payments as a filtered, sorted, and paged envelope (SDD-PAY-001 §2.11).</summary>
    /// <param name="request">The filter, sort, and pagination request from the query string.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>A <see cref="PagedResult{T}"/> of <see cref="PaymentDto"/>.</returns>
    [HttpGet]
    [RequirePermission("finance.payment:read")]
    [ProducesResponseType(typeof(PagedResult<PaymentDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PagedResult<PaymentDto>>> List(
        [FromQuery] FilterRequest request,
        CancellationToken cancellationToken)
    {
        Result<PagedResult<PaymentDto>> result =
            await _payments.SearchAsync(request, cancellationToken).ConfigureAwait(false);
        return ToActionResult(result);
    }

    /// <summary>Returns a single payment (SDD-PAY-001 §2.11).</summary>
    /// <param name="id">The payment identifier.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>The matching <see cref="PaymentDto"/>, or a 404 ProblemDetails.</returns>
    [HttpGet("{id:guid}")]
    [RequirePermission("finance.payment:read")]
    [ProducesResponseType(typeof(PaymentDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PaymentDto>> Get(Guid id, CancellationToken cancellationToken)
    {
        Result<PaymentDto> result = await _payments.GetAsync(id, cancellationToken).ConfigureAwait(false);
        return ToActionResult(result);
    }

    /// <summary>Creates a draft payment (SDD-PAY-001 §2.3).</summary>
    /// <param name="request">The create-draft request body.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>The created <see cref="PaymentDto"/>, or a validation / reference-data ProblemDetails.</returns>
    [HttpPost]
    [RequirePermission("finance.payment:create")]
    [ProducesResponseType(typeof(PaymentDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<PaymentDto>> Create(
        CreatePaymentRequest request,
        CancellationToken cancellationToken)
    {
        Result<PaymentDto> result =
            await _payments.CreateDraftAsync(request, cancellationToken).ConfigureAwait(false);

        if (!result.IsSuccess)
        {
            return ToActionResult(result);
        }

        return CreatedAtAction(nameof(Get), new { id = result.Value!.Id, version = "1" }, result.Value);
    }

    /// <summary>Updates a draft payment (SDD-PAY-001 §2.6).</summary>
    /// <param name="id">The payment identifier.</param>
    /// <param name="request">The update request body.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>The updated <see cref="PaymentDto"/>, or a 400 / 404 / 409 ProblemDetails.</returns>
    [HttpPut("{id:guid}")]
    [RequirePermission("finance.payment:create")]
    [ProducesResponseType(typeof(PaymentDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<PaymentDto>> Update(
        Guid id,
        UpdatePaymentRequest request,
        CancellationToken cancellationToken)
    {
        Result<PaymentDto> result =
            await _payments.UpdateDraftAsync(id, request, cancellationToken).ConfigureAwait(false);
        return ToActionResult(result);
    }

    /// <summary>Deletes a draft payment (SDD-PAY-001 §2.6).</summary>
    /// <param name="id">The payment identifier.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns><c>200 OK</c> on success, or a 404 / 409 ProblemDetails.</returns>
    [HttpDelete("{id:guid}")]
    [RequirePermission("finance.payment:create")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        Result result = await _payments.DeleteDraftAsync(id, cancellationToken).ConfigureAwait(false);
        return ToActionResult(result);
    }

    /// <summary>Confirms a draft payment (Draft → Confirmed) (SDD-PAY-001 §2.4).</summary>
    /// <param name="id">The payment identifier.</param>
    /// <param name="request">The confirm request carrying the row version.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>The confirmed <see cref="PaymentDto"/>, or a guard / state / concurrency ProblemDetails.</returns>
    [HttpPost("{id:guid}/confirm")]
    [RequirePermission("finance.payment:confirm")]
    [ProducesResponseType(typeof(PaymentDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<PaymentDto>> Confirm(
        Guid id,
        ConfirmPaymentRequest request,
        CancellationToken cancellationToken)
    {
        Result<PaymentDto> result = await _payments.ConfirmAsync(id, request, cancellationToken).ConfigureAwait(false);
        return ToActionResult(result);
    }

    /// <summary>
    /// Operator-driven post completion of a confirmed payment (SDD-PAY-001 §2.5). Never posts a journal entry
    /// itself: an already-<c>Posted</c> payment answers 200 idempotently, and a <c>Confirmed</c>-but-unlinked
    /// payment re-enqueues <c>PaymentConfirmedEvent</c> and answers <c>PAYMENT_POSTING_PENDING</c> (409)
    /// without mutating the payment row — the documented recovery path for a stuck handshake.
    /// </summary>
    /// <param name="id">The payment identifier.</param>
    /// <param name="request">The post request carrying the row version.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>The posted <see cref="PaymentDto"/>, or a state / period / concurrency ProblemDetails.</returns>
    [HttpPost("{id:guid}/post")]
    [RequirePermission("finance.payment:post")]
    [ProducesResponseType(typeof(PaymentDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<PaymentDto>> Post(
        Guid id,
        PostPaymentRequest request,
        CancellationToken cancellationToken)
    {
        Result<PaymentDto> result = await _payments.PostAsync(id, request, cancellationToken).ConfigureAwait(false);
        return ToActionResult(result);
    }

    /// <summary>
    /// Cancels (voids) a <c>Draft</c> payment (SDD-PAY-001 §2.6). Legal from <c>Draft</c> ONLY: a confirmed
    /// payment's posting is already in flight, so it is completed and then corrected by reversal.
    /// </summary>
    /// <param name="id">The payment identifier.</param>
    /// <param name="request">The cancel request carrying the mandatory reason and row version.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>The cancelled <see cref="PaymentDto"/>, or a state / validation / concurrency ProblemDetails.</returns>
    [HttpPost("{id:guid}/cancel")]
    [RequirePermission("finance.payment:cancel")]
    [ProducesResponseType(typeof(PaymentDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<PaymentDto>> Cancel(
        Guid id,
        CancelPaymentRequest request,
        CancellationToken cancellationToken)
    {
        Result<PaymentDto> result = await _payments.CancelAsync(id, request, cancellationToken).ConfigureAwait(false);
        return ToActionResult(result);
    }

    /// <summary>
    /// Reverses a <c>Posted</c> payment (SDD-PAY-001 §2.7) — the immutability-preserving correction. Pre-checks
    /// the fiscal period of the linked entry's date; the reversing entry is never re-dated, so a closed original
    /// period is a hard block until it is reopened.
    /// </summary>
    /// <param name="id">The payment identifier.</param>
    /// <param name="request">The reverse request carrying the mandatory reason and row version.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>The reversed <see cref="PaymentDto"/>, or a state / period / allocation ProblemDetails.</returns>
    [HttpPost("{id:guid}/reverse")]
    [RequirePermission("finance.payment:reverse")]
    [ProducesResponseType(typeof(PaymentDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<PaymentDto>> Reverse(
        Guid id,
        ReversePaymentRequest request,
        CancellationToken cancellationToken)
    {
        Result<PaymentDto> result = await _payments.ReverseAsync(id, request, cancellationToken).ConfigureAwait(false);
        return ToActionResult(result);
    }
}
