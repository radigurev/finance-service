using Finance.Common.Enums;
using Finance.Common.ErrorCodes;
using Finance.Common.Validation;
using Finance.Payments.DBModel.Models;
using Finance.ServiceModel.Payments;

namespace Finance.Payments.API.Validation;

/// <summary>
/// Chain rule 3 (SDD-PAY-002 §2.5): each open item's mirrored status MUST be <c>Confirmed</c> or <c>Posted</c>,
/// otherwise <c>PAYMENT_ALLOCATION_INVOICE_NOT_ELIGIBLE</c>. The whitelist is exhaustive over the §2.2 value
/// set, so BOTH terminal statuses are rejected.
/// <para><c>Reversed</c> is included because a reversed invoice's ledger effect is fully offset: matching real
/// cash to it would consume the payment's unallocated amount while the genuinely open invoice stayed
/// outstanding. The rule is only ENFORCEABLE because the §2.3 reversal consumer mirrors the reversal — a
/// projection that never learned about it would keep reading <c>Posted</c> and pass this rule.</para>
/// </summary>
public sealed class AllocationInvoiceEligibleValidator : IChainValidator<PaymentAllocationValidationContext>
{
    private static readonly IReadOnlySet<string> EligibleStatuses = new HashSet<string>(StringComparer.Ordinal)
    {
        nameof(InvoiceStatus.Confirmed),
        nameof(InvoiceStatus.Posted)
    };

    /// <inheritdoc />
    public Task<ChainValidationResult> ValidateAsync(
        PaymentAllocationValidationContext request,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        foreach (AllocatePaymentItem item in request.Items)
        {
            if (!request.OpenItems.TryGetValue(item.InvoiceId, out InvoiceOpenItem? openItem))
            {
                continue;
            }

            if (!EligibleStatuses.Contains(openItem.InvoiceStatus))
            {
                return Task.FromResult(ChainValidationResult.Failure(
                    PaymentErrorCodes.PAYMENT_ALLOCATION_INVOICE_NOT_ELIGIBLE,
                    $"Invoice '{item.InvoiceId}' is '{openItem.InvoiceStatus}'; only a Confirmed or Posted invoice may be settled."));
            }
        }

        return Task.FromResult(ChainValidationResult.Success());
    }
}
