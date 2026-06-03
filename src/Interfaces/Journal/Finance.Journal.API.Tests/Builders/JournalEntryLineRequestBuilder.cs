using Finance.ServiceModel.Journal;

namespace Finance.Journal.API.Tests.Builders;

/// <summary>
/// Fluent builder for <see cref="JournalEntryLineRequest"/> test data (SDD-FIN-001 §2.2). Defaults to a
/// valid base-currency (<c>BGN</c>) debit line of <c>100.00</c> with a rate of <c>1.000000</c> and a
/// reconciling base amount; tests override the side, amounts, currency, rate, or account as needed.
/// </summary>
public sealed class JournalEntryLineRequestBuilder
{
    private int _accountId = 1;
    private decimal _debitAmount;
    private decimal _creditAmount;
    private string _currencyCode = "BGN";
    private decimal _exchangeRate = 1.000000m;
    private decimal _baseDebitAmount;
    private decimal _baseCreditAmount;

    private JournalEntryLineRequestBuilder()
    {
    }

    /// <summary>Starts a new builder seeded with a valid base-currency debit line of 100.00.</summary>
    /// <returns>A new builder instance.</returns>
    public static JournalEntryLineRequestBuilder Create()
    {
        return new JournalEntryLineRequestBuilder()
            .AsDebit(100.00m);
    }

    /// <summary>Sets the posting-target account id.</summary>
    /// <param name="accountId">The account id.</param>
    /// <returns>The same builder.</returns>
    public JournalEntryLineRequestBuilder WithAccountId(int accountId)
    {
        _accountId = accountId;
        return this;
    }

    /// <summary>Configures the line as a debit of the supplied amount with a matching base amount.</summary>
    /// <param name="amount">The transactional debit amount.</param>
    /// <returns>The same builder.</returns>
    public JournalEntryLineRequestBuilder AsDebit(decimal amount)
    {
        _debitAmount = amount;
        _creditAmount = 0m;
        _baseDebitAmount = decimal.Round(amount * _exchangeRate, 2, MidpointRounding.AwayFromZero);
        _baseCreditAmount = 0m;
        return this;
    }

    /// <summary>Configures the line as a credit of the supplied amount with a matching base amount.</summary>
    /// <param name="amount">The transactional credit amount.</param>
    /// <returns>The same builder.</returns>
    public JournalEntryLineRequestBuilder AsCredit(decimal amount)
    {
        _creditAmount = amount;
        _debitAmount = 0m;
        _baseCreditAmount = decimal.Round(amount * _exchangeRate, 2, MidpointRounding.AwayFromZero);
        _baseDebitAmount = 0m;
        return this;
    }

    /// <summary>Sets the transactional currency code without recomputing base amounts.</summary>
    /// <param name="currencyCode">The ISO 4217 currency code.</param>
    /// <returns>The same builder.</returns>
    public JournalEntryLineRequestBuilder WithCurrency(string currencyCode)
    {
        _currencyCode = currencyCode;
        return this;
    }

    /// <summary>Sets the exchange rate without recomputing base amounts.</summary>
    /// <param name="exchangeRate">The line-to-base exchange rate.</param>
    /// <returns>The same builder.</returns>
    public JournalEntryLineRequestBuilder WithExchangeRate(decimal exchangeRate)
    {
        _exchangeRate = exchangeRate;
        return this;
    }

    /// <summary>Overrides the raw debit/credit amounts directly (for shape-violation cases).</summary>
    /// <param name="debitAmount">The transactional debit amount.</param>
    /// <param name="creditAmount">The transactional credit amount.</param>
    /// <returns>The same builder.</returns>
    public JournalEntryLineRequestBuilder WithRawAmounts(decimal debitAmount, decimal creditAmount)
    {
        _debitAmount = debitAmount;
        _creditAmount = creditAmount;
        return this;
    }

    /// <summary>Overrides the base-currency amounts directly (for reconciliation/balance cases).</summary>
    /// <param name="baseDebitAmount">The base-currency debit amount.</param>
    /// <param name="baseCreditAmount">The base-currency credit amount.</param>
    /// <returns>The same builder.</returns>
    public JournalEntryLineRequestBuilder WithBaseAmounts(decimal baseDebitAmount, decimal baseCreditAmount)
    {
        _baseDebitAmount = baseDebitAmount;
        _baseCreditAmount = baseCreditAmount;
        return this;
    }

    /// <summary>Materializes the configured <see cref="JournalEntryLineRequest"/>.</summary>
    /// <returns>The built line request.</returns>
    public JournalEntryLineRequest Build()
    {
        return new JournalEntryLineRequest
        {
            AccountId = _accountId,
            DebitAmount = _debitAmount,
            CreditAmount = _creditAmount,
            CurrencyCode = _currencyCode,
            ExchangeRate = _exchangeRate,
            BaseDebitAmount = _baseDebitAmount,
            BaseCreditAmount = _baseCreditAmount,
            Description = null
        };
    }
}
