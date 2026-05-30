using Finance.Accounts.DBModel;
using Finance.Common.ErrorCodes;
using Finance.Common.Validation;
using Finance.ServiceModel.Accounts;
using Microsoft.EntityFrameworkCore;

namespace Finance.Accounts.API.Validators;

/// <summary>
/// Cross-aggregate validator ensuring an account <c>Code</c> is unique within the configured
/// country's chart (SDD-ACCT-001 §2.3, §3.2). A clash yields <c>DUPLICATE_ACCOUNT_CODE</c>.
/// </summary>
public sealed class DuplicateAccountCodeValidator : IChainValidator<CreateAccountRequest>
{
    private const string DefaultCountryCode = "BG";

    private readonly AccountsDbContext _db;
    private readonly IConfiguration _configuration;

    /// <summary>Creates a new <see cref="DuplicateAccountCodeValidator"/>.</summary>
    /// <param name="db">The accounts database context.</param>
    /// <param name="configuration">Configuration carrying the owning <c>Country:Code</c>.</param>
    public DuplicateAccountCodeValidator(AccountsDbContext db, IConfiguration configuration)
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

        string countryCode = _configuration["Country:Code"] ?? DefaultCountryCode;

        bool exists = await _db.Accounts
            .AsNoTracking()
            .AnyAsync(a => a.CountryCode == countryCode && a.Code == request.Code, ct)
            .ConfigureAwait(false);

        if (exists)
        {
            return ChainValidationResult.Failure(
                AccountErrorCodes.DUPLICATE_ACCOUNT_CODE,
                $"An account with code '{request.Code}' already exists in country '{countryCode}'.");
        }

        return ChainValidationResult.Success();
    }
}
