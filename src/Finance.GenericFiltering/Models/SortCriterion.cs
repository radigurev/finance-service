namespace Finance.GenericFiltering.Models;

/// <summary>
/// A single client-supplied sort clause. <see cref="Direction"/> is the wire token
/// <c>asc</c> or <c>desc</c>.
/// </summary>
public sealed record SortCriterion
{
    /// <summary>The entity property name to sort by. MUST be marked <c>[Sortable]</c>.</summary>
    public required string Field { get; init; }

    /// <summary>The sort direction wire token: <c>asc</c> (default) or <c>desc</c>.</summary>
    public string Direction { get; init; } = "asc";
}
