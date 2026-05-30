using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Finance.Infrastructure.Caching.Interfaces;

namespace Finance.Infrastructure.Caching;

/// <summary>
/// Default <see cref="ICacheKeyBuilder"/> producing keys under a fixed service prefix following the
/// Finance key convention (SDD-INFRA-004 §2.1). The filter hash is a stable SHA-256 computed over a
/// case-insensitive, ordinally-sorted canonical query string so equivalent filters share a key.
/// </summary>
public sealed class CacheKeyBuilder : ICacheKeyBuilder
{
    private readonly string _servicePrefix;

    /// <summary>
    /// Initializes a new instance of the <see cref="CacheKeyBuilder"/> class.
    /// </summary>
    /// <param name="servicePrefix">The kebab-case service prefix (e.g., <c>finance-accounts</c>).</param>
    public CacheKeyBuilder(string servicePrefix)
    {
        if (string.IsNullOrWhiteSpace(servicePrefix))
        {
            throw new ArgumentException("Service prefix must be a non-empty value.", nameof(servicePrefix));
        }

        _servicePrefix = servicePrefix;
    }

    /// <inheritdoc />
    public string ServicePrefix => _servicePrefix;

    /// <inheritdoc />
    public string All(string entity)
    {
        return $"{_servicePrefix}:{RequireEntity(entity)}:all";
    }

    /// <inheritdoc />
    public string ById(string entity, object id)
    {
        ArgumentNullException.ThrowIfNull(id);
        return $"{_servicePrefix}:{RequireEntity(entity)}:{Convert.ToString(id, CultureInfo.InvariantCulture)}";
    }

    /// <inheritdoc />
    public string ByCode(string entity, string code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new ArgumentException("Code must be a non-empty value.", nameof(code));
        }

        return $"{_servicePrefix}:{RequireEntity(entity)}:byCode:{code}";
    }

    /// <inheritdoc />
    public string Filter(string entity, IReadOnlyDictionary<string, string?> filterParameters)
    {
        ArgumentNullException.ThrowIfNull(filterParameters);
        string hash = ComputeStableHash(filterParameters);
        return $"{_servicePrefix}:{RequireEntity(entity)}:filter:{hash}";
    }

    /// <inheritdoc />
    public string EntityPattern(string entity)
    {
        return $"{_servicePrefix}:{RequireEntity(entity)}:*";
    }

    /// <summary>Validates and returns the entity segment.</summary>
    /// <param name="entity">The entity segment to validate.</param>
    /// <returns>The validated entity segment.</returns>
    private static string RequireEntity(string entity)
    {
        if (string.IsNullOrWhiteSpace(entity))
        {
            throw new ArgumentException("Entity segment must be a non-empty value.", nameof(entity));
        }

        return entity;
    }

    /// <summary>
    /// Computes a deterministic SHA-256 hex digest over a canonical ordering of the filter parameters,
    /// so that two equivalent filters always map to the same key (SDD-INFRA-004 §2.1).
    /// </summary>
    /// <param name="filterParameters">The filter parameters to hash.</param>
    /// <returns>A lowercase hexadecimal SHA-256 digest.</returns>
    private static string ComputeStableHash(IReadOnlyDictionary<string, string?> filterParameters)
    {
        IEnumerable<string> canonicalPairs = filterParameters
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => $"{pair.Key}={pair.Value}");

        string canonical = string.Join("&", canonicalPairs);
        byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexString(digest).ToLowerInvariant();
    }
}
