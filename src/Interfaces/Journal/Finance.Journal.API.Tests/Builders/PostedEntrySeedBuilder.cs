using Finance.Common.Enums;
using Finance.Journal.DBModel.Models;

namespace Finance.Journal.API.Tests.Builders;

/// <summary>
/// Fluent builder that materializes a <see cref="JournalEntry"/> with its lines directly for the General
/// Ledger / Trial Balance read tests (SDD-FIN-003 §6). Because the GL is a pure read aggregation over
/// persisted <c>Posted</c> / <c>Draft</c> / <c>Reversed</c> rows, the tests seed entities straight into the
/// SQLite context rather than driving the write-path service — this gives precise control over status,
/// entry date, per-line accounts, and the base-currency amounts the aggregation sums. Each added line lets
/// the test set the transactional and base amounts independently so the "base-only summation" rule
/// (SDD-FIN-003 §2.1) can be exercised against deliberately divergent transactional amounts.
/// </summary>
public sealed class PostedEntrySeedBuilder
{
    private static int _sequence;

    private readonly List<JournalEntryLine> _lines = [];
    private Guid _id = Guid.NewGuid();
    private string? _entryNumber = $"JE-2026-{Interlocked.Increment(ref _sequence):000000}";
    private DateTimeOffset _entryDate = new(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
    private string _description = "Seeded ledger entry";
    private string _baseCurrencyCode = "BGN";
    private JournalEntryStatus _status = JournalEntryStatus.Posted;
    private Guid? _reversesEntryId;

    private PostedEntrySeedBuilder()
    {
    }

    /// <summary>Starts a new builder defaulting to a <c>Posted</c> entry dated 2026-06-01 with no lines.</summary>
    /// <returns>A new builder instance.</returns>
    public static PostedEntrySeedBuilder Create()
    {
        return new PostedEntrySeedBuilder();
    }

    /// <summary>Sets the entry identifier (used to link a reversal to its original).</summary>
    /// <param name="id">The entry identifier.</param>
    /// <returns>The same builder.</returns>
    public PostedEntrySeedBuilder WithId(Guid id)
    {
        _id = id;
        return this;
    }

    /// <summary>Sets the accounting entry date the aggregation windows on.</summary>
    /// <param name="entryDate">The entry date.</param>
    /// <returns>The same builder.</returns>
    public PostedEntrySeedBuilder WithEntryDate(DateTimeOffset entryDate)
    {
        _entryDate = entryDate;
        return this;
    }

    /// <summary>Sets the gapless document number surfaced on ledger lines.</summary>
    /// <param name="entryNumber">The entry number.</param>
    /// <returns>The same builder.</returns>
    public PostedEntrySeedBuilder WithEntryNumber(string? entryNumber)
    {
        _entryNumber = entryNumber;
        return this;
    }

    /// <summary>Sets the entry memo.</summary>
    /// <param name="description">The description.</param>
    /// <returns>The same builder.</returns>
    public PostedEntrySeedBuilder WithDescription(string description)
    {
        _description = description;
        return this;
    }

    /// <summary>Marks the entry as <c>Draft</c> (excluded from every balance, SDD-FIN-003 §2.1).</summary>
    /// <returns>The same builder.</returns>
    public PostedEntrySeedBuilder AsDraft()
    {
        _status = JournalEntryStatus.Draft;
        _entryNumber = null;
        return this;
    }

    /// <summary>Marks the entry as <c>Reversed</c> (still aggregated, SDD-FIN-003 §2.1).</summary>
    /// <returns>The same builder.</returns>
    public PostedEntrySeedBuilder AsReversed()
    {
        _status = JournalEntryStatus.Reversed;
        return this;
    }

    /// <summary>Links this entry to the original it reverses.</summary>
    /// <param name="originalId">The reversed original's identifier.</param>
    /// <returns>The same builder.</returns>
    public PostedEntrySeedBuilder Reverses(Guid originalId)
    {
        _reversesEntryId = originalId;
        return this;
    }

    /// <summary>
    /// Adds a debit line whose base-currency debit equals <paramref name="amount"/>. The transactional
    /// amount mirrors the base amount unless <paramref name="transactionalAmount"/> is supplied, letting a
    /// test prove only the base column is summed (SDD-FIN-003 §2.1).
    /// </summary>
    /// <param name="accountId">The posting-target account id.</param>
    /// <param name="amount">The base-currency debit amount.</param>
    /// <param name="currencyCode">The transactional currency code (defaults to the base currency).</param>
    /// <param name="transactionalAmount">The transactional debit amount, when it differs from the base amount.</param>
    /// <returns>The same builder.</returns>
    public PostedEntrySeedBuilder WithDebit(
        int accountId,
        decimal amount,
        string currencyCode = "BGN",
        decimal? transactionalAmount = null)
    {
        _lines.Add(new JournalEntryLine
        {
            AccountId = accountId,
            DebitAmount = transactionalAmount ?? amount,
            CreditAmount = 0m,
            CurrencyCode = currencyCode,
            ExchangeRate = 1.000000m,
            BaseDebitAmount = amount,
            BaseCreditAmount = 0m,
            LineNumber = _lines.Count + 1
        });
        return this;
    }

    /// <summary>
    /// Adds a credit line whose base-currency credit equals <paramref name="amount"/>. The transactional
    /// amount mirrors the base amount unless <paramref name="transactionalAmount"/> is supplied.
    /// </summary>
    /// <param name="accountId">The posting-target account id.</param>
    /// <param name="amount">The base-currency credit amount.</param>
    /// <param name="currencyCode">The transactional currency code (defaults to the base currency).</param>
    /// <param name="transactionalAmount">The transactional credit amount, when it differs from the base amount.</param>
    /// <returns>The same builder.</returns>
    public PostedEntrySeedBuilder WithCredit(
        int accountId,
        decimal amount,
        string currencyCode = "BGN",
        decimal? transactionalAmount = null)
    {
        _lines.Add(new JournalEntryLine
        {
            AccountId = accountId,
            DebitAmount = 0m,
            CreditAmount = transactionalAmount ?? amount,
            CurrencyCode = currencyCode,
            ExchangeRate = 1.000000m,
            BaseDebitAmount = 0m,
            BaseCreditAmount = amount,
            LineNumber = _lines.Count + 1
        });
        return this;
    }

    /// <summary>Materializes the configured <see cref="JournalEntry"/> with its lines.</summary>
    /// <returns>The built entry, ready to be added to the context.</returns>
    public JournalEntry Build()
    {
        JournalEntry entry = new()
        {
            Id = _id,
            EntryNumber = _entryNumber,
            EntryDate = _entryDate,
            Description = _description,
            BaseCurrencyCode = _baseCurrencyCode,
            Status = _status,
            ReversesEntryId = _reversesEntryId,
            CorrelationId = "test-correlation",
            CreatedAt = _entryDate,
            CreatedBy = Guid.NewGuid(),
            Lines = _lines
        };

        if (_status != JournalEntryStatus.Draft)
        {
            entry.PostedAt = _entryDate;
            entry.PostedBy = Guid.NewGuid();
        }

        return entry;
    }
}
