using Finance.Common.Enums;
using Finance.Payments.API.Tests.Fixtures;
using Finance.ServiceModel.Payments;

namespace Finance.Payments.API.Tests.Builders;

/// <summary>
/// Builds valid <see cref="CreatePaymentRequest"/> instances for the Payments unit tests. The defaults produce a
/// base-currency customer receipt whose payment date falls in the <see cref="FixedTimeProvider.DefaultNow"/> year,
/// so it passes every SDD-PAY-001 §3.1 field rule and the §2.2 confirm-clock-year guard; a test overrides only the
/// field it is exercising.
/// </summary>
public sealed class CreatePaymentRequestBuilder
{
    /// <summary>The default payment date every request carries: a past day of the default clock's year.</summary>
    public static readonly DateTimeOffset DefaultPaymentDate = new(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>The default counterparty every request carries.</summary>
    public static readonly Guid DefaultCounterpartyId = new("11111111-1111-1111-1111-111111111111");

    private PaymentDocumentType _documentType = PaymentDocumentType.CustomerReceipt;
    private PaymentMethod _method = PaymentMethod.BankTransfer;
    private Guid _counterpartyId = DefaultCounterpartyId;
    private string _currencyCode = FakePaymentCountryStrategy.BaseCurrency;
    private decimal _amount = 1000.00m;
    private decimal _exchangeRate = 1.000000m;
    private int _settlementAccountId = 503;
    private DateTimeOffset _paymentDate = DefaultPaymentDate;
    private string? _bankReference = "REF-0001";

    /// <summary>Creates a builder pre-loaded with valid defaults.</summary>
    /// <returns>A new builder.</returns>
    public static CreatePaymentRequestBuilder Create() => new();

    /// <summary>Sets the document type, which also derives the frozen direction.</summary>
    /// <param name="documentType">The document type.</param>
    /// <returns>The builder.</returns>
    public CreatePaymentRequestBuilder WithDocumentType(PaymentDocumentType documentType)
    {
        _documentType = documentType;
        return this;
    }

    /// <summary>Sets how the cash moved.</summary>
    /// <param name="method">The payment method.</param>
    /// <returns>The builder.</returns>
    public CreatePaymentRequestBuilder WithMethod(PaymentMethod method)
    {
        _method = method;
        return this;
    }

    /// <summary>Sets the Warehouse-owned counterparty reference.</summary>
    /// <param name="counterpartyId">The counterparty identifier.</param>
    /// <returns>The builder.</returns>
    public CreatePaymentRequestBuilder WithCounterpartyId(Guid counterpartyId)
    {
        _counterpartyId = counterpartyId;
        return this;
    }

    /// <summary>Sets the transactional currency code.</summary>
    /// <param name="currencyCode">The ISO 4217 alphabetic code.</param>
    /// <returns>The builder.</returns>
    public CreatePaymentRequestBuilder WithCurrencyCode(string currencyCode)
    {
        _currencyCode = currencyCode;
        return this;
    }

    /// <summary>Sets the transactional cash amount.</summary>
    /// <param name="amount">The amount.</param>
    /// <returns>The builder.</returns>
    public CreatePaymentRequestBuilder WithAmount(decimal amount)
    {
        _amount = amount;
        return this;
    }

    /// <summary>Sets the exchange rate at the payment date.</summary>
    /// <param name="exchangeRate">The rate.</param>
    /// <returns>The builder.</returns>
    public CreatePaymentRequestBuilder WithExchangeRate(decimal exchangeRate)
    {
        _exchangeRate = exchangeRate;
        return this;
    }

    /// <summary>Sets the cash/bank GL settlement account.</summary>
    /// <param name="settlementAccountId">The surrogate account identifier.</param>
    /// <returns>The builder.</returns>
    public CreatePaymentRequestBuilder WithSettlementAccountId(int settlementAccountId)
    {
        _settlementAccountId = settlementAccountId;
        return this;
    }

    /// <summary>Sets the date the cash moved.</summary>
    /// <param name="paymentDate">The payment date.</param>
    /// <returns>The builder.</returns>
    public CreatePaymentRequestBuilder WithPaymentDate(DateTimeOffset paymentDate)
    {
        _paymentDate = paymentDate;
        return this;
    }

    /// <summary>Sets the optional operator-supplied bank reference.</summary>
    /// <param name="bankReference">The bank reference, or <c>null</c>.</param>
    /// <returns>The builder.</returns>
    public CreatePaymentRequestBuilder WithBankReference(string? bankReference)
    {
        _bankReference = bankReference;
        return this;
    }

    /// <summary>Materializes the request.</summary>
    /// <returns>The built request.</returns>
    public CreatePaymentRequest Build() => new()
    {
        DocumentType = _documentType,
        Method = _method,
        CounterpartyId = _counterpartyId,
        CurrencyCode = _currencyCode,
        Amount = _amount,
        ExchangeRate = _exchangeRate,
        SettlementAccountId = _settlementAccountId,
        PaymentDate = _paymentDate,
        BankReference = _bankReference
    };
}
