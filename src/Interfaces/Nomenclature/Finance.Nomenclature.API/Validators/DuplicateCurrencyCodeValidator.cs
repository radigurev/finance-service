using Finance.Common.ErrorCodes;
using Finance.Common.Validation;
using Finance.Nomenclature.DBModel;
using Finance.ServiceModel.Nomenclature;
using Microsoft.EntityFrameworkCore;

namespace Finance.Nomenclature.API.Validators;

/// <summary>
/// Cross-aggregate validator ensuring a currency <c>IsoCode</c> is unique across the catalogue
/// (SDD-NOM-001 §2.1, §3). A clash yields <c>DUPLICATE_CURRENCY_CODE</c> (409).
/// </summary>
public sealed class DuplicateCurrencyCodeValidator : IChainValidator<CreateCurrencyRequest>
{
    private readonly NomenclatureDbContext _db;

    /// <summary>Creates a new <see cref="DuplicateCurrencyCodeValidator"/>.</summary>
    /// <param name="db">The nomenclature database context.</param>
    public DuplicateCurrencyCodeValidator(NomenclatureDbContext db)
    {
        _db = db;
    }

    /// <inheritdoc />
    public async Task<ChainValidationResult> ValidateAsync(CreateCurrencyRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        bool exists = await _db.Currencies
            .AsNoTracking()
            .AnyAsync(c => c.IsoCode == request.IsoCode, ct)
            .ConfigureAwait(false);

        if (exists)
        {
            return ChainValidationResult.Failure(
                NomenclatureErrorCodes.DUPLICATE_CURRENCY_CODE,
                $"A currency with code '{request.IsoCode}' already exists.");
        }

        return ChainValidationResult.Success();
    }
}
