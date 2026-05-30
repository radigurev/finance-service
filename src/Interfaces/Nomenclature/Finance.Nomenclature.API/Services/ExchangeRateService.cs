using AutoMapper;
using AutoMapper.QueryableExtensions;
using Finance.Common.ErrorCodes;
using Finance.Common.Results;
using Finance.Nomenclature.API.Interfaces;
using Finance.Nomenclature.DBModel;
using Finance.ServiceModel.Nomenclature;
using Microsoft.EntityFrameworkCore;

namespace Finance.Nomenclature.API.Services;

/// <summary>
/// Read-only <see cref="IExchangeRateService"/> implementation (SDD-NOM-001 §2.2). Every query hits the
/// database directly and is never cached because exchange rates are transactional data (SDD-INFRA-004).
/// </summary>
public sealed class ExchangeRateService : IExchangeRateService
{
    private readonly NomenclatureDbContext _db;
    private readonly IMapper _mapper;

    /// <summary>Creates a new <see cref="ExchangeRateService"/>.</summary>
    /// <param name="db">The nomenclature database context.</param>
    /// <param name="mapper">The AutoMapper instance used for projection.</param>
    public ExchangeRateService(NomenclatureDbContext db, IMapper mapper)
    {
        _db = db;
        _mapper = mapper;
    }

    /// <inheritdoc />
    public async Task<Result<ExchangeRateDto>> GetLatestRateAsync(
        string isoCode,
        DateTimeOffset date,
        CancellationToken cancellationToken)
    {
        Result currencyCheck = await EnsureCurrencyExistsAsync(isoCode, cancellationToken).ConfigureAwait(false);
        if (!currencyCheck.IsSuccess)
        {
            return Result<ExchangeRateDto>.Failure(currencyCheck.ErrorCode!, currencyCheck.Detail);
        }

        ExchangeRateDto? rate = await _db.ExchangeRates
            .AsNoTracking()
            .Where(r => r.CurrencyIsoCode == isoCode && r.RateDate <= date)
            .OrderByDescending(r => r.RateDate)
            .ProjectTo<ExchangeRateDto>(_mapper.ConfigurationProvider)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (rate is null)
        {
            return Result<ExchangeRateDto>.Failure(NomenclatureErrorCodes.EXCHANGE_RATE_NOT_FOUND);
        }

        return Result<ExchangeRateDto>.Success(rate);
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<ExchangeRateDto>>> GetRateRangeAsync(
        string isoCode,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        if (from > to)
        {
            return Result<IReadOnlyList<ExchangeRateDto>>.Failure(
                NomenclatureErrorCodes.INVALID_DATE_RANGE,
                "The 'from' date must not be later than the 'to' date.");
        }

        Result currencyCheck = await EnsureCurrencyExistsAsync(isoCode, cancellationToken).ConfigureAwait(false);
        if (!currencyCheck.IsSuccess)
        {
            return Result<IReadOnlyList<ExchangeRateDto>>.Failure(currencyCheck.ErrorCode!, currencyCheck.Detail);
        }

        List<ExchangeRateDto> rates = await _db.ExchangeRates
            .AsNoTracking()
            .Where(r => r.CurrencyIsoCode == isoCode && r.RateDate >= from && r.RateDate <= to)
            .OrderBy(r => r.RateDate)
            .ProjectTo<ExchangeRateDto>(_mapper.ConfigurationProvider)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return Result<IReadOnlyList<ExchangeRateDto>>.Success(rates);
    }

    private async Task<Result> EnsureCurrencyExistsAsync(string isoCode, CancellationToken cancellationToken)
    {
        bool exists = await _db.Currencies
            .AsNoTracking()
            .AnyAsync(c => c.IsoCode == isoCode, cancellationToken)
            .ConfigureAwait(false);

        if (!exists)
        {
            return Result.Failure(NomenclatureErrorCodes.CURRENCY_NOT_FOUND);
        }

        return Result.Success();
    }
}
