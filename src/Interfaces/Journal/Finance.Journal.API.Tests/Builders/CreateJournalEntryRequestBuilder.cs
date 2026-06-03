using Finance.ServiceModel.Journal;

namespace Finance.Journal.API.Tests.Builders;

/// <summary>
/// Fluent builder for <see cref="CreateJournalEntryRequest"/> test data (SDD-FIN-002 §2.3). Defaults to a
/// balanced two-line base-currency (<c>BGN</c>) entry (a 100.00 debit to account 1, a 100.00 credit to
/// account 2) with a present entry date; tests replace the lines to exercise specific invariants.
/// </summary>
public sealed class CreateJournalEntryRequestBuilder
{
    private DateTimeOffset _entryDate = new(2026, 6, 3, 0, 0, 0, TimeSpan.Zero);
    private string _description = "Test journal entry";
    private IReadOnlyList<JournalEntryLineRequest> _lines =
    [
        JournalEntryLineRequestBuilder.Create().WithAccountId(1).AsDebit(100.00m).Build(),
        JournalEntryLineRequestBuilder.Create().WithAccountId(2).AsCredit(100.00m).Build()
    ];

    private CreateJournalEntryRequestBuilder()
    {
    }

    /// <summary>Starts a new builder seeded with a balanced two-line base-currency entry.</summary>
    /// <returns>A new builder instance.</returns>
    public static CreateJournalEntryRequestBuilder Create()
    {
        return new CreateJournalEntryRequestBuilder();
    }

    /// <summary>Sets the accounting entry date.</summary>
    /// <param name="entryDate">The entry date.</param>
    /// <returns>The same builder.</returns>
    public CreateJournalEntryRequestBuilder WithEntryDate(DateTimeOffset entryDate)
    {
        _entryDate = entryDate;
        return this;
    }

    /// <summary>Sets the memo description.</summary>
    /// <param name="description">The description.</param>
    /// <returns>The same builder.</returns>
    public CreateJournalEntryRequestBuilder WithDescription(string description)
    {
        _description = description;
        return this;
    }

    /// <summary>Replaces the entry's lines.</summary>
    /// <param name="lines">The replacement line set.</param>
    /// <returns>The same builder.</returns>
    public CreateJournalEntryRequestBuilder WithLines(params JournalEntryLineRequest[] lines)
    {
        _lines = lines;
        return this;
    }

    /// <summary>Materializes the configured <see cref="CreateJournalEntryRequest"/>.</summary>
    /// <returns>The built create request.</returns>
    public CreateJournalEntryRequest Build()
    {
        return new CreateJournalEntryRequest
        {
            EntryDate = _entryDate,
            Description = _description,
            Lines = _lines
        };
    }
}
