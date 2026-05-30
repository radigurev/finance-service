namespace Finance.GenericFiltering.Models;

/// <summary>
/// Sort ordering for a <see cref="SortCriterion"/>. Wire values are the
/// lowercase tokens <c>asc</c> and <c>desc</c>.
/// </summary>
public enum SortDirection
{
    /// <summary>Ascending order (<c>asc</c>).</summary>
    Asc = 0,

    /// <summary>Descending order (<c>desc</c>).</summary>
    Desc = 1
}
