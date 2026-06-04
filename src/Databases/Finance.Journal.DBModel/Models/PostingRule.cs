using Finance.GenericFiltering.Attributes;

namespace Finance.Journal.DBModel.Models;

/// <summary>
/// A named posting-rule template — editable reference data (SDD-FIN-006 §2.1). Owns an ordered set of
/// <see cref="PostingRuleLine"/>s and is seeded from <c>ICountryStrategy.GetDefaultPostingRules()</c>
/// (SDD-CTRY-001). An internal reference-data entity, so its primary key is an <c>INT IDENTITY</c>
/// (CLAUDE.md §0.1). Retired by deactivation (<see cref="IsActive"/> = <c>false</c>), never hard-deleted.
/// </summary>
public sealed class PostingRule
{
    /// <summary>Surrogate identifier.</summary>
    public int Id { get; set; }

    /// <summary>The stable, unique, uppercase machine key (e.g. <c>"SALE_INVOICE"</c>); immutable after create.</summary>
    [Filterable]
    [Sortable]
    public required string RuleKey { get; set; }

    /// <summary>A human-readable description of what the rule books.</summary>
    [Filterable]
    [Sortable]
    public required string Description { get; set; }

    /// <summary>The ISO 3166-1 alpha-2 country code that owns the rule.</summary>
    [Filterable]
    [Sortable]
    public required string CountryCode { get; set; }

    /// <summary>Whether the rule is active and applicable; an inactive rule is excluded from apply resolution.</summary>
    [Filterable]
    [Sortable]
    public bool IsActive { get; set; } = true;

    /// <summary>UTC-offset creation timestamp.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>UTC-offset last-update timestamp.</summary>
    public DateTimeOffset? UpdatedAt { get; set; }

    /// <summary>SQL Server <c>rowversion</c> optimistic-concurrency token (SDD-INFRA-009).</summary>
    public byte[] RowVersion { get; set; } = [];

    /// <summary>The ordered lines composing the rule (composition: loaded and saved with the rule).</summary>
    public ICollection<PostingRuleLine> Lines { get; set; } = new List<PostingRuleLine>();
}
