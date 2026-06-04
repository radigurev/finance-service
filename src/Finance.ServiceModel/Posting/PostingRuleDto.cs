namespace Finance.ServiceModel.Posting;

/// <summary>
/// Representation of a posting rule exposed by the Journal API (SDD-FIN-006 §2.1).
/// </summary>
public sealed record PostingRuleDto
{
    /// <summary>Surrogate identifier of the rule.</summary>
    public required int Id { get; init; }

    /// <summary>The stable, unique, uppercase machine key (e.g. <c>"SALE_INVOICE"</c>).</summary>
    public required string RuleKey { get; init; }

    /// <summary>A human-readable description of what the rule books.</summary>
    public required string Description { get; init; }

    /// <summary>The ISO 3166-1 alpha-2 country code that owns the rule.</summary>
    public required string CountryCode { get; init; }

    /// <summary>Whether the rule is active and applicable.</summary>
    public required bool IsActive { get; init; }

    /// <summary>The ordered lines composing the rule.</summary>
    public required IReadOnlyList<PostingRuleLineDto> Lines { get; init; }

    /// <summary>
    /// Base64-encoded SQL Server <c>rowversion</c> optimistic-concurrency token. Clients round-trip this
    /// value back on update so a stale write is rejected with <c>CONCURRENT_MODIFICATION</c>.
    /// </summary>
    public required string RowVersion { get; init; }
}
