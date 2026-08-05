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
/// REST endpoints for payment allocation — the sub-ledger matching sub-collection of the payment aggregate
/// (SDD-PAY-002 §2.4, §2.6, §2.7, §2.13). Inherits <see cref="BaseApiController"/> so every action translates a
/// service <see cref="Result"/> / <see cref="Result{T}"/> into an RFC 7807 ProblemDetails-aware
/// <see cref="ActionResult"/>.
/// <para>The write actions require the NEW <c>finance.payment:allocate</c> permission, which must be seeded
/// manually in the auth service while permission auto-registration is deferred; the list requires
/// <c>finance.payment:read</c>.</para>
/// <para>Nothing on this surface is cached: allocations, open items, settlement state, and outstanding balances
/// are transactional data.</para>
/// </summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/payments/{paymentId:guid}/allocations")]
[Produces("application/json")]
public sealed class PaymentAllocationsController : BaseApiController
{
    private readonly IPaymentAllocationService _allocations;

    /// <summary>Creates a new <see cref="PaymentAllocationsController"/>.</summary>
    /// <param name="allocations">The payment allocation application service.</param>
    /// <param name="statusMap">The error-code → HTTP-status map used by the base controller.</param>
    public PaymentAllocationsController(
        IPaymentAllocationService allocations,
        IErrorCodeToStatusMap statusMap)
        : base(statusMap)
    {
        _allocations = allocations;
    }

    /// <summary>
    /// Lists a payment's allocation rows as a filtered, sorted, and paged envelope, each row enriched from the
    /// LOCAL invoice open-item projection (SDD-PAY-002 §2.7). Default order is allocation time descending, with
    /// the primary key appended as the final deterministic sort term; page size is capped at 200. A payment with
    /// no allocations returns an EMPTY page with 200 — an unallocated payment is a normal business state.
    /// </summary>
    /// <param name="paymentId">The owning payment identifier.</param>
    /// <param name="request">The filter, sort, and pagination request from the query string.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>A <see cref="PagedResult{T}"/> of <see cref="PaymentAllocationDto"/>.</returns>
    [HttpGet]
    [RequirePermission("finance.payment:read")]
    [ProducesResponseType(typeof(PagedResult<PaymentAllocationDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PagedResult<PaymentAllocationDto>>> List(
        Guid paymentId,
        [FromQuery] FilterRequest request,
        CancellationToken cancellationToken)
    {
        Result<PagedResult<PaymentAllocationDto>> result = await _allocations
            .ListAsync(paymentId, request, cancellationToken)
            .ConfigureAwait(false);
        return ToActionResult(result);
    }

    /// <summary>
    /// Matches a confirmed-or-later payment against an explicit list of invoices (SDD-PAY-002 §2.4). The call is
    /// ALL-OR-NOTHING: one transaction over every item, so a single failing invariant writes no row, publishes no
    /// event, and records no audit row.
    /// <para>It answers <b>200, not 201</b>, and emits no <c>Location</c> header: the created rows are a
    /// sub-collection of the payment aggregate, not independently addressable resources, and the response body
    /// already carries everything the caller needs.</para>
    /// </summary>
    /// <param name="paymentId">The payment to match.</param>
    /// <param name="request">The explicit item list plus the payment's base64 row version.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>The created rows and the post-change payment and invoice state, or a ProblemDetails.</returns>
    [HttpPost]
    [RequirePermission("finance.payment:allocate")]
    [ProducesResponseType(typeof(AllocatePaymentResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AllocatePaymentResultDto>> Allocate(
        Guid paymentId,
        AllocatePaymentRequest request,
        CancellationToken cancellationToken)
    {
        Result<AllocatePaymentResultDto> result = await _allocations
            .AllocateAsync(paymentId, request, cancellationToken)
            .ConfigureAwait(false);
        return ToActionResult(result);
    }

    /// <summary>
    /// Removes one allocation row and releases the matched amount (SDD-PAY-002 §2.6). The lookup is scoped by
    /// <c>(paymentId, allocationId)</c>, so an allocation owned by a DIFFERENT payment answers 404 — never a
    /// cross-payment delete. Deallocation posts nothing, reverses nothing, and leaves the payment's status
    /// untouched.
    /// <para>v1 has no in-place amendment of an allocation amount: a wrong amount is corrected by releasing the
    /// row and allocating again.</para>
    /// </summary>
    /// <param name="paymentId">The owning payment identifier.</param>
    /// <param name="allocationId">The allocation row to release.</param>
    /// <param name="rowVersion">
    /// The OPTIONAL base64 payment row version. When supplied it is applied as the concurrency token; when
    /// omitted the token loaded inside the transaction still guards a concurrent write.
    /// </param>
    /// <param name="reason">An OPTIONAL free-text reason, persisted on the audit row when supplied.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>The released amount and the post-release payment and invoice state, or a ProblemDetails.</returns>
    [HttpDelete("{allocationId:int}")]
    [RequirePermission("finance.payment:allocate")]
    [ProducesResponseType(typeof(DeallocatePaymentResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<DeallocatePaymentResultDto>> Deallocate(
        Guid paymentId,
        int allocationId,
        [FromQuery] string? rowVersion,
        [FromQuery] string? reason,
        CancellationToken cancellationToken)
    {
        Result<DeallocatePaymentResultDto> result = await _allocations
            .DeallocateAsync(paymentId, allocationId, rowVersion, reason, cancellationToken)
            .ConfigureAwait(false);
        return ToActionResult(result);
    }
}
