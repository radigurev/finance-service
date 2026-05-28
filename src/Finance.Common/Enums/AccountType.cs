namespace Finance.Common.Enums;

/// <summary>
/// Top-level classification of a financial account, used to determine
/// debit/credit normal balance and statement placement.
/// </summary>
public enum AccountType
{
    /// <summary>Resources owned by the entity. Normal debit balance.</summary>
    Asset = 1,

    /// <summary>Obligations to external parties. Normal credit balance.</summary>
    Liability = 2,

    /// <summary>Residual owner interest. Normal credit balance.</summary>
    Equity = 3,

    /// <summary>Income from operations. Normal credit balance.</summary>
    Revenue = 4,

    /// <summary>Costs incurred to generate revenue. Normal debit balance.</summary>
    Expense = 5
}
