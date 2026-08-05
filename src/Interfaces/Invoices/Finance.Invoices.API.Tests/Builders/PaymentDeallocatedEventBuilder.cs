using Finance.Common.Enums;
using Finance.ServiceModel.Events.Payments;

namespace Finance.Invoices.API.Tests.Builders;

/// <summary>
/// Builds <see cref="PaymentDeallocatedEvent"/> instances for the invoice-side settlement mirror tests
/// (SDD-INV-001 §2.15, SDD-PAY-002 §2.10). Defaults produce a valid full release so a test overrides only the
/// field it exercises.
/// <para><c>OccurredAt</c> is the ORDERING TOKEN, so it is set from the same FIXED test clock as
/// <see cref="PaymentAllocatedEventBuilder.BaseInstant"/> — never <c>DateTimeOffset.UtcNow</c>.</para>
/// </summary>
public sealed class PaymentDeallocatedEventBuilder
{
    private Guid _messageId = Guid.NewGuid();
    private string _correlationId = "corr-dealloc-1";
    private DateTimeOffset _occurredAt = PaymentAllocatedEventBuilder.BaseInstant;
    private Guid _paymentId = Guid.NewGuid();
    private string _documentNumber = "RCT-2026-000001";
    private Guid _invoiceId = Guid.NewGuid();
    private decimal _releasedAmount = 100.00m;
    private decimal _invoiceSettledAmount;
    private SettlementStatus _invoiceSettlementStatus = SettlementStatus.Unsettled;

    /// <summary>Starts a new builder with valid defaults.</summary>
    /// <returns>A fresh builder.</returns>
    public static PaymentDeallocatedEventBuilder Create() => new();

    /// <summary>Sets the transport message id (the Redis dedupe key).</summary>
    /// <param name="messageId">The message id.</param>
    /// <returns>This builder.</returns>
    public PaymentDeallocatedEventBuilder WithMessageId(Guid messageId)
    {
        _messageId = messageId;
        return this;
    }

    /// <summary>Sets the correlation id carried onto the audit row and the log scope.</summary>
    /// <param name="correlationId">The correlation id.</param>
    /// <returns>This builder.</returns>
    public PaymentDeallocatedEventBuilder WithCorrelationId(string correlationId)
    {
        _correlationId = correlationId;
        return this;
    }

    /// <summary>Sets the ordering token the mirror compares against the stored applied token.</summary>
    /// <param name="occurredAt">The event instant.</param>
    /// <returns>This builder.</returns>
    public PaymentDeallocatedEventBuilder WithOccurredAt(DateTimeOffset occurredAt)
    {
        _occurredAt = occurredAt;
        return this;
    }

    /// <summary>Sets the invoice the amount is released from.</summary>
    /// <param name="invoiceId">The invoice id.</param>
    /// <returns>This builder.</returns>
    public PaymentDeallocatedEventBuilder WithInvoiceId(Guid invoiceId)
    {
        _invoiceId = invoiceId;
        return this;
    }

    /// <summary>Sets the releasing payment's identifier and document number.</summary>
    /// <param name="paymentId">The payment id.</param>
    /// <param name="documentNumber">The payment's gapless document number.</param>
    /// <returns>This builder.</returns>
    public PaymentDeallocatedEventBuilder WithPayment(Guid paymentId, string documentNumber)
    {
        _paymentId = paymentId;
        _documentNumber = documentNumber;
        return this;
    }

    /// <summary>
    /// Sets the released amount and the ABSOLUTE authoritative settled amount that remains, together with the
    /// publisher's derived status.
    /// </summary>
    /// <param name="releasedAmount">The amount this release returned to the payment.</param>
    /// <param name="invoiceSettledAmount">The invoice's absolute settled amount after the release.</param>
    /// <param name="reportedStatus">The status the publishing service derived.</param>
    /// <returns>This builder.</returns>
    public PaymentDeallocatedEventBuilder WithSettlement(
        decimal releasedAmount,
        decimal invoiceSettledAmount,
        SettlementStatus reportedStatus)
    {
        _releasedAmount = releasedAmount;
        _invoiceSettledAmount = invoiceSettledAmount;
        _invoiceSettlementStatus = reportedStatus;
        return this;
    }

    /// <summary>Materializes the configured deallocation event.</summary>
    /// <returns>The built <see cref="PaymentDeallocatedEvent"/>.</returns>
    public PaymentDeallocatedEvent Build() => new()
    {
        MessageId = _messageId,
        CorrelationId = _correlationId,
        OccurredAt = _occurredAt,
        PaymentId = _paymentId,
        DocumentNumber = _documentNumber,
        InvoiceId = _invoiceId,
        ReleasedAmount = _releasedAmount,
        BaseReleasedAmount = _releasedAmount,
        InvoiceSettledAmount = _invoiceSettledAmount,
        InvoiceSettlementStatus = _invoiceSettlementStatus,
        DeallocatedAt = _occurredAt
    };
}
