using Finance.GenericFiltering.Attributes;

namespace Finance.Nomenclature.DBModel.Models;

/// <summary>
/// Persistent representation of an ISO 4217 currency owned by the Nomenclature service
/// (SDD-NOM-001 §2.0). Soft-deletion is performed via <see cref="IsActive"/>; rows are never
/// hard-deleted because historical documents reference the currency by <see cref="IsoCode"/>.
/// </summary>
public sealed class Currency
{
    /// <summary>Surrogate identifier (internal — not exposed in events or external references).</summary>
    public int Id { get; set; }

    /// <summary>ISO 4217 alphabetic code (exactly three uppercase letters, e.g. "BGN", "EUR"). Unique.</summary>
    [Filterable]
    [Sortable]
    [Searchable]
    public required string IsoCode { get; set; }

    /// <summary>Human-readable currency name (e.g. "Bulgarian Lev").</summary>
    [Filterable]
    [Sortable]
    [Searchable]
    public required string Name { get; set; }

    /// <summary>Optional display symbol (e.g. "лв", "€", "$").</summary>
    [Filterable]
    [Sortable]
    public string? Symbol { get; set; }

    /// <summary>Whether the currency is active and offered in dropdowns.</summary>
    [Filterable]
    [Sortable]
    public bool IsActive { get; set; } = true;

    /// <summary>UTC creation timestamp.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>UTC last-update timestamp.</summary>
    public DateTimeOffset? UpdatedAt { get; set; }

    /// <summary>SQL Server <c>rowversion</c> optimistic-concurrency token.</summary>
    public byte[] RowVersion { get; set; } = [];
}
