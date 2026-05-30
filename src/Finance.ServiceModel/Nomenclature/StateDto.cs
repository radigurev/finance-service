namespace Finance.ServiceModel.Nomenclature;

/// <summary>
/// A state / province record proxied from Warehouse Nomenclature (SDD-NOM-001 §2.3).
/// </summary>
public sealed record StateDto
{
    /// <summary>The Warehouse state identifier.</summary>
    public required int Id { get; init; }

    /// <summary>Human-readable state / province name.</summary>
    public required string Name { get; init; }

    /// <summary>ISO 3166-1 alpha-2 code of the owning country.</summary>
    public required string CountryIsoCode { get; init; }
}
