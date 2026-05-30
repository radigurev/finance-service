using Finance.Common.ErrorCodes;
using Finance.Common.Results;
using Finance.Infrastructure.Caching;
using Finance.Infrastructure.Caching.Interfaces;
using Finance.Nomenclature.API.Caching;
using Finance.Nomenclature.API.Interfaces;
using Finance.ServiceModel.Nomenclature;
using Microsoft.Extensions.Logging;
using Polly.Timeout;
using Refit;

namespace Finance.Nomenclature.API.Services;

/// <summary>
/// Default <see cref="IWarehouseProxyService"/> implementation (SDD-NOM-001 §2.3). Reads country /
/// state / city reference data through the <see cref="IWarehouseNomenclatureClient"/> Refit client,
/// caching each query for 30 minutes (SDD-INFRA-004). When the upstream Warehouse service is
/// unreachable the service returns a <c>WAREHOUSE_NOMENCLATURE_UNREACHABLE</c> failure (503) rather than
/// throwing, so the proxy degrades gracefully.
/// </summary>
public sealed class WarehouseProxyService : IWarehouseProxyService
{
    private const string UpstreamUnreachableDetail =
        "The Warehouse Nomenclature service backing country/state/city lookups is unreachable.";

    private readonly IWarehouseNomenclatureClient _client;
    private readonly ICacheService<IReadOnlyList<CountryDto>> _countryCache;
    private readonly ICacheService<IReadOnlyList<StateDto>> _stateCache;
    private readonly ICacheService<IReadOnlyList<CityDto>> _cityCache;
    private readonly ILogger<WarehouseProxyService> _logger;

    /// <summary>Creates a new <see cref="WarehouseProxyService"/>.</summary>
    /// <param name="client">The Refit client targeting Warehouse Nomenclature through the Warehouse Gateway.</param>
    /// <param name="countryCache">The 30-minute reference cache for the country list.</param>
    /// <param name="stateCache">The 30-minute reference cache for per-country states.</param>
    /// <param name="cityCache">The 30-minute reference cache for per-state cities.</param>
    /// <param name="logger">Logger used to record upstream-unreachable failures.</param>
    public WarehouseProxyService(
        IWarehouseNomenclatureClient client,
        ICacheService<IReadOnlyList<CountryDto>> countryCache,
        ICacheService<IReadOnlyList<StateDto>> stateCache,
        ICacheService<IReadOnlyList<CityDto>> cityCache,
        ILogger<WarehouseProxyService> logger)
    {
        _client = client;
        _countryCache = countryCache;
        _stateCache = stateCache;
        _cityCache = cityCache;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task<Result<IReadOnlyList<CountryDto>>> GetCountriesAsync(CancellationToken cancellationToken)
    {
        return FetchAsync(
            WarehouseProxyCacheKeys.Countries,
            _countryCache,
            _client.GetCountriesAsync,
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<Result<IReadOnlyList<StateDto>>> GetStatesAsync(
        string countryIso,
        CancellationToken cancellationToken)
    {
        return FetchAsync(
            WarehouseProxyCacheKeys.StatesForCountry(countryIso),
            _stateCache,
            ct => _client.GetStatesAsync(countryIso, ct),
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<Result<IReadOnlyList<CityDto>>> GetCitiesAsync(
        int stateId,
        CancellationToken cancellationToken)
    {
        return FetchAsync(
            WarehouseProxyCacheKeys.CitiesForState(stateId),
            _cityCache,
            ct => _client.GetCitiesAsync(stateId, ct),
            cancellationToken);
    }

    private async Task<Result<IReadOnlyList<T>>> FetchAsync<T>(
        string cacheKey,
        ICacheService<IReadOnlyList<T>> cache,
        Func<CancellationToken, Task<IReadOnlyList<T>>> upstream,
        CancellationToken cancellationToken)
    {
        try
        {
            IReadOnlyList<T>? results = await cache.GetOrSetAsync(
                cacheKey,
                async ct => await upstream(ct).ConfigureAwait(false),
                CacheTtl.ReferenceData,
                cancellationToken).ConfigureAwait(false);

            return Result<IReadOnlyList<T>>.Success(results ?? []);
        }
        catch (Exception exception) when (IsUpstreamFailure(exception))
        {
            _logger.LogWarning(
                exception,
                "Warehouse Nomenclature proxy unreachable for {CacheKey}. Code={ErrorCode}",
                cacheKey,
                NomenclatureErrorCodes.WAREHOUSE_NOMENCLATURE_UNREACHABLE);

            return Result<IReadOnlyList<T>>.Failure(
                NomenclatureErrorCodes.WAREHOUSE_NOMENCLATURE_UNREACHABLE,
                UpstreamUnreachableDetail);
        }
    }

    private static bool IsUpstreamFailure(Exception exception) =>
        exception is ApiException
            or HttpRequestException
            or TimeoutRejectedException
            or TaskCanceledException;
}
