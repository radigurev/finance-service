namespace Finance.Common.ErrorCodes;

/// <summary>
/// Machine-readable error codes for aggregate workflow state-transition failures.
/// Used as the <c>title</c> field of ProblemDetails responses.
/// <para>The concurrency code lives in <see cref="CommonErrorCodes.CONCURRENT_MODIFICATION"/>
/// and is referenced (never redefined) from here.</para>
/// </summary>
public static class WorkflowErrorCodes
{
    /// <summary>The requested target state is not in the current state's allowed next states.</summary>
    public const string INVALID_STATE_TRANSITION = nameof(INVALID_STATE_TRANSITION);

    /// <summary>A registered transition guard validator rejected the transition.</summary>
    public const string WORKFLOW_GUARD_FAILED = nameof(WORKFLOW_GUARD_FAILED);

    /// <summary>The aggregate's current state has no registered workflow state implementation.</summary>
    public const string STATE_NOT_REGISTERED = nameof(STATE_NOT_REGISTERED);
}
