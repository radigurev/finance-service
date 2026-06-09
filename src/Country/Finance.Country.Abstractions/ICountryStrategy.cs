using Finance.Common.Enums;

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

    /// <summary>
    /// The country's standard (default) tax rate as a decimal fraction (e.g. <c>0.20</c> for 20%)
    /// (SDD-CTRY-001 §5, SDD-INT-WH-001 §2.3). A side-effect-free, deterministic read. Used to default a
    /// line whose source document omits an explicit tax rate (so a Warehouse-originated draft carries the
    /// country's default rate rather than zero) — the country owns the rate so the core never hard-codes a
    /// VAT rate. The value MUST satisfy <see cref="IsValidTaxRate"/>.
    /// </summary>
    decimal StandardTaxRate { get; }

    /// <summary>
    /// Applies the country's rounding mode for a monetary tax amount, returning the rounded value
    /// (SDD-CTRY-001 §5, SDD-INV-001 §2.8). Pure, deterministic, and side-effect-free: the same input
    /// always yields the same output. The Finance core MUST route every tax rounding through this member
    /// rather than inlining a <see cref="System.MidpointRounding"/> mode, so the rounding rule stays
    /// country-owned.
    /// </summary>
    /// <param name="amount">The raw (unrounded) tax amount to round.</param>
    /// <returns>The amount rounded to the country's tax precision and midpoint rule.</returns>
    decimal ApplyTaxRounding(decimal amount);

    /// <summary>
    /// Determines whether <paramref name="rate"/> is a tax rate the country recognizes as legal
    /// (SDD-CTRY-001 §5, SDD-INV-001 §2.8). Pure, deterministic, and side-effect-free. The invoice service
    /// rejects an unrecognized rate with <c>INVALID_INVOICE_TAX_RATE</c>; the country owns which rates are
    /// valid so the core never hard-codes a VAT rate.
    /// </summary>
    /// <param name="rate">The tax rate to test (e.g. <c>0.20</c> for 20%).</param>
    /// <returns><c>true</c> when the rate is a recognized country tax rate; otherwise <c>false</c>.</returns>
    bool IsValidTaxRate(decimal rate);

    /// <summary>
    /// Formats a gapless sequence value into the country's document number for the supplied
    /// <paramref name="documentType"/> (SDD-CTRY-001 §5, SDD-INV-001 §2.4). Pure, deterministic, and
    /// side-effect-free — it performs no I/O and never allocates a sequence value itself (the caller
    /// allocates it via <c>ISequenceGenerator</c>). The prefix is per document type (purchase / sale /
    /// credit note / debit note).
    /// </summary>
    /// <param name="documentType">The invoice document type whose number is being formatted.</param>
    /// <param name="sequenceValue">The freshly allocated gapless sequence value (1-based).</param>
    /// <returns>The country-formatted document number (e.g. <c>ФПр-2026-000001</c>).</returns>
    string GenerateDocumentNumber(InvoiceDocumentType documentType, long sequenceValue);
}
