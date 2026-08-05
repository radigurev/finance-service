using Finance.Common.Enums;
using Finance.Payments.API.Services;
using Finance.Payments.API.Tests.Fixtures;
using Finance.Payments.DBModel.Models;

namespace Finance.Payments.API.Tests.Builders;

/// <summary>
/// Builds <see cref="Payment"/> entities for tests that must seed a state the v1 endpoints cannot reach — the
/// UNREACHABLE defense-in-depth branches SDD-PAY-001 §2.5/§3.2 and SDD-PAY-002 §2.6 deliberately retain
/// (<c>Confirmed</c> AND linked, an allocated <c>Cancelled</c> payment) — and for the SDD-PAY-003 aging fixtures
/// that need payment rows without exercising the lifecycle.
/// </summary>
public sealed class PaymentBuilder
{
    private Guid _id = Guid.NewGuid();
    private string? _documentNumber;
    private bool _documentNumberSet;
    private PaymentDocumentType _documentType = PaymentDocumentType.CustomerReceipt;
    private PaymentMethod _method = PaymentMethod.BankTransfer;
    private PaymentStatus _status = PaymentStatus.Confirmed;
    private Guid _counterpartyId = CreatePaymentRequestBuilder.DefaultCounterpartyId;
    private string _currencyCode = FakePaymentCountryStrategy.BaseCurrency;
    private string _baseCurrencyCode = FakePaymentCountryStrategy.BaseCurrency;
    private decimal _amount = 1000.00m;
    private decimal _exchangeRate = 1.000000m;
    private decimal _baseAmount = 1000.00m;
    private decimal _allocatedAmount;
    private int _settlementAccountId = 503;
    private DateTimeOffset _paymentDate = CreatePaymentRequestBuilder.DefaultPaymentDate;
    private Guid? _journalEntryId;
    private string _correlationId = StubCorrelationIdAccessor.DefaultCorrelationId;

    /// <summary>Creates a builder pre-loaded with a confirmed, unlinked, unallocated base-currency receipt.</summary>
    /// <returns>A new builder.</returns>
    public static PaymentBuilder Create() => new();

    /// <summary>Sets the identifier.</summary>
    /// <param name="id">The payment identifier.</param>
    /// <returns>The builder.</returns>
    public PaymentBuilder WithId(Guid id)
    {
        _id = id;
        return this;
    }

    /// <summary>Sets the gapless document number, or clears it for a draft.</summary>
    /// <param name="documentNumber">The document number, or <c>null</c>.</param>
    /// <returns>The builder.</returns>
    public PaymentBuilder WithDocumentNumber(string? documentNumber)
    {
        _documentNumber = documentNumber;
        _documentNumberSet = true;
        return this;
    }

    /// <summary>Sets the document type and derives the frozen direction from it.</summary>
    /// <param name="documentType">The document type.</param>
    /// <returns>The builder.</returns>
    public PaymentBuilder WithDocumentType(PaymentDocumentType documentType)
    {
        _documentType = documentType;
        return this;
    }

    /// <summary>Sets how the cash moved.</summary>
    /// <param name="method">The payment method.</param>
    /// <returns>The builder.</returns>
    public PaymentBuilder WithMethod(PaymentMethod method)
    {
        _method = method;
        return this;
    }

    /// <summary>Sets the lifecycle state directly, bypassing the workflow engine.</summary>
    /// <param name="status">The status.</param>
    /// <returns>The builder.</returns>
    public PaymentBuilder WithStatus(PaymentStatus status)
    {
        _status = status;
        return this;
    }

    /// <summary>Sets the counterparty reference.</summary>
    /// <param name="counterpartyId">The counterparty identifier.</param>
    /// <returns>The builder.</returns>
    public PaymentBuilder WithCounterpartyId(Guid counterpartyId)
    {
        _counterpartyId = counterpartyId;
        return this;
    }

    /// <summary>Sets the transactional currency and, when it is the base currency, keeps the rate at one.</summary>
    /// <param name="currencyCode">The transactional currency code.</param>
    /// <returns>The builder.</returns>
    public PaymentBuilder WithCurrencyCode(string currencyCode)
    {
        _currencyCode = currencyCode;
        return this;
    }

    /// <summary>Sets the base currency the payment reports in.</summary>
    /// <param name="baseCurrencyCode">The base currency code.</param>
    /// <returns>The builder.</returns>
    public PaymentBuilder WithBaseCurrencyCode(string baseCurrencyCode)
    {
        _baseCurrencyCode = baseCurrencyCode;
        return this;
    }

    /// <summary>Sets the transactional amount and recomputes the base amount at the current rate.</summary>
    /// <param name="amount">The amount.</param>
    /// <returns>The builder.</returns>
    public PaymentBuilder WithAmount(decimal amount)
    {
        _amount = amount;
        _baseAmount = decimal.Round(amount * _exchangeRate, 2, MidpointRounding.AwayFromZero);
        return this;
    }

    /// <summary>Sets the exchange rate and recomputes the base amount.</summary>
    /// <param name="exchangeRate">The rate.</param>
    /// <returns>The builder.</returns>
    public PaymentBuilder WithExchangeRate(decimal exchangeRate)
    {
        _exchangeRate = exchangeRate;
        _baseAmount = decimal.Round(_amount * exchangeRate, 2, MidpointRounding.AwayFromZero);
        return this;
    }

    /// <summary>Sets the already-matched amount (the SDD-PAY-002 carve-out column).</summary>
    /// <param name="allocatedAmount">The allocated amount.</param>
    /// <returns>The builder.</returns>
    public PaymentBuilder WithAllocatedAmount(decimal allocatedAmount)
    {
        _allocatedAmount = allocatedAmount;
        return this;
    }

    /// <summary>Sets the cash/bank GL settlement account.</summary>
    /// <param name="settlementAccountId">The surrogate account identifier.</param>
    /// <returns>The builder.</returns>
    public PaymentBuilder WithSettlementAccountId(int settlementAccountId)
    {
        _settlementAccountId = settlementAccountId;
        return this;
    }

    /// <summary>Sets the date the cash moved.</summary>
    /// <param name="paymentDate">The payment date.</param>
    /// <returns>The builder.</returns>
    public PaymentBuilder WithPaymentDate(DateTimeOffset paymentDate)
    {
        _paymentDate = paymentDate;
        return this;
    }

    /// <summary>Links a posted journal entry — the state the v1 paths never persist while <c>Confirmed</c>.</summary>
    /// <param name="journalEntryId">The journal entry identifier.</param>
    /// <returns>The builder.</returns>
    public PaymentBuilder WithJournalEntryId(Guid? journalEntryId)
    {
        _journalEntryId = journalEntryId;
        return this;
    }

    /// <summary>Sets the stored correlation identifier.</summary>
    /// <param name="correlationId">The correlation identifier.</param>
    /// <returns>The builder.</returns>
    public PaymentBuilder WithCorrelationId(string correlationId)
    {
        _correlationId = correlationId;
        return this;
    }

    /// <summary>Materializes the entity.</summary>
    /// <returns>The built payment.</returns>
    public Payment Build() => new()
    {
        Id = _id,
        DocumentNumber = _documentNumberSet ? _documentNumber : DerivedDocumentNumber(),
        DocumentType = _documentType,
        Direction = PaymentDocumentTypeMap.DirectionFor(_documentType),
        Method = _method,
        Status = _status,
        CounterpartyId = _counterpartyId,
        CurrencyCode = _currencyCode,
        BaseCurrencyCode = _baseCurrencyCode,
        Amount = _amount,
        ExchangeRate = _exchangeRate,
        BaseAmount = _baseAmount,
        AllocatedAmount = _allocatedAmount,
        SettlementAccountId = _settlementAccountId,
        PaymentDate = _paymentDate,
        JournalEntryId = _journalEntryId,
        CorrelationId = _correlationId,
        CreatedAt = FixedTimeProvider.DefaultNow,
        CreatedBy = StubCurrentUserAccessor.TestUserId,
        ConfirmedAt = _status == PaymentStatus.Draft ? null : FixedTimeProvider.DefaultNow,
        ConfirmedBy = _status == PaymentStatus.Draft ? null : StubCurrentUserAccessor.TestUserId,
        PostedAt = _status is PaymentStatus.Posted or PaymentStatus.Reversed
            ? FixedTimeProvider.DefaultNow
            : null,
        ReversedAt = _status == PaymentStatus.Reversed ? FixedTimeProvider.DefaultNow : null
    };

    /// <summary>
    /// Derives a document number from the payment's own identifier so seeding several payments in one test cannot
    /// collide on the UNIQUE filtered index <c>IX_Payments_DocumentNumber</c>. It is deterministic for a given
    /// identifier and is never asserted verbatim — a test that cares sets the number explicitly.
    /// </summary>
    /// <returns>The derived document number.</returns>
    private string DerivedDocumentNumber()
    {
        string prefix = _documentType == PaymentDocumentType.SupplierPayment ? "PAY" : "RCT";
        return $"{prefix}-2026-{_id.ToString("N")[..20]}";
    }
}
