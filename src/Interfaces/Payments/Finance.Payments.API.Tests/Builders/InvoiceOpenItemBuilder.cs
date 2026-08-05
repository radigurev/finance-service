using Finance.Common.Enums;
using Finance.Payments.API.Tests.Fixtures;
using Finance.Payments.DBModel.Models;

namespace Finance.Payments.API.Tests.Builders;

/// <summary>
/// Builds <see cref="InvoiceOpenItem"/> projection rows for tests. Seeding the row DIRECTLY is required for the
/// SDD-PAY-002 §2.5 rule-10 control-account case (no consumer ever projects a credit note) and for the SDD-PAY-003
/// aging fixtures, which assert over the projection rather than over the invoice events that feed it.
/// </summary>
public sealed class InvoiceOpenItemBuilder
{
    /// <summary>The default issue date every row carries.</summary>
    public static readonly DateTimeOffset DefaultIssueDate = new(2026, 5, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>The default due date every row carries.</summary>
    public static readonly DateTimeOffset DefaultDueDate = new(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);

    private Guid _invoiceId = Guid.NewGuid();
    private string _documentNumber = "SINV-2026-000001";
    private string _documentType = nameof(InvoiceDocumentType.SaleInvoice);
    private string _direction = nameof(InvoiceDirection.AR);
    private Guid _counterpartyId = CreatePaymentRequestBuilder.DefaultCounterpartyId;
    private string _currencyCode = FakePaymentCountryStrategy.BaseCurrency;
    private string _baseCurrencyCode = FakePaymentCountryStrategy.BaseCurrency;
    private decimal _grossTotal = 1000.00m;
    private decimal _bookingExchangeRate = 1.000000m;
    private DateTimeOffset _issueDate = DefaultIssueDate;
    private DateTimeOffset _dueDate = DefaultDueDate;
    private string _invoiceStatus = nameof(InvoiceStatus.Confirmed);
    private decimal _settledAmount;

    /// <summary>Creates a builder pre-loaded with a confirmed, unsettled, base-currency sale invoice.</summary>
    /// <returns>A new builder.</returns>
    public static InvoiceOpenItemBuilder Create() => new();

    /// <summary>Sets the mirrored invoice identifier, which is also the primary key.</summary>
    /// <param name="invoiceId">The invoice identifier.</param>
    /// <returns>The builder.</returns>
    public InvoiceOpenItemBuilder WithInvoiceId(Guid invoiceId)
    {
        _invoiceId = invoiceId;
        return this;
    }

    /// <summary>Sets the invoice document number.</summary>
    /// <param name="documentNumber">The document number.</param>
    /// <returns>The builder.</returns>
    public InvoiceOpenItemBuilder WithDocumentNumber(string documentNumber)
    {
        _documentNumber = documentNumber;
        return this;
    }

    /// <summary>Sets the invoice document type name and derives the direction the shipped map assigns it.</summary>
    /// <param name="documentType">The invoice document type.</param>
    /// <returns>The builder.</returns>
    public InvoiceOpenItemBuilder WithDocumentType(InvoiceDocumentType documentType)
    {
        _documentType = documentType.ToString();
        _direction = DirectionFor(documentType).ToString();
        return this;
    }

    /// <summary>Overrides the mirrored direction name independently of the document type.</summary>
    /// <param name="direction">The direction.</param>
    /// <returns>The builder.</returns>
    public InvoiceOpenItemBuilder WithDirection(InvoiceDirection direction)
    {
        _direction = direction.ToString();
        return this;
    }

    /// <summary>Sets the counterparty reference.</summary>
    /// <param name="counterpartyId">The counterparty identifier.</param>
    /// <returns>The builder.</returns>
    public InvoiceOpenItemBuilder WithCounterpartyId(Guid counterpartyId)
    {
        _counterpartyId = counterpartyId;
        return this;
    }

    /// <summary>Sets the transactional currency code.</summary>
    /// <param name="currencyCode">The currency code.</param>
    /// <returns>The builder.</returns>
    public InvoiceOpenItemBuilder WithCurrencyCode(string currencyCode)
    {
        _currencyCode = currencyCode;
        return this;
    }

    /// <summary>Sets the base currency the invoice books in.</summary>
    /// <param name="baseCurrencyCode">The base currency code.</param>
    /// <returns>The builder.</returns>
    public InvoiceOpenItemBuilder WithBaseCurrencyCode(string baseCurrencyCode)
    {
        _baseCurrencyCode = baseCurrencyCode;
        return this;
    }

    /// <summary>Sets the invoice gross total.</summary>
    /// <param name="grossTotal">The gross total.</param>
    /// <returns>The builder.</returns>
    public InvoiceOpenItemBuilder WithGrossTotal(decimal grossTotal)
    {
        _grossTotal = grossTotal;
        return this;
    }

    /// <summary>Sets the rate the invoice froze when it was booked.</summary>
    /// <param name="bookingExchangeRate">The booking rate.</param>
    /// <returns>The builder.</returns>
    public InvoiceOpenItemBuilder WithBookingExchangeRate(decimal bookingExchangeRate)
    {
        _bookingExchangeRate = bookingExchangeRate;
        return this;
    }

    /// <summary>Sets the invoice issue date.</summary>
    /// <param name="issueDate">The issue date.</param>
    /// <returns>The builder.</returns>
    public InvoiceOpenItemBuilder WithIssueDate(DateTimeOffset issueDate)
    {
        _issueDate = issueDate;
        return this;
    }

    /// <summary>Sets the invoice payment due date — the aging bucket key.</summary>
    /// <param name="dueDate">The due date.</param>
    /// <returns>The builder.</returns>
    public InvoiceOpenItemBuilder WithDueDate(DateTimeOffset dueDate)
    {
        _dueDate = dueDate;
        return this;
    }

    /// <summary>Sets the mirrored invoice lifecycle status name.</summary>
    /// <param name="invoiceStatus">The invoice status.</param>
    /// <returns>The builder.</returns>
    public InvoiceOpenItemBuilder WithInvoiceStatus(InvoiceStatus invoiceStatus)
    {
        _invoiceStatus = invoiceStatus.ToString();
        return this;
    }

    /// <summary>Sets the locally-owned settled amount.</summary>
    /// <param name="settledAmount">The settled amount.</param>
    /// <returns>The builder.</returns>
    public InvoiceOpenItemBuilder WithSettledAmount(decimal settledAmount)
    {
        _settledAmount = settledAmount;
        return this;
    }

    /// <summary>Materializes the projection row.</summary>
    /// <returns>The built open item.</returns>
    public InvoiceOpenItem Build() => new()
    {
        InvoiceId = _invoiceId,
        DocumentNumber = _documentNumber,
        DocumentType = _documentType,
        Direction = _direction,
        CounterpartyId = _counterpartyId,
        CurrencyCode = _currencyCode,
        BaseCurrencyCode = _baseCurrencyCode,
        GrossTotal = _grossTotal,
        BookingExchangeRate = _bookingExchangeRate,
        IssueDate = _issueDate,
        DueDate = _dueDate,
        InvoiceStatus = _invoiceStatus,
        SettledAmount = _settledAmount,
        LastAppliedAt = FixedTimeProvider.DefaultNow
    };

    /// <summary>
    /// Mirrors the shipped <c>InvoiceDocumentTypeMap.DirectionFor</c> classification: a sale invoice and a debit
    /// note are <c>AR</c>; a purchase invoice and a credit note are <c>AP</c>.
    /// </summary>
    /// <param name="documentType">The invoice document type.</param>
    /// <returns>The direction the shipped map assigns.</returns>
    private static InvoiceDirection DirectionFor(InvoiceDocumentType documentType) => documentType switch
    {
        InvoiceDocumentType.SaleInvoice => InvoiceDirection.AR,
        InvoiceDocumentType.DebitNote => InvoiceDirection.AR,
        _ => InvoiceDirection.AP
    };
}
