namespace Finance.Invoices.API.Interfaces;

/// <summary>
/// The input to the <c>Posted → Reversed</c> reversal path (SDD-INV-001 §2.7): the original invoice a
/// fully-offsetting credit/debit note has corrected, the note that corrects it, and the operator reason
/// recorded on the audit row and the published <c>InvoiceReversedEvent</c>.
/// <para>There is deliberately NO reversal endpoint in v1 (SDD-INV-001 §5): the automatic full-offset DETECTION
/// that triggers the transition stays deferred with the credit/debit-note posting-rule templates. What ships is
/// the transition path itself plus its publish obligation, so a reversal can never land without the
/// sub-ledger being told.</para>
/// </summary>
public sealed record InvoiceReversalRequest
{
    /// <summary>The posted ORIGINAL invoice being reversed.</summary>
    public required Guid InvoiceId { get; init; }

    /// <summary>The credit/debit note whose full offset reverses the original.</summary>
    public required Guid CorrectingInvoiceId { get; init; }

    /// <summary>The operator-supplied reason recorded with the reversal.</summary>
    public required string Reason { get; init; }
}
