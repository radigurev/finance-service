namespace Finance.Common.ErrorCodes;

/// <summary>
/// Machine-readable error codes for the reference-data (nomenclature) domain.
/// Used as the <c>title</c> field of ProblemDetails responses.
/// </summary>
public static class NomenclatureErrorCodes
{
    /// <summary>The supplied currency code is not a valid ISO 4217 code.</summary>
    public const string INVALID_CURRENCY_CODE = nameof(INVALID_CURRENCY_CODE);

    /// <summary>A currency with the supplied code already exists.</summary>
    public const string DUPLICATE_CURRENCY_CODE = nameof(DUPLICATE_CURRENCY_CODE);

    /// <summary>The referenced currency does not exist.</summary>
    public const string CURRENCY_NOT_FOUND = nameof(CURRENCY_NOT_FOUND);

    /// <summary>The Warehouse nomenclature service backing country/state/city lookups is unreachable.</summary>
    public const string WAREHOUSE_NOMENCLATURE_UNREACHABLE = nameof(WAREHOUSE_NOMENCLATURE_UNREACHABLE);
}
