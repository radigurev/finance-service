using Asp.Versioning;
using Finance.Common.Results;
using Finance.GenericFiltering.Models;
using Finance.Infrastructure.Web.Controllers;
using Finance.Infrastructure.Web.ErrorMapping;
using Finance.Invoices.API.Interfaces;
using Finance.ServiceModel.Invoices;
using Microsoft.AspNetCore.Mvc;
using Warehouse.Auth.AspNetCore.Authorization;

namespace Finance.Invoices.API.Controllers;

/// <summary>
/// REST endpoints for the invoice lifecycle (SDD-INV-001 §2, §5). Inherits <see cref="BaseApiController"/>
/// so every action translates a service <see cref="Result"/> / <see cref="Result{T}"/> into an RFC 7807
/// ProblemDetails-aware <see cref="ActionResult"/>. Confirmed and later states are immutable: correct via a
/// credit/debit note, never by editing.
/// </summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/invoices")]
[Produces("application/json")]
public sealed class InvoicesController : BaseApiController
{
    private readonly IInvoiceService _invoices;

    /// <summary>Creates a new <see cref="InvoicesController"/>.</summary>
    /// <param name="invoices">The invoice application service.</param>
    /// <param name="statusMap">The error-code → HTTP-status map used by the base controller.</param>
    public InvoicesController(IInvoiceService invoices, IErrorCodeToStatusMap statusMap)
        : base(statusMap)
    {
        _invoices = invoices;
    }

    /// <summary>Lists invoices as a filtered, sorted, and paged envelope (SDD-INV-001 §2.10).</summary>
    /// <param name="request">The filter, sort, and pagination request from the query string.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>A <see cref="PagedResult{T}"/> of <see cref="InvoiceDto"/>.</returns>
    [HttpGet]
    [RequirePermission("finance.invoice:read")]
    [ProducesResponseType(typeof(PagedResult<InvoiceDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PagedResult<InvoiceDto>>> List(
        [FromQuery] FilterRequest request,
        CancellationToken cancellationToken)
    {
        Result<PagedResult<InvoiceDto>> result =
            await _invoices.SearchAsync(request, cancellationToken).ConfigureAwait(false);
        return ToActionResult(result);
    }

    /// <summary>Returns a single invoice with its lines (SDD-INV-001 §2.10).</summary>
    /// <param name="id">The invoice identifier.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>The matching <see cref="InvoiceDto"/>, or a 404 ProblemDetails.</returns>
    [HttpGet("{id:guid}")]
    [RequirePermission("finance.invoice:read")]
    [ProducesResponseType(typeof(InvoiceDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<InvoiceDto>> Get(Guid id, CancellationToken cancellationToken)
    {
        Result<InvoiceDto> result = await _invoices.GetAsync(id, cancellationToken).ConfigureAwait(false);
        return ToActionResult(result);
    }

    /// <summary>Creates a draft invoice (SDD-INV-001 §2.3).</summary>
    /// <param name="request">The create-draft request body.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>The created <see cref="InvoiceDto"/>, or a validation/conflict ProblemDetails.</returns>
    [HttpPost]
    [RequirePermission("finance.invoice:create")]
    [ProducesResponseType(typeof(InvoiceDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<InvoiceDto>> Create(
        CreateInvoiceRequest request,
        CancellationToken cancellationToken)
    {
        Result<InvoiceDto> result =
            await _invoices.CreateDraftAsync(request, allowEmptyLines: false, cancellationToken).ConfigureAwait(false);

        if (!result.IsSuccess)
        {
            return ToActionResult(result);
        }

        return CreatedAtAction(nameof(Get), new { id = result.Value!.Id, version = "1" }, result.Value);
    }

    /// <summary>Updates a draft invoice (SDD-INV-001 §2.6).</summary>
    /// <param name="id">The invoice identifier.</param>
    /// <param name="request">The update request body.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>The updated <see cref="InvoiceDto"/>, or a 404 / 409 ProblemDetails.</returns>
    [HttpPut("{id:guid}")]
    [RequirePermission("finance.invoice:create")]
    [ProducesResponseType(typeof(InvoiceDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<InvoiceDto>> Update(
        Guid id,
        UpdateInvoiceRequest request,
        CancellationToken cancellationToken)
    {
        Result<InvoiceDto> result =
            await _invoices.UpdateDraftAsync(id, request, cancellationToken).ConfigureAwait(false);
        return ToActionResult(result);
    }

    /// <summary>Deletes a draft invoice (SDD-INV-001 §2.9).</summary>
    /// <param name="id">The invoice identifier.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns><c>200 OK</c> on success, or a 404 / 409 ProblemDetails.</returns>
    [HttpDelete("{id:guid}")]
    [RequirePermission("finance.invoice:create")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        Result result = await _invoices.DeleteDraftAsync(id, cancellationToken).ConfigureAwait(false);
        return ToActionResult(result);
    }

    /// <summary>Confirms a draft invoice (Draft → Confirmed) (SDD-INV-001 §2.4).</summary>
    /// <param name="id">The invoice identifier.</param>
    /// <param name="request">The confirm request carrying the row version.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>The confirmed <see cref="InvoiceDto"/>, or a state / validation / concurrency ProblemDetails.</returns>
    [HttpPost("{id:guid}/confirm")]
    [RequirePermission("finance.invoice:confirm")]
    [ProducesResponseType(typeof(InvoiceDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<InvoiceDto>> Confirm(
        Guid id,
        ConfirmInvoiceRequest request,
        CancellationToken cancellationToken)
    {
        Result<InvoiceDto> result = await _invoices.ConfirmAsync(id, request, cancellationToken).ConfigureAwait(false);
        return ToActionResult(result);
    }

    /// <summary>Completes posting of a confirmed invoice (Confirmed → Posted) (SDD-INV-001 §2.5).</summary>
    /// <param name="id">The invoice identifier.</param>
    /// <param name="request">The post request carrying the row version.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>The posted <see cref="InvoiceDto"/>, or a state / concurrency ProblemDetails.</returns>
    [HttpPost("{id:guid}/post")]
    [RequirePermission("finance.invoice:post")]
    [ProducesResponseType(typeof(InvoiceDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<InvoiceDto>> Post(
        Guid id,
        PostInvoiceRequest request,
        CancellationToken cancellationToken)
    {
        Result<InvoiceDto> result = await _invoices.PostAsync(id, request, cancellationToken).ConfigureAwait(false);
        return ToActionResult(result);
    }

    /// <summary>
    /// Cancels (voids) a draft or confirmed invoice (SDD-INV-001 §2.6). An invoice that already carries payment
    /// allocations is rejected with <c>INVOICE_HAS_SETTLEMENTS</c> (409) — a best-effort guard over the
    /// eventually-consistent settlement mirror; the operator releases the allocation in the Payments service
    /// first (SDD-INV-001 §2.6/§2.14).
    /// </summary>
    /// <param name="id">The invoice identifier.</param>
    /// <param name="request">The cancel request carrying the mandatory reason and row version.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>The cancelled <see cref="InvoiceDto"/>, or a state / validation / concurrency ProblemDetails.</returns>
    [HttpPost("{id:guid}/cancel")]
    [RequirePermission("finance.invoice:cancel")]
    [ProducesResponseType(typeof(InvoiceDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<InvoiceDto>> Cancel(
        Guid id,
        CancelInvoiceRequest request,
        CancellationToken cancellationToken)
    {
        Result<InvoiceDto> result = await _invoices.CancelAsync(id, request, cancellationToken).ConfigureAwait(false);
        return ToActionResult(result);
    }
}
