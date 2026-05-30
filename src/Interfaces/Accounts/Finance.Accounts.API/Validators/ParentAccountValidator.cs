using Finance.Accounts.DBModel;
using Finance.Common.ErrorCodes;
using Finance.Common.Validation;
using Finance.ServiceModel.Accounts;
using Microsoft.EntityFrameworkCore;

namespace Finance.Accounts.API.Validators;

/// <summary>
/// Cross-aggregate validator ensuring that, when a <c>ParentId</c> is supplied, the parent account
/// exists AND belongs to the configured country (SDD-ACCT-001 §2.3, §3.2). A missing or
/// cross-country parent yields <c>INVALID_PARENT_ACCOUNT</c>.
/// </summary>
public sealed class ParentAccountValidator : IChainValidator<CreateAccountRequest>
{
    private const string DefaultCountryCode = "BG";

    private readonly AccountsDbContext _db;
    private readonly IConfiguration _configuration;

    /// <summary>Creates a new <see cref="ParentAccountValidator"/>.</summary>
    /// <param name="db">The accounts database context.</param>
    /// <param name="configuration">Configuration carrying the owning <c>Country:Code</c>.</param>
    public ParentAccountValidator(AccountsDbContext db, IConfiguration configuration)
    {
        _db = db;
        _configuration = configuration;
    }

    /// <inheritdoc />
    public async Task<ChainValidationResult> ValidateAsync(
        CreateAccountRequest request,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.ParentId is null)
        {
            return ChainValidationResult.Success();
        }

        string countryCode = _configuration["Country:Code"] ?? DefaultCountryCode;
        int parentId = request.ParentId.Value;

        bool parentValid = await _db.Accounts
            .AsNoTracking()
            .AnyAsync(a => a.Id == parentId && a.CountryCode == countryCode, ct)
            .ConfigureAwait(false);

        if (!parentValid)
        {
            return ChainValidationResult.Failure(
                AccountErrorCodes.INVALID_PARENT_ACCOUNT,
                $"Parent account '{parentId}' does not exist in country '{countryCode}'.");
        }

        return ChainValidationResult.Success();
    }
}
