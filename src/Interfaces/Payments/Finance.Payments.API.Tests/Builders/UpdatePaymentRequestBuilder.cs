using Finance.Common.Enums;
using Finance.Payments.API.Tests.Fixtures;
using Finance.ServiceModel.Payments;

namespace Finance.Payments.API.Tests.Builders;

/// <summary>
/// Builds valid <see cref="UpdatePaymentRequest"/> instances for the Payments unit tests. The defaults mirror
/// <see cref="CreatePaymentRequestBuilder"/> — a base-currency customer receipt whose payment date falls in the
/// <see cref="FixedTimeProvider.DefaultNow"/> year — plus a well-formed base64 <c>RowVersion</c> token, so the
/// request passes every SDD-PAY-001 §3.1 update rule; a test overrides only the field it is exercising.
/// </summary>
public sealed class UpdatePaymentRequestBuilder
{
    /// <summary>A well-formed base64 <c>rowversion</c> token of the eight bytes SQL Server emits.</summary>
    public static readonly string DefaultRowVersion =
        Convert.ToBase64String(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });

    private PaymentDocumentType _documentType = PaymentDocumentType.CustomerReceipt;
    private PaymentMethod _method = PaymentMethod.BankTransfer;
    private Guid _counterpartyId = CreatePaymentRequestBuilder.DefaultCounterpartyId;
    private string _currencyCode = FakePaymentCountryStrategy.BaseCurrency;
    private decimal _amount = 1000.00m;
    private decimal _exchangeRate = 1.000000m;
    private int _settlementAccountId = 503;
    private DateTimeOffset _paymentDate = CreatePaymentRequestBuilder.DefaultPaymentDate;
    private string? _bankReference = "REF-0001";
    private string _rowVersion = DefaultRowVersion;

    /// <summary>Creates a builder pre-loaded with valid defaults.</summary>
    /// <returns>A new builder.</returns>
    public static UpdatePaymentRequestBuilder Create() => new();

    /// <summary>Sets the document type, which the service asserts is unchanged from the persisted draft.</summary>
    /// <param name="documentType">The document type.</param>
    /// <returns>The builder.</returns>
    public UpdatePaymentRequestBuilder WithDocumentType(PaymentDocumentType documentType)
    {
        _documentType = documentType;
        return this;
    }

    /// <summary>Sets how the cash moved.</summary>
    /// <param name="method">The payment method.</param>
    /// <returns>The builder.</returns>
    public UpdatePaymentRequestBuilder WithMethod(PaymentMethod method)
    {
        _method = method;
        return this;
    }

    /// <summary>Sets the Warehouse-owned counterparty reference.</summary>
    /// <param name="counterpartyId">The counterparty identifier.</param>
    /// <returns>The builder.</returns>
    public UpdatePaymentRequestBuilder WithCounterpartyId(Guid counterpartyId)
    {
        _counterpartyId = counterpartyId;
        return this;
    }

    /// <summary>Sets the transactional currency code.</summary>
    /// <param name="currencyCode">The ISO 4217 alphabetic code.</param>
    /// <returns>The builder.</returns>
    public UpdatePaymentRequestBuilder WithCurrencyCode(string currencyCode)
    {
        _currencyCode = currencyCode;
        return this;
    }

    /// <summary>Sets the transactional cash amount.</summary>
    /// <param name="amount">The amount.</param>
    /// <returns>The builder.</returns>
    public UpdatePaymentRequestBuilder WithAmount(decimal amount)
    {
        _amount = amount;
        return this;
    }

    /// <summary>Sets the exchange rate at the payment date.</summary>
    /// <param name="exchangeRate">The rate.</param>
    /// <returns>The builder.</returns>
    public UpdatePaymentRequestBuilder WithExchangeRate(decimal exchangeRate)
    {
        _exchangeRate = exchangeRate;
        return this;
    }

    /// <summary>Sets the cash/bank GL settlement account.</summary>
    /// <param name="settlementAccountId">The surrogate account identifier.</param>
    /// <returns>The builder.</returns>
    public UpdatePaymentRequestBuilder WithSettlementAccountId(int settlementAccountId)
    {
        _settlementAccountId = settlementAccountId;
        return this;
    }

    /// <summary>Sets the date the cash moved.</summary>
    /// <param name="paymentDate">The payment date.</param>
    /// <returns>The builder.</returns>
    public UpdatePaymentRequestBuilder WithPaymentDate(DateTimeOffset paymentDate)
    {
        _paymentDate = paymentDate;
        return this;
    }

    /// <summary>Sets the optional operator-supplied bank reference.</summary>
    /// <param name="bankReference">The bank reference, or <c>null</c>.</param>
    /// <returns>The builder.</returns>
    public UpdatePaymentRequestBuilder WithBankReference(string? bankReference)
    {
        _bankReference = bankReference;
        return this;
    }

    /// <summary>Sets the base64 optimistic-concurrency token.</summary>
    /// <param name="rowVersion">The base64 <c>rowversion</c> token.</param>
    /// <returns>The builder.</returns>
    public UpdatePaymentRequestBuilder WithRowVersion(string rowVersion)
    {
        _rowVersion = rowVersion;
        return this;
    }

    /// <summary>Materializes the request.</summary>
    /// <returns>The built request.</returns>
    public UpdatePaymentRequest Build() => new()
    {
        DocumentType = _documentType,
        Method = _method,
        CounterpartyId = _counterpartyId,
        CurrencyCode = _currencyCode,
        Amount = _amount,
        ExchangeRate = _exchangeRate,
        SettlementAccountId = _settlementAccountId,
        PaymentDate = _paymentDate,
        BankReference = _bankReference,
        RowVersion = _rowVersion
    };
}
