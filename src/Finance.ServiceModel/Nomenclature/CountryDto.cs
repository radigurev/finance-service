namespace Finance.ServiceModel.Nomenclature;

/// <summary>
/// A country record proxied from Warehouse Nomenclature (SDD-NOM-001 §2.3). Finance does not own the
/// country catalogue; this DTO is the shape returned by the proxy endpoint and consumed by the SPA.
/// </summary>
public sealed record CountryDto
{
    /// <summary>The Warehouse country identifier.</summary>
    public required int Id { get; init; }

    /// <summary>ISO 3166-1 alpha-2 country code (e.g. "BG").</summary>
    public required string IsoCode { get; init; }

    /// <summary>Human-readable country name.</summary>
    public required string Name { get; init; }
}
