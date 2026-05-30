using Finance.Common.ErrorCodes;
using Finance.GenericFiltering.Exceptions;
using Finance.GenericFiltering.Models;

namespace Finance.GenericFiltering.Internal;

/// <summary>
/// Validates that a <see cref="FilterOperator"/> is applicable to the CLR type of the
/// target property, rejecting mismatches (e.g. <c>contains</c> on <see cref="decimal"/>)
/// with <c>INVALID_OPERATOR</c>.
/// </summary>
internal static class FilterOperatorValidator
{
    /// <summary>
    /// Ensures the operator is valid for the property type, or throws.
    /// </summary>
    /// <param name="op">The parsed operator.</param>
    /// <param name="propertyType">The target property type (possibly nullable).</param>
    /// <param name="field">The field name, used in error detail.</param>
    /// <exception cref="FilterValidationException">When the operator is not valid for the type.</exception>
    public static void EnsureValid(FilterOperator op, Type propertyType, string field)
    {
        Type effectiveType = Nullable.GetUnderlyingType(propertyType) ?? propertyType;

        if (IsValid(op, effectiveType))
        {
            return;
        }

        throw new FilterValidationException(
            FilterErrorCodes.INVALID_OPERATOR,
            $"Operator '{op}' is not valid for field '{field}' of type '{effectiveType.Name}'.");
    }

    private static bool IsValid(FilterOperator op, Type effectiveType) => op switch
    {
        FilterOperator.Eq or FilterOperator.Neq or FilterOperator.In
            or FilterOperator.IsNull or FilterOperator.IsNotNull => true,
        FilterOperator.Contains or FilterOperator.StartsWith or FilterOperator.EndsWith =>
            effectiveType == typeof(string),
        FilterOperator.Gt or FilterOperator.Gte or FilterOperator.Lt
            or FilterOperator.Lte or FilterOperator.Between => IsComparable(effectiveType),
        _ => false
    };

    private static bool IsComparable(Type effectiveType)
    {
        if (effectiveType == typeof(bool) || effectiveType == typeof(string))
        {
            return false;
        }

        return effectiveType.IsEnum
            || effectiveType == typeof(int)
            || effectiveType == typeof(long)
            || effectiveType == typeof(decimal)
            || effectiveType == typeof(double)
            || effectiveType == typeof(float)
            || effectiveType == typeof(short)
            || effectiveType == typeof(byte)
            || effectiveType == typeof(DateTimeOffset)
            || effectiveType == typeof(DateTime)
            || effectiveType == typeof(Guid);
    }
}
