namespace Finance.ServiceModel.Nomenclature;

/// <summary>
/// A city record proxied from Warehouse Nomenclature (SDD-NOM-001 §2.3).
/// </summary>
public sealed record CityDto
{
    /// <summary>The Warehouse city identifier.</summary>
    public required int Id { get; init; }

    /// <summary>Human-readable city name.</summary>
    public required string Name { get; init; }

    /// <summary>The Warehouse identifier of the owning state / province.</summary>
    public required int StateId { get; init; }
}
