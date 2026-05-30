namespace Finance.Infrastructure.Caching.Interfaces;

/// <summary>
/// Builds cache keys and scan patterns that follow the Finance key convention
/// <c>{service}:{entity}:all|:{id}|:byCode:{code}|:filter:{sha256}</c> (SDD-INFRA-004 §2.1).
/// </summary>
public interface ICacheKeyBuilder
{
    /// <summary>The kebab-case service prefix this builder produces keys for (e.g., <c>finance-accounts</c>).</summary>
    string ServicePrefix { get; }

    /// <summary>Builds the full-collection key <c>{service}:{entity}:all</c>.</summary>
    /// <param name="entity">The entity segment (e.g., <c>chart</c>).</param>
    /// <returns>The collection cache key.</returns>
    string All(string entity);

    /// <summary>Builds the single-row-by-id key <c>{service}:{entity}:{id}</c>.</summary>
    /// <param name="entity">The entity segment.</param>
    /// <param name="id">The primary-key value.</param>
    /// <returns>The single-row cache key.</returns>
    string ById(string entity, object id);

    /// <summary>Builds the single-row-by-natural-key key <c>{service}:{entity}:byCode:{code}</c>.</summary>
    /// <param name="entity">The entity segment.</param>
    /// <param name="code">The natural-key value.</param>
    /// <returns>The by-code cache key.</returns>
    string ByCode(string entity, string code);

    /// <summary>
    /// Builds the filtered-list key <c>{service}:{entity}:filter:{sha256}</c> where the hash is a
    /// stable SHA-256 over the canonical ordering of <paramref name="filterParameters"/>.
    /// </summary>
    /// <param name="entity">The entity segment.</param>
    /// <param name="filterParameters">The filter parameters contributing to the stable hash.</param>
    /// <returns>The filtered-list cache key.</returns>
    string Filter(string entity, IReadOnlyDictionary<string, string?> filterParameters);

    /// <summary>
    /// Builds the bounded scan pattern <c>{service}:{entity}:*</c> for invalidating every key of an
    /// entity via <see cref="ICacheService{T}.RemoveByPatternAsync"/>.
    /// </summary>
    /// <param name="entity">The entity segment whose keys should be matched.</param>
    /// <returns>A scan pattern prefixed by the registered service segment.</returns>
    string EntityPattern(string entity);
}
