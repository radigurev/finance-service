namespace Finance.Country.Abstractions;

/// <summary>
/// The minimal v1 country-strategy seam (SDD-CTRY-001 §2.1). Keeps the Finance core country-agnostic by
/// exposing ONLY what the Posting Engine (SDD-FIN-006) needs now: the country's identity, its base
/// currency, and the default posting-rule templates to seed the rule store. Every member MUST be a pure,
/// deterministic, side-effect-free read — no I/O, DB, events, or async. Implementations MUST be stateless
/// and injected by interface (a single <c>AddScoped</c>/<c>AddSingleton</c> binding — no factory/resolver
/// in v1). The interface is grown one member at a time by the spec that owns each deferred responsibility;
/// it MUST NOT be widened speculatively (Interface-Segregation; SDD-CTRY-001 §5).
/// </summary>
public interface ICountryStrategy
{
    /// <summary>
    /// The ISO 3166-1 alpha-2 country code this strategy serves (e.g. <c>"BG"</c>). Non-null, non-empty,
    /// uppercase, and stable across calls.
    /// </summary>
    string CountryCode { get; }

    /// <summary>
    /// The ISO 4217 alphabetic base currency the country books in (e.g. <c>"BGN"</c>). Non-null,
    /// non-empty, uppercase, and stable across calls.
    /// </summary>
    string BaseCurrencyCode { get; }

    /// <summary>
    /// Returns the country's default posting-rule templates as a read-only list for SDD-FIN-006's seeder
    /// to upsert into the rule store. Non-null (possibly empty), deterministic, and performs no I/O. Every
    /// returned template's <see cref="PostingRuleTemplate.CountryCode"/> equals <see cref="CountryCode"/>
    /// and its <see cref="PostingRuleTemplate.RuleKey"/> is unique within the list.
    /// </summary>
    /// <returns>The country's default posting-rule templates (SDD-CTRY-001 §2.3).</returns>
    IReadOnlyList<PostingRuleTemplate> GetDefaultPostingRules();
}
