using Finance.Common.Results;

namespace Finance.Invoices.API.Interfaces;

/// <summary>
/// Maintains the invoice-side settlement mirror from SDD-PAY-002's allocation events (SDD-INV-001 §2.14/§2.15).
/// The handshake is ONE-WAY: this service publishes no back-event, never calls the Payments service, and is
/// never waited on by allocation.
/// <para>Settlement is ORTHOGONAL to the lifecycle: applying an update never invokes
/// <c>IWorkflowEngine&lt;Invoice&gt;</c>, never appends an <c>invoice_status_history</c> row, never touches the
/// document number, lines, or totals, and never changes <c>Status</c> — including on a <c>Cancelled</c> or
/// <c>Reversed</c> invoice, where a later update (in particular the orphan-repair release carrying
/// <c>0.00</c>) MUST still be applied.</para>
/// </summary>
public interface IInvoiceSettlementService
{
    /// <summary>
    /// Applies one allocation event to the invoice's settlement mirror: it drops a strictly stale event as a
    /// silent success, asserts the settled amount against the invoice's own gross total, assigns the absolute
    /// amount, re-derives the settlement status locally, stamps the ordering token, and writes an audit row —
    /// all in one transaction (SDD-INV-001 §2.15).
    /// </summary>
    /// <param name="update">The settlement update the allocation event carries.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>
    /// A success <see cref="Result"/> when the update was applied OR deliberately dropped as stale;
    /// <c>INVOICE_NOT_FOUND</c> for an unknown invoice and
    /// <c>INVOICE_SETTLEMENT_EXCEEDS_GROSS_TOTAL</c> for a ceiling breach — both of which the calling consumer
    /// turns into a throw so MassTransit retries and finally dead-letters rather than clamping a ledger figure.
    /// </returns>
    Task<Result> ApplyAsync(InvoiceSettlementUpdate update, CancellationToken cancellationToken);
}
