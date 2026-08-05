using Finance.Common.ErrorCodes;
using Finance.Common.Validation;
using Finance.Payments.DBModel.Models;
using Finance.ServiceModel.Payments;

namespace Finance.Payments.API.Validation;

/// <summary>
/// Chain rule 8 (SDD-PAY-002 §2.5): the sum of the payment's EXISTING allocations plus the sum of the
/// REQUESTED items MUST be less than or equal to <c>Payment.Amount</c>, otherwise
/// <c>PAYMENT_ALLOCATION_EXCEEDS_PAYMENT</c>.
/// <para>The request items are summed TOGETHER, never validated independently: the documented edge case is a
/// multi-item request where each item fits its own invoice's outstanding but the item SUM exceeds the payment.
/// Comparison is exact <c>decimal</c> at two decimal places — a single cent over the bound fails, with no
/// epsilon and no tolerance band. Because over-allocation is forbidden outright, v1 needs no residual
/// write-off and no unapplied-cash suspense rule.</para>
/// </summary>
public sealed class AllocationWithinPaymentValidator : IChainValidator<PaymentAllocationValidationContext>
{
    /// <inheritdoc />
    public Task<ChainValidationResult> ValidateAsync(
        PaymentAllocationValidationContext request,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        decimal alreadyAllocated = 0m;
        foreach (PaymentAllocation existing in request.Payment.Allocations)
        {
            alreadyAllocated += existing.AllocatedAmount;
        }

        decimal requested = 0m;
        foreach (AllocatePaymentItem item in request.Items)
        {
            requested += item.AllocatedAmount;
        }

        if (alreadyAllocated + requested <= request.Payment.Amount)
        {
            return Task.FromResult(ChainValidationResult.Success());
        }

        return Task.FromResult(ChainValidationResult.Failure(
            PaymentErrorCodes.PAYMENT_ALLOCATION_EXCEEDS_PAYMENT,
            $"Allocating {requested} on top of {alreadyAllocated} would exceed the payment amount {request.Payment.Amount}."));
    }
}
