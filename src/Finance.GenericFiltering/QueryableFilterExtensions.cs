using Finance.Common.ErrorCodes;
using Finance.GenericFiltering.Exceptions;
using Finance.GenericFiltering.Internal;
using Finance.GenericFiltering.Models;

namespace Finance.GenericFiltering;

/// <summary>
/// <see cref="IQueryable{T}"/> extension methods that translate a <see cref="FilterRequest"/>
/// into Where + OrderBy + Skip + Take, enforcing property opt-in, operator validity, the
/// deterministic final sort key, and the page-size cap.
/// </summary>
public static class QueryableFilterExtensions
{
    /// <summary>The maximum permitted <see cref="FilterRequest.PageSize"/>.</summary>
    public const int MaxPageSize = 200;

    /// <summary>
    /// Composes the filter, search, sort, and pagination of <paramref name="request"/> onto
    /// <paramref name="source"/>, returning the deferred query (no materialization).
    /// The result always carries a deterministic final sort term so paging is stable.
    /// </summary>
    /// <typeparam name="T">The entity type.</typeparam>
    /// <param name="source">The source query.</param>
    /// <param name="request">The filter request.</param>
    /// <returns>The composed query with paging applied.</returns>
    /// <exception cref="FilterValidationException">When the request is invalid.</exception>
    public static IQueryable<T> ApplyFilter<T>(this IQueryable<T> source, FilterRequest request)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(request);

        EnsurePageSize(request.PageSize);
        EntityFilterMetadata metadata = EntityFilterMetadata.For(typeof(T));

        IQueryable<T> filtered = WhereClauseBuilder.Apply(source, request, metadata);
        IQueryable<T> ordered = SortApplier.Apply(filtered, request.Sort, metadata);

        int page = request.Page < 1 ? 1 : request.Page;
        int skip = (page - 1) * request.PageSize;
        return ordered.Skip(skip).Take(request.PageSize);
    }

    /// <summary>
    /// Composes the filter onto <paramref name="source"/> WITHOUT pagination, returning the
    /// filtered and ordered query. Useful for computing <c>TotalCount</c> separately.
    /// </summary>
    /// <typeparam name="T">The entity type.</typeparam>
    /// <param name="source">The source query.</param>
    /// <param name="request">The filter request.</param>
    /// <returns>The filtered and ordered query without Skip/Take.</returns>
    /// <exception cref="FilterValidationException">When the request is invalid.</exception>
    public static IQueryable<T> ApplyFilterWithoutPaging<T>(this IQueryable<T> source, FilterRequest request)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(request);

        EnsurePageSize(request.PageSize);
        EntityFilterMetadata metadata = EntityFilterMetadata.For(typeof(T));

        IQueryable<T> filtered = WhereClauseBuilder.Apply(source, request, metadata);
        return SortApplier.Apply(filtered, request.Sort, metadata);
    }

    /// <summary>
    /// Materializes a <see cref="PagedResult{T}"/> synchronously over an in-memory or
    /// LINQ-to-Objects source. EF Core callers SHOULD compose <see cref="ApplyFilterWithoutPaging{T}"/>
    /// and use their provider's async <c>CountAsync</c> / <c>ToListAsync</c> instead.
    /// </summary>
    /// <typeparam name="T">The entity type.</typeparam>
    /// <param name="source">The source query.</param>
    /// <param name="request">The filter request.</param>
    /// <returns>The paged result envelope.</returns>
    /// <exception cref="FilterValidationException">When the request is invalid.</exception>
    public static PagedResult<T> ToPagedResult<T>(this IQueryable<T> source, FilterRequest request)
    {
        IQueryable<T> ordered = source.ApplyFilterWithoutPaging(request);
        int total = ordered.Count();

        int page = request.Page < 1 ? 1 : request.Page;
        int skip = (page - 1) * request.PageSize;
        List<T> items = ordered.Skip(skip).Take(request.PageSize).ToList();

        return new PagedResult<T>
        {
            Items = items,
            TotalCount = total,
            Page = page,
            PageSize = request.PageSize
        };
    }

    private static void EnsurePageSize(int pageSize)
    {
        if (pageSize > MaxPageSize)
        {
            throw new FilterValidationException(
                FilterErrorCodes.PAGE_SIZE_TOO_LARGE,
                $"The requested page size {pageSize} exceeds the maximum of {MaxPageSize}.");
        }
    }
}
