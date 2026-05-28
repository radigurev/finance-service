namespace Finance.Common.ErrorCodes;

/// <summary>
/// Machine-readable error codes for the Chart of Accounts domain.
/// Used as the <c>title</c> field of ProblemDetails responses and in
/// FluentValidation <c>.WithErrorCode(...)</c> calls.
/// </summary>
public static class AccountErrorCodes
{
    /// <summary>The account code is missing or violates the country chart-of-accounts format.</summary>
    public const string INVALID_ACCOUNT_CODE = nameof(INVALID_ACCOUNT_CODE);

    /// <summary>The account code is already in use within the chart.</summary>
    public const string DUPLICATE_ACCOUNT_CODE = nameof(DUPLICATE_ACCOUNT_CODE);

    /// <summary>The referenced account does not exist.</summary>
    public const string ACCOUNT_NOT_FOUND = nameof(ACCOUNT_NOT_FOUND);

    /// <summary>The referenced parent account does not exist or cannot have children.</summary>
    public const string INVALID_PARENT_ACCOUNT = nameof(INVALID_PARENT_ACCOUNT);

    /// <summary>The account is currently inactive and cannot be used for posting.</summary>
    public const string ACCOUNT_INACTIVE = nameof(ACCOUNT_INACTIVE);

    /// <summary>The account type (Asset/Liability/Equity/Revenue/Expense) is invalid.</summary>
    public const string INVALID_ACCOUNT_TYPE = nameof(INVALID_ACCOUNT_TYPE);

    /// <summary>An account with posted entries cannot be deleted; deactivate it instead.</summary>
    public const string ACCOUNT_HAS_ENTRIES = nameof(ACCOUNT_HAS_ENTRIES);
}
