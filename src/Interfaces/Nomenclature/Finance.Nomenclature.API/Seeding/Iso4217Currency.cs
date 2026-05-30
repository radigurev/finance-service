namespace Finance.Nomenclature.API.Seeding;

/// <summary>
/// A single ISO 4217 currency definition from the bundled static seed list (SDD-NOM-001 §2.5).
/// </summary>
/// <param name="IsoCode">The ISO 4217 alphabetic code (three uppercase letters).</param>
/// <param name="Name">The English currency name.</param>
/// <param name="Symbol">The optional display symbol, or <c>null</c> when none is defined.</param>
public sealed record Iso4217Currency(string IsoCode, string Name, string? Symbol);
