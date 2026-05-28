using Finance.Common.Enums;

namespace Finance.ServiceModel.Accounts;

/// <summary>
/// Representation of a financial account exposed by the API.
/// </summary>
public sealed record AccountDto
{
    /// <summary>Surrogate identifier of the account.</summary>
    public required int Id { get; init; }

    /// <summary>Country-specific account code (e.g. "304", "401", "501").</summary>
    public required string Code { get; init; }

    /// <summary>Human-readable account name.</summary>
    public required string Name { get; init; }

    /// <summary>Asset, Liability, Equity, Revenue, or Expense.</summary>
    public required AccountType Type { get; init; }

    /// <summary>Optional parent account ID, for hierarchical charts.</summary>
    public int? ParentId { get; init; }

    /// <summary>Whether the account is active and available for posting.</summary>
    public required bool IsActive { get; init; }

    /// <summary>ISO 3166-1 alpha-2 country code identifying the owning chart.</summary>
    public required string CountryCode { get; init; }
}
