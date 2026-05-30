using AutoMapper;
using Finance.Common.Abstractions;
using Finance.Common.ErrorCodes;
using Finance.Common.Results;
using Microsoft.EntityFrameworkCore;

namespace Finance.Infrastructure.Services;

/// <summary>
/// Base class for Finance entity services, removing boilerplate around find-or-404,
/// map-and-save, and optimistic-concurrency translation. Mirrors Warehouse's
/// <c>BaseEntityService</c> per SDD-INFRA-009 §2.1.
/// </summary>
/// <typeparam name="TContext">The owning <see cref="DbContext"/> type for the microservice.</typeparam>
public abstract class BaseEntityService<TContext>
    where TContext : DbContext
{
    /// <summary>Initializes the base service with its context, mapper, and correlation accessor.</summary>
    /// <param name="db">The microservice's <see cref="DbContext"/>.</param>
    /// <param name="mapper">The AutoMapper instance used for entity-to-DTO projection.</param>
    /// <param name="correlation">The ambient correlation-id accessor.</param>
    protected BaseEntityService(TContext db, IMapper mapper, ICorrelationIdAccessor correlation)
    {
        Db = db;
        Mapper = mapper;
        Correlation = correlation;
    }

    /// <summary>The owning <see cref="DbContext"/> used to read and persist entities.</summary>
    protected TContext Db { get; }

    /// <summary>The AutoMapper instance used for entity-to-DTO mapping and projection.</summary>
    protected IMapper Mapper { get; }

    /// <summary>The ambient correlation-id accessor for the current operation.</summary>
    protected ICorrelationIdAccessor Correlation { get; }

    /// <summary>
    /// Loads an entity by integer key, returning <see cref="Result{T}.Failure(string, string?)"/> with
    /// the supplied domain code when it is absent.
    /// </summary>
    /// <typeparam name="TEntity">The entity type to load.</typeparam>
    /// <param name="id">The primary key value.</param>
    /// <param name="notFoundErrorCode">The domain-specific not-found error code (e.g. <c>ACCOUNT_NOT_FOUND</c>).</param>
    /// <param name="ct">A token to observe for cancellation.</param>
    /// <returns>A success result carrying the entity, or a failure with <paramref name="notFoundErrorCode"/>.</returns>
    protected async Task<Result<TEntity>> FindOrNotFoundAsync<TEntity>(
        int id, string notFoundErrorCode, CancellationToken ct)
        where TEntity : class
    {
        TEntity? entity = await Db.Set<TEntity>().FindAsync([id], ct).ConfigureAwait(false);
        if (entity is null)
        {
            return Result<TEntity>.Failure(notFoundErrorCode);
        }

        return Result<TEntity>.Success(entity);
    }

    /// <summary>
    /// Adds an entity, persists it, then maps the persisted entity to <typeparamref name="TDto"/>.
    /// </summary>
    /// <typeparam name="TEntity">The entity type being persisted.</typeparam>
    /// <typeparam name="TDto">The DTO type returned to the caller.</typeparam>
    /// <param name="entity">The new entity to persist.</param>
    /// <param name="ct">A token to observe for cancellation.</param>
    /// <returns>A success result carrying the mapped DTO, or a concurrency failure.</returns>
    protected async Task<Result<TDto>> MapAndSaveAsync<TEntity, TDto>(TEntity entity, CancellationToken ct)
        where TEntity : class
    {
        Db.Set<TEntity>().Add(entity);
        Result saveResult = await SaveWithConcurrencyCheckAsync(ct).ConfigureAwait(false);
        if (!saveResult.IsSuccess)
        {
            return Result<TDto>.Failure(saveResult.ErrorCode!, saveResult.Detail);
        }

        TDto dto = Mapper.Map<TDto>(entity);
        return Result<TDto>.Success(dto);
    }

    /// <summary>
    /// Persists pending changes, translating <see cref="DbUpdateConcurrencyException"/> into
    /// <see cref="CommonErrorCodes.CONCURRENT_MODIFICATION"/> per SDD-INFRA-009 §2.1.
    /// </summary>
    /// <param name="ct">A token to observe for cancellation.</param>
    /// <returns>A success result, or a concurrency failure.</returns>
    protected async Task<Result> SaveWithConcurrencyCheckAsync(CancellationToken ct)
    {
        try
        {
            await Db.SaveChangesAsync(ct).ConfigureAwait(false);
            return Result.Success();
        }
        catch (DbUpdateConcurrencyException ex)
        {
            return Result.Failure(CommonErrorCodes.CONCURRENT_MODIFICATION, ex.Message);
        }
    }
}
