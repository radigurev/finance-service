using Finance.Common.Enums;
using Finance.Common.ErrorCodes;
using Finance.Common.Validation;

namespace Finance.Payments.API.Validation;

/// <summary>
/// Chain rule 1 (SDD-PAY-002 §2.5): the payment MUST be <c>Confirmed</c> or <c>Posted</c>, otherwise
/// <c>PAYMENT_NOT_ALLOCATABLE</c>. A <c>Draft</c> payment is not yet a legally-numbered document, and a
/// <c>Cancelled</c> or <c>Reversed</c> payment MUST NOT be matched.
/// <para>This is the ONLY rule the deallocate path exercises: it is registered first so an ineligible payment
/// short-circuits before any projection read (§2.14).</para>
/// </summary>
public sealed class PaymentAllocatableValidator : IChainValidator<PaymentAllocationValidationContext>
{
    /// <inheritdoc />
    public Task<ChainValidationResult> ValidateAsync(
        PaymentAllocationValidationContext request,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        PaymentStatus status = request.Payment.Status;
        if (status == PaymentStatus.Confirmed || status == PaymentStatus.Posted)
        {
            return Task.FromResult(ChainValidationResult.Success());
        }

        return Task.FromResult(ChainValidationResult.Failure(
            PaymentErrorCodes.PAYMENT_NOT_ALLOCATABLE,
            $"A payment in state '{status}' cannot be allocated or deallocated; it must be Confirmed or Posted."));
    }
}
