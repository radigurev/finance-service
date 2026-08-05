using Finance.Common.Enums;
using Finance.ServiceModel.Events.Payments;

namespace Finance.Invoices.API.Tests.Builders;

/// <summary>
/// Builds <see cref="PaymentAllocatedEvent"/> instances for the invoice-side settlement mirror tests
/// (SDD-INV-001 §2.15, SDD-PAY-002 §2.10). Defaults produce a valid single-invoice allocation so a test
/// overrides only the field it exercises.
/// <para><c>OccurredAt</c> is the ORDERING TOKEN the mirror compares against
/// <c>Invoice.LastSettlementAppliedAt</c>, so it is set from a FIXED test clock — never
/// <c>DateTimeOffset.UtcNow</c> — and every out-of-order scenario states both timestamps explicitly.</para>
/// </summary>
public sealed class PaymentAllocatedEventBuilder
{
    /// <summary>The fixed base instant every allocation-event timestamp is derived from.</summary>
    public static readonly DateTimeOffset BaseInstant = new(2026, 3, 10, 12, 0, 0, TimeSpan.Zero);

    private Guid _messageId = Guid.NewGuid();
    private string _correlationId = "corr-alloc-1";
    private DateTimeOffset _occurredAt = BaseInstant;
    private Guid _paymentId = Guid.NewGuid();
    private string _documentNumber = "RCT-2026-000001";
    private Guid _invoiceId = Guid.NewGuid();
    private decimal _allocatedAmount = 100.00m;
    private decimal _invoiceSettledAmount = 100.00m;
    private SettlementStatus _invoiceSettlementStatus = SettlementStatus.PartiallySettled;

    /// <summary>Starts a new builder with valid defaults.</summary>
    /// <returns>A fresh builder.</returns>
    public static PaymentAllocatedEventBuilder Create() => new();

    /// <summary>Sets the transport message id (the Redis dedupe key).</summary>
    /// <param name="messageId">The message id.</param>
    /// <returns>This builder.</returns>
    public PaymentAllocatedEventBuilder WithMessageId(Guid messageId)
    {
        _messageId = messageId;
        return this;
    }

    /// <summary>Sets the correlation id carried onto the audit row and the log scope.</summary>
    /// <param name="correlationId">The correlation id.</param>
    /// <returns>This builder.</returns>
    public PaymentAllocatedEventBuilder WithCorrelationId(string correlationId)
    {
        _correlationId = correlationId;
        return this;
    }

    /// <summary>Sets the ordering token the mirror compares against the stored applied token.</summary>
    /// <param name="occurredAt">The event instant.</param>
    /// <returns>This builder.</returns>
    public PaymentAllocatedEventBuilder WithOccurredAt(DateTimeOffset occurredAt)
    {
        _occurredAt = occurredAt;
        return this;
    }

    /// <summary>Sets the invoice the allocation is matched against.</summary>
    /// <param name="invoiceId">The invoice id.</param>
    /// <returns>This builder.</returns>
    public PaymentAllocatedEventBuilder WithInvoiceId(Guid invoiceId)
    {
        _invoiceId = invoiceId;
        return this;
    }

    /// <summary>Sets the paying payment's identifier and document number.</summary>
    /// <param name="paymentId">The payment id.</param>
    /// <param name="documentNumber">The payment's gapless document number.</param>
    /// <returns>This builder.</returns>
    public PaymentAllocatedEventBuilder WithPayment(Guid paymentId, string documentNumber)
    {
        _paymentId = paymentId;
        _documentNumber = documentNumber;
        return this;
    }

    /// <summary>
    /// Sets the ABSOLUTE authoritative settled amount the event carries, together with the amount applied by
    /// this allocation and the publisher's derived status.
    /// </summary>
    /// <param name="allocatedAmount">The amount this allocation applied.</param>
    /// <param name="invoiceSettledAmount">The invoice's absolute settled amount after the allocation.</param>
    /// <param name="reportedStatus">The status the publishing service derived.</param>
    /// <returns>This builder.</returns>
    public PaymentAllocatedEventBuilder WithSettlement(
        decimal allocatedAmount,
        decimal invoiceSettledAmount,
        SettlementStatus reportedStatus)
    {
        _allocatedAmount = allocatedAmount;
        _invoiceSettledAmount = invoiceSettledAmount;
        _invoiceSettlementStatus = reportedStatus;
        return this;
    }

    /// <summary>Materializes the configured allocation event.</summary>
    /// <returns>The built <see cref="PaymentAllocatedEvent"/>.</returns>
    public PaymentAllocatedEvent Build() => new()
    {
        MessageId = _messageId,
        CorrelationId = _correlationId,
        OccurredAt = _occurredAt,
        PaymentId = _paymentId,
        DocumentNumber = _documentNumber,
        InvoiceId = _invoiceId,
        Direction = PaymentDirection.AR,
        CounterpartyId = new Guid("11111111-1111-1111-1111-111111111111"),
        CurrencyCode = "BGN",
        AllocatedAmount = _allocatedAmount,
        BaseAllocatedAmount = _allocatedAmount,
        RealizedFxDifference = 0.00m,
        InvoiceSettledAmount = _invoiceSettledAmount,
        InvoiceSettlementStatus = _invoiceSettlementStatus,
        AllocatedAt = _occurredAt
    };
}
