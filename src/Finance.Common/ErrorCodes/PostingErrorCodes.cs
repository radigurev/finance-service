namespace Finance.Common.ErrorCodes;

/// <summary>
/// Machine-readable error codes for the Posting Rules + Posting Engine domain (SDD-FIN-006 §4). Used as
/// the <c>title</c> field of ProblemDetails responses and in FluentValidation <c>.WithErrorCode(...)</c>
/// calls — never raw string literals (CLAUDE.md §0.3). <c>CONCURRENT_MODIFICATION</c> is referenced from
/// <see cref="CommonErrorCodes"/>; <c>PAGE_SIZE_TOO_LARGE</c> from <see cref="FilterErrorCodes"/>;
/// <c>INVALID_LINE_CURRENCY</c> / <c>INVALID_ENTRY_DATE</c> from <see cref="JournalErrorCodes"/> — none
/// are redefined here.
/// </summary>
public static class PostingErrorCodes
{
    /// <summary>The posting rule id/key does not exist, or apply targets an inactive rule (404).</summary>
    public const string POSTING_RULE_NOT_FOUND = nameof(POSTING_RULE_NOT_FOUND);

    /// <summary>A create (or key-changing update) used a <c>RuleKey</c> that already exists (409).</summary>
    public const string DUPLICATE_POSTING_RULE_KEY = nameof(DUPLICATE_POSTING_RULE_KEY);

    /// <summary>A create/update supplied a rule with zero lines (400).</summary>
    public const string POSTING_RULE_HAS_NO_LINES = nameof(POSTING_RULE_HAS_NO_LINES);

    /// <summary>
    /// The rule is structurally not balanceable (create/update: missing a debit or a credit line), OR the
    /// materialized lines do not net to zero for the supplied amounts (apply, before the JE path) (409).
    /// </summary>
    public const string POSTING_RULE_UNBALANCED = nameof(POSTING_RULE_UNBALANCED);

    /// <summary>The apply context lacks an amount for an <c>AmountSource</c> referenced by the rule (400).</summary>
    public const string MISSING_POSTING_AMOUNT = nameof(MISSING_POSTING_AMOUNT);

    /// <summary>The <c>RuleKey</c> is empty, too long, or malformed (400).</summary>
    public const string INVALID_POSTING_RULE_KEY = nameof(INVALID_POSTING_RULE_KEY);

    /// <summary>A line has an empty <c>AccountSelector</c> or an invalid <c>DebitOrCredit</c>/<c>AmountSource</c> enum (400).</summary>
    public const string INVALID_POSTING_RULE_LINE = nameof(INVALID_POSTING_RULE_LINE);

    /// <summary>An <c>AccountSelector</c> code resolves to no postable account at apply time (422); at seed it is logged and the rule skipped.</summary>
    public const string POSTING_RULE_ACCOUNT_NOT_FOUND = nameof(POSTING_RULE_ACCOUNT_NOT_FOUND);
}
