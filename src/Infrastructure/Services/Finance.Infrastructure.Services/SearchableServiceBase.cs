using AutoMapper;
using AutoMapper.QueryableExtensions;
using Finance.Common.Abstractions;
using Finance.Common.Results;
using Finance.GenericFiltering;
using Finance.GenericFiltering.Exceptions;
using Finance.GenericFiltering.Models;
using Microsoft.EntityFrameworkCore;

namespace Finance.Infrastructure.Services;

/// <summary>
/// Base class for searchable, paginated list services. Composes the SDD-INFRA-005 generic
/// filtering pipeline (<c>ApplyFilterWithoutPaging</c>, count-before-paging, AutoMapper
/// <c>ProjectTo</c>) onto an EF Core query per SDD-INFRA-009 §2.2.
/// </summary>
/// <typeparam name="TEntity">The entity type being queried.</typeparam>
/// <typeparam name="TDto">The DTO type the entity is projected to.</typeparam>
/// <typeparam name="TContext">The owning <see cref="DbContext"/> type.</typeparam>
public abstract class SearchableServiceBase<TEntity, TDto, TContext> : BaseEntityService<TContext>
    where TEntity : class
    where TContext : DbContext
{
    /// <summary>Initializes the searchable service with its context, mapper, and correlation accessor.</summary>
    /// <param name="db">The microservice's <see cref="DbContext"/>.</param>
    /// <param name="mapper">The AutoMapper instance used for projection.</param>
    /// <param name="correlation">The ambient correlation-id accessor.</param>
    protected SearchableServiceBase(TContext db, IMapper mapper, ICorrelationIdAccessor correlation)
        : base(db, mapper, correlation)
    {
    }

    /// <summary>
    /// Builds the base query the search starts from. Defaults to a non-tracking query over the
    /// full entity set; subclasses MAY override to add scope (e.g. only-active rows).
    /// </summary>
    /// <returns>The base <see cref="IQueryable{T}"/> the filter is applied to.</returns>
    protected virtual IQueryable<TEntity> BuildBaseQuery()
    {
        return Db.Set<TEntity>().AsNoTracking();
    }

    /// <summary>
    /// Applies <paramref name="request"/> to the base query, counts the matches before paging,
    /// then pages and projects the page to <typeparamref name="TDto"/>.
    /// </summary>
    /// <param name="request">The client-supplied filter, sort, and pagination request.</param>
    /// <param name="ct">A token to observe for cancellation.</param>
    /// <returns>A success result carrying the page, or a failure carrying the filter error code.</returns>
    public virtual async Task<Result<PagedResult<TDto>>> SearchAsync(FilterRequest request, CancellationToken ct)
    {
        try
        {
            IQueryable<TEntity> filtered = BuildBaseQuery().ApplyFilterWithoutPaging(request);
            int totalCount = await filtered.CountAsync(ct).ConfigureAwait(false);

            int page = request.Page < 1 ? 1 : request.Page;
            int skip = (page - 1) * request.PageSize;

            List<TDto> items = await filtered
                .Skip(skip)
                .Take(request.PageSize)
                .ProjectTo<TDto>(Mapper.ConfigurationProvider)
                .ToListAsync(ct)
                .ConfigureAwait(false);

            PagedResult<TDto> pagedResult = new()
            {
                Items = items,
                TotalCount = totalCount,
                Page = page,
                PageSize = request.PageSize
            };
            return Result<PagedResult<TDto>>.Success(pagedResult);
        }
        catch (FilterValidationException ex)
        {
            return Result<PagedResult<TDto>>.Failure(ex.ErrorCode, ex.Detail);
        }
    }
}
