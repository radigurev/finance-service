namespace Finance.GenericFiltering.Models;

/// <summary>
/// The paged response envelope returned after applying a <see cref="FilterRequest"/>.
/// </summary>
/// <typeparam name="T">The item type of the page.</typeparam>
public sealed record PagedResult<T>
{
    /// <summary>The items on the current page.</summary>
    public required IReadOnlyList<T> Items { get; init; }

    /// <summary>The total number of matching items across all pages.</summary>
    public required int TotalCount { get; init; }

    /// <summary>The 1-based page number this result represents.</summary>
    public required int Page { get; init; }

    /// <summary>The page size used to produce this result.</summary>
    public required int PageSize { get; init; }
}
