namespace Finance.GenericFiltering.Models;

/// <summary>
/// The canonical filtering request contract: a set of AND-combined filter clauses,
/// an ordered list of sort clauses, 1-based pagination, and an optional free-text
/// search term ORed across all <c>[Searchable]</c> string properties.
/// </summary>
public sealed record FilterRequest
{
    /// <summary>Filter clauses combined with logical AND. Empty means no filtering.</summary>
    public List<FilterCriterion> Filters { get; init; } = [];

    /// <summary>Ordered sort clauses applied before the deterministic final key.</summary>
    public List<SortCriterion> Sort { get; init; } = [];

    /// <summary>1-based page number. Defaults to 1.</summary>
    public int Page { get; init; } = 1;

    /// <summary>Page size. Defaults to 50; capped at 200.</summary>
    public int PageSize { get; init; } = 50;

    /// <summary>Optional free-text term ORed across all <c>[Searchable]</c> properties.</summary>
    public string? Search { get; init; }
}
