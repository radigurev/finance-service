using Finance.GenericFiltering.Attributes;

namespace Finance.GenericFiltering.Tests.TestEntities;

/// <summary>
/// Test entity without an <c>Id</c> property, used to verify the deterministic final sort
/// falls back to the first declared <c>[Sortable]</c> property.
/// </summary>
public sealed class RowWithoutId
{
    /// <summary>First declared sortable property — the expected fallback final-sort key.</summary>
    [Filterable]
    [Sortable]
    public string Code { get; set; } = string.Empty;

    /// <summary>Secondary sortable property.</summary>
    [Filterable]
    [Sortable]
    public int Rank { get; set; }
}
