namespace Finance.Common.ErrorCodes;

/// <summary>
/// Machine-readable error codes for the Fiscal Period domain (SDD-FIN-004 §4). Used as the
/// <c>title</c> field of ProblemDetails responses and in FluentValidation <c>.WithErrorCode(...)</c> calls.
/// <para>The concurrency code is sourced from <see cref="CommonErrorCodes.CONCURRENT_MODIFICATION"/> and
/// referenced (never redefined) from here; <c>POSTING_PERIOD_CLOSED</c> lives in
/// <see cref="JournalErrorCodes"/> and is the posting-side rejection, not a Periods-domain code.</para>
/// </summary>
public static class PeriodErrorCodes
{
    /// <summary>The referenced fiscal period does not exist (get / close / reopen) (SDD-FIN-004 §3.3).</summary>
    public const string PERIOD_NOT_FOUND = nameof(PERIOD_NOT_FOUND);

    /// <summary>No period's <c>[StartDate, EndDate]</c> contains the supplied date (SDD-FIN-004 §2.6).</summary>
    public const string NO_PERIOD_FOR_DATE = nameof(NO_PERIOD_FOR_DATE);

    /// <summary>A close was attempted on a period already in the <c>Closed</c> state (SDD-FIN-004 §2.4).</summary>
    public const string PERIOD_ALREADY_CLOSED = nameof(PERIOD_ALREADY_CLOSED);

    /// <summary>A reopen was attempted on a period already in the <c>Open</c> state (SDD-FIN-004 §2.5).</summary>
    public const string PERIOD_ALREADY_OPEN = nameof(PERIOD_ALREADY_OPEN);

    /// <summary>
    /// The requested lifecycle transition is not allowed. The Periods-domain alias for the workflow
    /// engine's generic <c>INVALID_STATE_TRANSITION</c> (SDD-FIN-004 §2.1, §4).
    /// </summary>
    public const string INVALID_PERIOD_STATE_TRANSITION = nameof(INVALID_PERIOD_STATE_TRANSITION);

    /// <summary>
    /// Close with an earlier <c>Open</c> period (or reopen with a later <c>Closed</c> period) in the same
    /// fiscal year — the period-ordering invariant (SDD-FIN-004 §2.4, §2.5).
    /// </summary>
    public const string CANNOT_CLOSE_OUT_OF_ORDER = nameof(CANNOT_CLOSE_OUT_OF_ORDER);

    /// <summary>A generated or created period's date range overlaps an existing period (SDD-FIN-004 §2.2).</summary>
    public const string OVERLAPPING_PERIOD = nameof(OVERLAPPING_PERIOD);

    /// <summary>A period with the supplied <c>(FiscalYear, PeriodNumber)</c> already exists (SDD-FIN-004 §2.2).</summary>
    public const string DUPLICATE_PERIOD = nameof(DUPLICATE_PERIOD);

    /// <summary>A close was requested without a non-empty <c>Reason</c> (SDD-FIN-004 §2.4).</summary>
    public const string CLOSE_REASON_REQUIRED = nameof(CLOSE_REASON_REQUIRED);

    /// <summary>A reopen was requested without a non-empty <c>Reason</c> (SDD-FIN-004 §2.5).</summary>
    public const string REOPEN_REASON_REQUIRED = nameof(REOPEN_REASON_REQUIRED);

    /// <summary>The generate / create / lookup request shape is invalid (year / number / dates / date param) (SDD-FIN-004 §3.1).</summary>
    public const string INVALID_PERIOD = nameof(INVALID_PERIOD);
}
