using Finance.Country.Abstractions;

namespace Finance.ServiceModel.Posting;

/// <summary>
/// Request body for applying a named posting rule to a caller-supplied amount context (SDD-FIN-006 §2.5).
/// The engine resolves the active rule, materializes balanced debit/credit lines by mapping each line's
/// <see cref="PostingAmountSource"/> to <see cref="Amounts"/>, and delegates posting to the journal-entry
/// service. v1 SHOULD supply a base-currency context (<see cref="CurrencyCode"/> equal to the country's
/// base currency); multi-currency contexts are deferred to SDD-FIN-005.
/// </summary>
public sealed record ApplyPostingRuleRequest
{
    /// <summary>The stable key of the active rule to apply.</summary>
    public required string RuleKey { get; init; }

    /// <summary>The named monetary amounts keyed by <see cref="PostingAmountSource"/> (e.g. Net, Tax, Gross).</summary>
    public required IReadOnlyDictionary<PostingAmountSource, decimal> Amounts { get; init; }

    /// <summary>The ISO 4217 alphabetic currency code of the supplied amounts.</summary>
    public required string CurrencyCode { get; init; }

    /// <summary>The accounting date of the resulting entry (used for period assignment).</summary>
    public required DateTimeOffset EntryDate { get; init; }

    /// <summary>An optional memo; when empty the engine derives one from the rule key.</summary>
    public string? Description { get; init; }

    /// <summary>
    /// Optional per-line account overrides keyed by the line's <c>AccountSelector</c> code, redirecting a
    /// line to a specific account code (e.g. a particular customer's receivable sub-account).
    /// </summary>
    public IReadOnlyDictionary<string, string>? AccountOverrides { get; init; }

    /// <summary>When <c>true</c> (the default) the resulting draft is posted immediately; otherwise it is left as a draft.</summary>
    public bool PostImmediately { get; init; } = true;
}
