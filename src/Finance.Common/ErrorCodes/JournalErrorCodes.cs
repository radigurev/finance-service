namespace Finance.Common.ErrorCodes;

/// <summary>
/// Machine-readable error codes for the Journal domain, shared by the Double-Entry Engine
/// (SDD-FIN-001) and the Journal Entry Lifecycle (SDD-FIN-002). Used as the <c>title</c> field of
/// ProblemDetails responses and in FluentValidation <c>.WithErrorCode(...)</c> calls.
/// <para>The concurrency code is sourced from <see cref="CommonErrorCodes.CONCURRENT_MODIFICATION"/>
/// and referenced (never redefined) from here.</para>
/// </summary>
public static class JournalErrorCodes
{
    /// <summary>The sum of base-currency debits does not equal the sum of base-currency credits (SDD-FIN-001 §2.3).</summary>
    public const string UNBALANCED_ENTRY = nameof(UNBALANCED_ENTRY);

    /// <summary>A line carries both a debit and a credit amount (SDD-FIN-001 §2.4).</summary>
    public const string LINE_DEBIT_AND_CREDIT_SET = nameof(LINE_DEBIT_AND_CREDIT_SET);

    /// <summary>A line has neither a positive debit nor a positive credit amount (SDD-FIN-001 §2.4).</summary>
    public const string LINE_HAS_NO_AMOUNT = nameof(LINE_HAS_NO_AMOUNT);

    /// <summary>The entry contains fewer than two lines (SDD-FIN-001 §2.5).</summary>
    public const string MIN_TWO_LINES_REQUIRED = nameof(MIN_TWO_LINES_REQUIRED);

    /// <summary>A line account is missing, inactive, or a header/parent account (SDD-FIN-001 §2.6).</summary>
    public const string ACCOUNT_NOT_POSTABLE = nameof(ACCOUNT_NOT_POSTABLE);

    /// <summary>A line currency is malformed or is not an active currency (SDD-FIN-001 §2.7).</summary>
    public const string INVALID_LINE_CURRENCY = nameof(INVALID_LINE_CURRENCY);

    /// <summary>A line base amount does not reconcile with amount × rate, or the rate is ≤ 0 on a foreign line (SDD-FIN-001 §2.7).</summary>
    public const string INVALID_LINE_BASE_AMOUNT = nameof(INVALID_LINE_BASE_AMOUNT);

    /// <summary>The accounting entry date is missing (SDD-FIN-001 §3.1).</summary>
    public const string INVALID_ENTRY_DATE = nameof(INVALID_ENTRY_DATE);

    /// <summary>A post was attempted on an entry that is not in the <c>Draft</c> state (SDD-FIN-002 §2.4).</summary>
    public const string ENTRY_NOT_DRAFT = nameof(ENTRY_NOT_DRAFT);

    /// <summary>An update or delete was attempted on a <c>Posted</c> or <c>Reversed</c> entry (SDD-FIN-002 §2.8).</summary>
    public const string CANNOT_EDIT_POSTED_ENTRY = nameof(CANNOT_EDIT_POSTED_ENTRY);

    /// <summary>
    /// The requested lifecycle transition is not allowed (e.g. reversing a draft or re-reversing). The
    /// Journal-domain alias for the workflow engine's generic <c>INVALID_STATE_TRANSITION</c> (SDD-FIN-002 §2.1).
    /// </summary>
    public const string INVALID_JOURNAL_STATE_TRANSITION = nameof(INVALID_JOURNAL_STATE_TRANSITION);

    /// <summary>
    /// The entry's accounting date falls in a closed or locked fiscal period. The real check is supplied
    /// by SDD-FIN-004; the Batch-10 default always-open guard never returns this code (SDD-FIN-002 §2.7).
    /// </summary>
    public const string POSTING_PERIOD_CLOSED = nameof(POSTING_PERIOD_CLOSED);

    /// <summary>A reversal was requested without a non-empty reason (SDD-FIN-002 §2.6).</summary>
    public const string REVERSAL_REASON_REQUIRED = nameof(REVERSAL_REASON_REQUIRED);

    /// <summary>The referenced journal entry does not exist (SDD-FIN-002 §2.9).</summary>
    public const string JOURNAL_ENTRY_NOT_FOUND = nameof(JOURNAL_ENTRY_NOT_FOUND);

    /// <summary>
    /// A general-ledger date window is invalid: <c>fromDate &gt; toDate</c>, <c>fromDate &gt; asOfDate</c>,
    /// or a required as-of date is missing (SDD-FIN-003 §4).
    /// </summary>
    public const string INVALID_DATE_RANGE = nameof(INVALID_DATE_RANGE);

    /// <summary>A general-ledger account-ledger route <c>accountId</c> is not a positive integer (SDD-FIN-003 §4).</summary>
    public const string INVALID_ACCOUNT_ID = nameof(INVALID_ACCOUNT_ID);
}
