using Finance.Common.ErrorCodes;
using Finance.Common.Validation;
using Finance.ServiceModel.Payments;

namespace Finance.Payments.API.Validation;

/// <summary>
/// Chain rule 2 (SDD-PAY-002 §2.5): every requested invoice MUST exist in the LOCAL open-item projection,
/// otherwise <c>PAYMENT_ALLOCATION_INVOICE_NOT_FOUND</c> (404).
/// <para>Absence has TWO causes and this rule deliberately does not try to tell them apart — doing so would
/// need exactly the cross-service read the projection exists to remove. The first is the projection's
/// eventual-consistency LAG, a legitimate transient outcome the caller retries; the second is a document type
/// the §2.3 admission rule never projects (v1: a credit note), which is permanent. The service MUST NOT fall
/// back to a synchronous read and MUST NOT create a speculative open item from the request.</para>
/// </summary>
public sealed class AllocationInvoiceKnownValidator : IChainValidator<PaymentAllocationValidationContext>
{
    /// <inheritdoc />
    public Task<ChainValidationResult> ValidateAsync(
        PaymentAllocationValidationContext request,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        foreach (AllocatePaymentItem item in request.Items)
        {
            if (!request.OpenItems.ContainsKey(item.InvoiceId))
            {
                return Task.FromResult(ChainValidationResult.Failure(
                    PaymentErrorCodes.PAYMENT_ALLOCATION_INVOICE_NOT_FOUND,
                    $"Invoice '{item.InvoiceId}' is not an allocatable open item in the local projection."));
            }
        }

        return Task.FromResult(ChainValidationResult.Success());
    }
}
