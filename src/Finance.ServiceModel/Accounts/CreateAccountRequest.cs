using Finance.Common.Enums;

namespace Finance.ServiceModel.Accounts;

/// <summary>
/// Request body for creating a new account in the chart.
/// </summary>
public sealed record CreateAccountRequest
{
    /// <summary>Country-specific account code (e.g. "304").</summary>
    public required string Code { get; init; }

    /// <summary>Human-readable account name.</summary>
    public required string Name { get; init; }

    /// <summary>Asset, Liability, Equity, Revenue, or Expense.</summary>
    public required AccountType Type { get; init; }

    /// <summary>Optional parent account ID.</summary>
    public int? ParentId { get; init; }
}
