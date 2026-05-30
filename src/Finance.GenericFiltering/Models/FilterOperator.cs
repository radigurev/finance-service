namespace Finance.GenericFiltering.Models;

/// <summary>
/// The complete v1 operator set understood by <c>ApplyFilter</c>. Wire values are
/// the lowerCamelCase tokens accepted in a <see cref="FilterCriterion.Operator"/> string.
/// </summary>
public enum FilterOperator
{
    /// <summary>Equality (<c>==</c>). Supports <see langword="null"/> targets.</summary>
    Eq = 0,

    /// <summary>Inequality (<c>!=</c>).</summary>
    Neq = 1,

    /// <summary>Greater than (<c>&gt;</c>). Comparable types only.</summary>
    Gt = 2,

    /// <summary>Greater than or equal (<c>&gt;=</c>). Comparable types only.</summary>
    Gte = 3,

    /// <summary>Less than (<c>&lt;</c>). Comparable types only.</summary>
    Lt = 4,

    /// <summary>Less than or equal (<c>&lt;=</c>). Comparable types only.</summary>
    Lte = 5,

    /// <summary>Substring match. String properties only.</summary>
    Contains = 6,

    /// <summary>Prefix match. String properties only.</summary>
    StartsWith = 7,

    /// <summary>Suffix match. String properties only.</summary>
    EndsWith = 8,

    /// <summary>Membership test against an array of values.</summary>
    In = 9,

    /// <summary>Inclusive range test against a 2-element array. Comparable types only.</summary>
    Between = 10,

    /// <summary>Tests the property is <see langword="null"/>.</summary>
    IsNull = 11,

    /// <summary>Tests the property is not <see langword="null"/>.</summary>
    IsNotNull = 12
}
