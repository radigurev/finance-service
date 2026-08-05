using Finance.Common.ErrorCodes;
using Finance.Common.Validation;
using Finance.Payments.DBModel.Models;
using Finance.ServiceModel.Payments;

namespace Finance.Payments.API.Validation;

/// <summary>
/// Chain rule 9 (SDD-PAY-002 §2.5): per invoice, the open item's locally-owned settled amount plus the
/// requested amount MUST be less than or equal to its gross total, otherwise
/// <c>PAYMENT_ALLOCATION_EXCEEDS_OUTSTANDING</c>.
/// <para>Requested amounts are accumulated PER INVOICE first, so a request that splits an over-allocation
/// across two items for the same invoice is still caught here (the duplicate rule already rejects that shape,
/// which makes this accumulation defensive). Comparison is exact <c>decimal</c> at two decimal places: a
/// request that would make the settled amount one cent over the gross total fails and writes nothing.</para>
/// </summary>
public sealed class AllocationWithinOutstandingValidator : IChainValidator<PaymentAllocationValidationContext>
{
    /// <inheritdoc />
    public Task<ChainValidationResult> ValidateAsync(
        PaymentAllocationValidationContext request,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        Dictionary<Guid, decimal> requestedByInvoice = AccumulateRequested(request.Items);

        foreach (KeyValuePair<Guid, decimal> requested in requestedByInvoice)
        {
            if (!request.OpenItems.TryGetValue(requested.Key, out InvoiceOpenItem? openItem))
            {
                continue;
            }

            if (openItem.SettledAmount + requested.Value > openItem.GrossTotal)
            {
                return Task.FromResult(ChainValidationResult.Failure(
                    PaymentErrorCodes.PAYMENT_ALLOCATION_EXCEEDS_OUTSTANDING,
                    $"Allocating {requested.Value} to invoice '{requested.Key}' would take its settled amount from {openItem.SettledAmount} past the gross total {openItem.GrossTotal}."));
            }
        }

        return Task.FromResult(ChainValidationResult.Success());
    }

    private static Dictionary<Guid, decimal> AccumulateRequested(IReadOnlyList<AllocatePaymentItem> items)
    {
        Dictionary<Guid, decimal> requestedByInvoice = new();

        foreach (AllocatePaymentItem item in items)
        {
            requestedByInvoice.TryGetValue(item.InvoiceId, out decimal running);
            requestedByInvoice[item.InvoiceId] = running + item.AllocatedAmount;
        }

        return requestedByInvoice;
    }
}
