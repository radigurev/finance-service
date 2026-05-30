using AutoMapper;
using Finance.Common.Abstractions;
using Finance.Common.Results;
using Finance.Infrastructure.Services;

namespace Finance.Infrastructure.Tests.Services.Fixtures;

/// <summary>
/// Concrete <see cref="SearchableServiceBase{TEntity, TDto, TContext}"/> over <see cref="SampleEntity"/>,
/// exposing the protected <c>FindOrNotFoundAsync</c> for unit testing.
/// </summary>
public sealed class SampleSearchService : SearchableServiceBase<SampleEntity, SampleDto, SampleDbContext>
{
    /// <summary>Initializes the test search service.</summary>
    /// <param name="db">The SQLite-backed context.</param>
    /// <param name="mapper">The AutoMapper instance.</param>
    /// <param name="correlation">The correlation accessor.</param>
    public SampleSearchService(SampleDbContext db, IMapper mapper, ICorrelationIdAccessor correlation)
        : base(db, mapper, correlation)
    {
    }

    /// <summary>Exposes <c>FindOrNotFoundAsync</c> for testing.</summary>
    /// <param name="id">The primary key.</param>
    /// <param name="notFoundErrorCode">The not-found code.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The find result.</returns>
    public Task<Result<SampleEntity>> FindAsync(int id, string notFoundErrorCode, CancellationToken ct)
    {
        return FindOrNotFoundAsync<SampleEntity>(id, notFoundErrorCode, ct);
    }
}
