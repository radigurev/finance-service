using Finance.Common.ErrorCodes;
using Finance.Common.Validation;
using Finance.Payments.DBModel.Models;
using Finance.ServiceModel.Payments;

namespace Finance.Payments.API.Validation;

/// <summary>
/// Chain rule 7 (SDD-PAY-002 §2.5): no allocation row may already exist for the <c>(PaymentId, InvoiceId)</c>
/// pair, AND no pair may appear twice WITHIN the request — either yields
/// <c>PAYMENT_ALLOCATION_DUPLICATE</c>.
/// <para>Both halves are checked HERE so the chain, never a <c>DbUpdateException</c> from the UNIQUE index
/// <c>IX_PaymentAllocations_PaymentInvoice</c>, is the user-facing path (§2.14). The index remains the
/// database-level backstop. An existing match is changed by deallocating and re-allocating: v1 has no in-place
/// amount amendment.</para>
/// </summary>
public sealed class AllocationDuplicateValidator : IChainValidator<PaymentAllocationValidationContext>
{
    /// <inheritdoc />
    public Task<ChainValidationResult> ValidateAsync(
        PaymentAllocationValidationContext request,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        HashSet<Guid> seen = new();

        foreach (PaymentAllocation existing in request.Payment.Allocations)
        {
            seen.Add(existing.InvoiceId);
        }

        foreach (AllocatePaymentItem item in request.Items)
        {
            if (!seen.Add(item.InvoiceId))
            {
                return Task.FromResult(ChainValidationResult.Failure(
                    PaymentErrorCodes.PAYMENT_ALLOCATION_DUPLICATE,
                    $"Invoice '{item.InvoiceId}' is already matched to this payment, or appears more than once in the request."));
            }
        }

        return Task.FromResult(ChainValidationResult.Success());
    }
}
