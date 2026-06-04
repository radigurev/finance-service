namespace Finance.Country.Abstractions;

/// <summary>
/// A default posting-rule template returned by <see cref="ICountryStrategy.GetDefaultPostingRules"/>
/// (SDD-CTRY-001 §2.2). A plain immutable data contract — the seed source for SDD-FIN-006's editable
/// posting-rule store, not the live rule itself. A template MUST be structurally balanceable: its
/// <see cref="Lines"/> MUST include at least one debit and one credit line.
/// </summary>
public sealed record PostingRuleTemplate
{
    /// <summary>The stable machine key the seeder upserts by (e.g. <c>"SALE_INVOICE"</c>); unique within the returned list.</summary>
    public required string RuleKey { get; init; }

    /// <summary>A human-readable description of what the rule books.</summary>
    public required string Description { get; init; }

    /// <summary>The ISO 3166-1 alpha-2 country code; MUST equal the producing strategy's <c>CountryCode</c>.</summary>
    public required string CountryCode { get; init; }

    /// <summary>The ordered lines composing the rule.</summary>
    public required IReadOnlyList<PostingRuleLineTemplate> Lines { get; init; }
}
