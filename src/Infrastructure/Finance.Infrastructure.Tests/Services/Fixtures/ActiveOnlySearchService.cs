using AutoMapper;
using Finance.Common.Abstractions;
using Finance.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace Finance.Infrastructure.Tests.Services.Fixtures;

/// <summary>
/// A <see cref="SearchableServiceBase{TEntity, TDto, TContext}"/> that overrides
/// <see cref="SearchableServiceBase{TEntity, TDto, TContext}.BuildBaseQuery"/> to scope the search to
/// active rows only — used to verify the override is respected (SDD-INFRA-009 §2.2).
/// </summary>
public sealed class ActiveOnlySearchService : SearchableServiceBase<SampleEntity, SampleDto, SampleDbContext>
{
    /// <summary>Initializes the active-only search service.</summary>
    /// <param name="db">The SQLite-backed context.</param>
    /// <param name="mapper">The AutoMapper instance.</param>
    /// <param name="correlation">The correlation accessor.</param>
    public ActiveOnlySearchService(SampleDbContext db, IMapper mapper, ICorrelationIdAccessor correlation)
        : base(db, mapper, correlation)
    {
    }

    /// <inheritdoc />
    protected override IQueryable<SampleEntity> BuildBaseQuery()
    {
        return Db.Set<SampleEntity>().AsNoTracking().Where(entity => entity.IsActive);
    }
}
