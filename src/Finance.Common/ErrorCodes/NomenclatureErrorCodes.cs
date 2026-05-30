namespace Finance.Common.ErrorCodes;

/// <summary>
/// Machine-readable error codes for the reference-data (nomenclature) domain.
/// Used as the <c>title</c> field of ProblemDetails responses.
/// </summary>
public static class NomenclatureErrorCodes
{
    /// <summary>The supplied currency code is not exactly three uppercase letters (ISO 4217 alphabetic).</summary>
    public const string INVALID_CURRENCY_CODE = nameof(INVALID_CURRENCY_CODE);

    /// <summary>The supplied currency name is empty or exceeds the allowed length.</summary>
    public const string INVALID_CURRENCY_NAME = nameof(INVALID_CURRENCY_NAME);

    /// <summary>The supplied currency symbol exceeds the allowed length.</summary>
    public const string INVALID_CURRENCY_SYMBOL = nameof(INVALID_CURRENCY_SYMBOL);

    /// <summary>The exchange-rate range query has <c>from</c> later than <c>to</c>.</summary>
    public const string INVALID_DATE_RANGE = nameof(INVALID_DATE_RANGE);

    /// <summary>A currency with the supplied code already exists.</summary>
    public const string DUPLICATE_CURRENCY_CODE = nameof(DUPLICATE_CURRENCY_CODE);

    /// <summary>The referenced currency does not exist.</summary>
    public const string CURRENCY_NOT_FOUND = nameof(CURRENCY_NOT_FOUND);

    /// <summary>No exchange rate exists on or before the requested date.</summary>
    public const string EXCHANGE_RATE_NOT_FOUND = nameof(EXCHANGE_RATE_NOT_FOUND);

    /// <summary>The Warehouse nomenclature service backing country/state/city lookups is unreachable.</summary>
    public const string WAREHOUSE_NOMENCLATURE_UNREACHABLE = nameof(WAREHOUSE_NOMENCLATURE_UNREACHABLE);
}
