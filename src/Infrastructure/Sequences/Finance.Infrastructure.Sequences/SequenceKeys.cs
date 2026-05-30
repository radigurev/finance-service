namespace Finance.Infrastructure.Sequences;

/// <summary>
/// The seven built-in finance sequence keys (SDD-INFRA-003 §2.1). Exposed as constants so
/// callers reference a symbol rather than a raw string literal when requesting a number.
/// </summary>
public static class SequenceKeys
{
    /// <summary>Journal Entry sequence key (<c>JE-{yyyy}-{nnnnnn}</c>).</summary>
    public const string JournalEntry = "JE";

    /// <summary>Purchase Invoice sequence key (<c>ФПок-{yyyy}-{nnnnnn}</c>, НАП ledger).</summary>
    public const string PurchaseInvoice = "PINV";

    /// <summary>Sale Invoice sequence key (<c>ФПр-{yyyy}-{nnnnnn}</c>, НАП ledger).</summary>
    public const string SaleInvoice = "SINV";

    /// <summary>Credit Note sequence key (<c>КИ-{yyyy}-{nnnnnn}</c>).</summary>
    public const string CreditNote = "CN";

    /// <summary>Debit Note sequence key (<c>ДИ-{yyyy}-{nnnnnn}</c>).</summary>
    public const string DebitNote = "DN";

    /// <summary>Payment sequence key (<c>PAY-{yyyy}-{nnnnnn}</c>).</summary>
    public const string Payment = "PAY";

    /// <summary>Receipt sequence key (<c>RCT-{yyyy}-{nnnnnn}</c>).</summary>
    public const string Receipt = "RCT";
}
