using Finance.Common.Enums;
using Finance.Payments.API.Tests.Fixtures;
using Finance.ServiceModel.Events.Invoices;

namespace Finance.Payments.API.Tests.Builders;

/// <summary>
/// Builds <see cref="InvoiceConfirmedEvent"/> messages for the SDD-PAY-002 §2.3 projection-consumer tests. The
/// defaults describe a confirmed base-currency sale invoice that the admission predicate ACCEPTS; a test overrides
/// the document type to exercise the silent credit-note skip, or clears the optional due date and booking rate to
/// exercise the documented fallbacks.
/// </summary>
public sealed class InvoiceConfirmedEventBuilder
{
    private Guid _messageId = Guid.NewGuid();
    private string _correlationId = StubCorrelationIdAccessor.DefaultCorrelationId;
    private DateTimeOffset _occurredAt = FixedTimeProvider.DefaultNow;
    private Guid _invoiceId = Guid.NewGuid();
    private string _documentNumber = "SINV-2026-000001";
    private InvoiceDocumentType _documentType = InvoiceDocumentType.SaleInvoice;
    private InvoiceDirection _direction = InvoiceDirection.AR;
    private Guid _counterpartyId = CreatePaymentRequestBuilder.DefaultCounterpartyId;
    private string _currencyCode = FakePaymentCountryStrategy.BaseCurrency;
    private string _baseCurrencyCode = FakePaymentCountryStrategy.BaseCurrency;
    private DateTimeOffset _issueDate = InvoiceOpenItemBuilder.DefaultIssueDate;
    private string _postingRuleKey = "SALE_INVOICE";
    private decimal _netTotal = 833.33m;
    private decimal _taxTotal = 166.67m;
    private decimal _grossTotal = 1000.00m;
    private DateTimeOffset? _dueDate = InvoiceOpenItemBuilder.DefaultDueDate;
    private decimal? _bookingExchangeRate = 1.000000m;

    /// <summary>Creates a builder pre-loaded with a settleable, confirmed sale invoice.</summary>
    /// <returns>A new builder.</returns>
    public static InvoiceConfirmedEventBuilder Create() => new();

    /// <summary>Sets the transport message identifier.</summary>
    /// <param name="messageId">The message identifier.</param>
    /// <returns>The builder.</returns>
    public InvoiceConfirmedEventBuilder WithMessageId(Guid messageId)
    {
        _messageId = messageId;
        return this;
    }

    /// <summary>Sets the correlation identifier.</summary>
    /// <param name="correlationId">The correlation identifier.</param>
    /// <returns>The builder.</returns>
    public InvoiceConfirmedEventBuilder WithCorrelationId(string correlationId)
    {
        _correlationId = correlationId;
        return this;
    }

    /// <summary>Sets the event's ordering timestamp.</summary>
    /// <param name="occurredAt">The moment the confirmation occurred.</param>
    /// <returns>The builder.</returns>
    public InvoiceConfirmedEventBuilder WithOccurredAt(DateTimeOffset occurredAt)
    {
        _occurredAt = occurredAt;
        return this;
    }

    /// <summary>Sets the invoice identifier the projection keys on.</summary>
    /// <param name="invoiceId">The invoice identifier.</param>
    /// <returns>The builder.</returns>
    public InvoiceConfirmedEventBuilder WithInvoiceId(Guid invoiceId)
    {
        _invoiceId = invoiceId;
        return this;
    }

    /// <summary>Sets the invoice document number.</summary>
    /// <param name="documentNumber">The document number.</param>
    /// <returns>The builder.</returns>
    public InvoiceConfirmedEventBuilder WithDocumentNumber(string documentNumber)
    {
        _documentNumber = documentNumber;
        return this;
    }

    /// <summary>Sets the document type and the direction the shipped map assigns it.</summary>
    /// <param name="documentType">The invoice document type.</param>
    /// <returns>The builder.</returns>
    public InvoiceConfirmedEventBuilder WithDocumentType(InvoiceDocumentType documentType)
    {
        _documentType = documentType;
        _direction = documentType is InvoiceDocumentType.SaleInvoice or InvoiceDocumentType.DebitNote
            ? InvoiceDirection.AR
            : InvoiceDirection.AP;
        return this;
    }

    /// <summary>Sets the counterparty reference.</summary>
    /// <param name="counterpartyId">The counterparty identifier.</param>
    /// <returns>The builder.</returns>
    public InvoiceConfirmedEventBuilder WithCounterpartyId(Guid counterpartyId)
    {
        _counterpartyId = counterpartyId;
        return this;
    }

    /// <summary>Sets the transactional currency code.</summary>
    /// <param name="currencyCode">The currency code.</param>
    /// <returns>The builder.</returns>
    public InvoiceConfirmedEventBuilder WithCurrencyCode(string currencyCode)
    {
        _currencyCode = currencyCode;
        return this;
    }

    /// <summary>Sets the gross total and splits it into a net and tax portion.</summary>
    /// <param name="grossTotal">The gross total.</param>
    /// <returns>The builder.</returns>
    public InvoiceConfirmedEventBuilder WithGrossTotal(decimal grossTotal)
    {
        _grossTotal = grossTotal;
        _taxTotal = decimal.Round(grossTotal / 6m, 2, MidpointRounding.AwayFromZero);
        _netTotal = grossTotal - _taxTotal;
        return this;
    }

    /// <summary>Sets the invoice issue date.</summary>
    /// <param name="issueDate">The issue date.</param>
    /// <returns>The builder.</returns>
    public InvoiceConfirmedEventBuilder WithIssueDate(DateTimeOffset issueDate)
    {
        _issueDate = issueDate;
        return this;
    }

    /// <summary>Sets the OPTIONAL due date, or clears it to exercise the issue-date fallback.</summary>
    /// <param name="dueDate">The due date, or <c>null</c>.</param>
    /// <returns>The builder.</returns>
    public InvoiceConfirmedEventBuilder WithDueDate(DateTimeOffset? dueDate)
    {
        _dueDate = dueDate;
        return this;
    }

    /// <summary>Sets the OPTIONAL booking rate, or clears it to exercise the rate-one fallback.</summary>
    /// <param name="bookingExchangeRate">The booking rate, or <c>null</c>.</param>
    /// <returns>The builder.</returns>
    public InvoiceConfirmedEventBuilder WithBookingExchangeRate(decimal? bookingExchangeRate)
    {
        _bookingExchangeRate = bookingExchangeRate;
        return this;
    }

    /// <summary>Materializes the event.</summary>
    /// <returns>The built event.</returns>
    public InvoiceConfirmedEvent Build() => new()
    {
        MessageId = _messageId,
        CorrelationId = _correlationId,
        OccurredAt = _occurredAt,
        InvoiceId = _invoiceId,
        DocumentNumber = _documentNumber,
        DocumentType = _documentType,
        Direction = _direction,
        CounterpartyId = _counterpartyId,
        CurrencyCode = _currencyCode,
        BaseCurrencyCode = _baseCurrencyCode,
        IssueDate = _issueDate,
        PostingRuleKey = _postingRuleKey,
        NetTotal = _netTotal,
        TaxTotal = _taxTotal,
        GrossTotal = _grossTotal,
        DueDate = _dueDate,
        BookingExchangeRate = _bookingExchangeRate
    };
}
