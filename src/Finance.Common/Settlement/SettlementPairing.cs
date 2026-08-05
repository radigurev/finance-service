using Finance.Common.Enums;

namespace Finance.Common.Settlement;

/// <summary>
/// The single, shared table of document-type pairs a payment may settle (SDD-PAY-002 §2.5 rule 10). It states
/// the ACCOUNTING match — that both documents moved the SAME control account — which the cheaper
/// <c>Direction</c> pre-filter cannot express, because <c>InvoiceDirection</c> encodes CASH direction and
/// groups <see cref="InvoiceDocumentType.SaleInvoice"/> with <see cref="InvoiceDocumentType.DebitNote"/> under
/// <c>AR</c> and <see cref="InvoiceDocumentType.PurchaseInvoice"/> with
/// <see cref="InvoiceDocumentType.CreditNote"/> under <c>AP</c>.
/// <para>The permitted pairs are exactly <c>CustomerReceipt → { SaleInvoice, DebitNote }</c> (the customer
/// control account) and <c>SupplierPayment → { PurchaseInvoice }</c> (the supplier control account). Document
/// TYPES are paired rather than resolved account codes because no <c>CREDIT_NOTE</c> control account is
/// determinable from shipped posting rules (pending the SDD-PAY-001 §7 accountant sign-off) and because
/// account codes are country data that SDD-CTRY-001 keeps behind <c>ICountryStrategy</c>, out of the
/// country-agnostic core.</para>
/// <para>The table lives in <c>Finance.Common</c> so no service re-derives it: it is read BOTH by the
/// SDD-PAY-002 §2.5 allocation rule AND by the §2.3 projection-admission rule, and
/// <see cref="IsSettleableInvoiceType"/> is DERIVED from <see cref="CanSettle"/> rather than being a second
/// literal list — so admission and allocation can never drift, and widening a pair widens both with no second
/// edit.</para>
/// </summary>
public static class SettlementPairing
{
    private static readonly IReadOnlyDictionary<PaymentDocumentType, IReadOnlySet<InvoiceDocumentType>> Pairs =
        new Dictionary<PaymentDocumentType, IReadOnlySet<InvoiceDocumentType>>
        {
            [PaymentDocumentType.CustomerReceipt] = new HashSet<InvoiceDocumentType>
            {
                InvoiceDocumentType.SaleInvoice,
                InvoiceDocumentType.DebitNote
            },
            [PaymentDocumentType.SupplierPayment] = new HashSet<InvoiceDocumentType>
            {
                InvoiceDocumentType.PurchaseInvoice
            }
        };

    private static readonly IReadOnlyList<PaymentDocumentType> PaymentDocumentTypes =
        Enum.GetValues<PaymentDocumentType>();

    /// <summary>
    /// Returns the invoice document types the supplied payment document type may settle (SDD-PAY-002 §2.5
    /// rule 10). An unrecognized payment type yields an empty set rather than throwing, so a future enum
    /// member is rejected by the allocation rule instead of faulting the request.
    /// </summary>
    /// <param name="paymentDocumentType">The payment document type whose pairs are requested.</param>
    /// <returns>The settleable invoice document types, possibly empty.</returns>
    public static IReadOnlySet<InvoiceDocumentType> AllocatableInvoiceTypesFor(
        PaymentDocumentType paymentDocumentType)
    {
        if (Pairs.TryGetValue(paymentDocumentType, out IReadOnlySet<InvoiceDocumentType>? invoiceTypes))
        {
            return invoiceTypes;
        }

        return new HashSet<InvoiceDocumentType>();
    }

    /// <summary>
    /// Determines whether the supplied pair is a documented settlement pair (SDD-PAY-002 §2.5 rule 10).
    /// Every other combination is rejected with <c>PAYMENT_ALLOCATION_CONTROL_ACCOUNT_MISMATCH</c>, because
    /// allocating across two different control accounts drifts both of them from their sub-ledgers by the
    /// allocated amount.
    /// </summary>
    /// <param name="paymentDocumentType">The paying document's type.</param>
    /// <param name="invoiceDocumentType">The settled document's type.</param>
    /// <returns><c>true</c> when the pair moves the same control account; otherwise <c>false</c>.</returns>
    public static bool CanSettle(
        PaymentDocumentType paymentDocumentType,
        InvoiceDocumentType invoiceDocumentType)
    {
        return AllocatableInvoiceTypesFor(paymentDocumentType).Contains(invoiceDocumentType);
    }

    /// <summary>
    /// Determines whether ANY payment document type can settle the supplied invoice document type — the
    /// SDD-PAY-002 §2.3 projection-admission predicate. It is DERIVED from <see cref="CanSettle"/> over every
    /// <see cref="PaymentDocumentType"/> and is deliberately NOT a second literal list, so the admission rule
    /// and the §2.5 allocation rule cannot drift apart.
    /// <para>In v1 this excludes exactly one type, <see cref="InvoiceDocumentType.CreditNote"/>: no payment
    /// document type can settle it, so admitting it into the open-item projection would age it as a phantom
    /// balance that can never reach <c>0.00</c>. Credit-note settlement is deferred with the refund / offset
    /// feature; widening the pairs above automatically widens the projection.</para>
    /// </summary>
    /// <param name="invoiceDocumentType">The invoice document type to test for admission.</param>
    /// <returns><c>true</c> when some payment document type can settle it; otherwise <c>false</c>.</returns>
    public static bool IsSettleableInvoiceType(InvoiceDocumentType invoiceDocumentType)
    {
        foreach (PaymentDocumentType paymentDocumentType in PaymentDocumentTypes)
        {
            if (CanSettle(paymentDocumentType, invoiceDocumentType))
            {
                return true;
            }
        }

        return false;
    }
}
