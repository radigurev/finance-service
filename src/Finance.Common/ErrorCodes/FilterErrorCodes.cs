namespace Finance.Common.ErrorCodes;

/// <summary>
/// Machine-readable error codes for generic filtering, sorting, and paging failures.
/// Used as the <c>title</c> field of ProblemDetails responses.
/// </summary>
public static class FilterErrorCodes
{
    /// <summary>A requested filter field is not exposed as filterable.</summary>
    public const string INVALID_FILTER_FIELD = nameof(INVALID_FILTER_FIELD);

    /// <summary>A requested sort field is not exposed as sortable.</summary>
    public const string INVALID_SORT_FIELD = nameof(INVALID_SORT_FIELD);

    /// <summary>The supplied filter operator is not recognized.</summary>
    public const string INVALID_OPERATOR = nameof(INVALID_OPERATOR);

    /// <summary>A filter value could not be parsed into the target property type.</summary>
    public const string INVALID_FILTER_VALUE = nameof(INVALID_FILTER_VALUE);

    /// <summary>The requested page size exceeds the allowed maximum.</summary>
    public const string PAGE_SIZE_TOO_LARGE = nameof(PAGE_SIZE_TOO_LARGE);
}
