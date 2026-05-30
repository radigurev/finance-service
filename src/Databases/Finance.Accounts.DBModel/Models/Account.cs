using Finance.Common.Enums;
using Finance.GenericFiltering.Attributes;

namespace Finance.Accounts.DBModel.Models;

/// <summary>
/// Persistent representation of a financial account inside the chart of accounts.
/// </summary>
public sealed class Account
{
    /// <summary>Surrogate identifier.</summary>
    public int Id { get; set; }

    /// <summary>Country-specific account code (e.g. "304", "401").</summary>
    [Filterable]
    [Sortable]
    [Searchable]
    public required string Code { get; set; }

    /// <summary>Human-readable account name.</summary>
    [Filterable]
    [Sortable]
    [Searchable]
    public required string Name { get; set; }

    /// <summary>Asset, Liability, Equity, Revenue, or Expense.</summary>
    [Filterable]
    [Sortable]
    public required AccountType Type { get; set; }

    /// <summary>Optional parent account ID for hierarchical charts.</summary>
    public int? ParentId { get; set; }

    /// <summary>Navigation to the parent account.</summary>
    public Account? Parent { get; set; }

    /// <summary>Child accounts (sub-accounts of this account).</summary>
    public ICollection<Account> Children { get; set; } = new List<Account>();

    /// <summary>Whether the account is active and available for posting.</summary>
    [Filterable]
    [Sortable]
    public bool IsActive { get; set; } = true;

    /// <summary>ISO 3166-1 alpha-2 country code identifying the owning chart.</summary>
    [Filterable]
    [Sortable]
    public required string CountryCode { get; set; }

    /// <summary>UTC creation timestamp.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>UTC last-update timestamp.</summary>
    public DateTimeOffset? UpdatedAt { get; set; }

    /// <summary>SQL Server <c>rowversion</c> optimistic-concurrency token.</summary>
    public byte[] RowVersion { get; set; } = [];
}
