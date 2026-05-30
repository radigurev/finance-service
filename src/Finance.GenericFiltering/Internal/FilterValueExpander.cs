using System.Collections;
using System.Text.Json;
using Finance.Common.ErrorCodes;
using Finance.GenericFiltering.Exceptions;

namespace Finance.GenericFiltering.Internal;

/// <summary>
/// Expands a raw filter value into a list of element values for the array-shaped
/// operators (<c>in</c>, <c>between</c>), supporting <see cref="JsonElement"/> arrays
/// and plain <see cref="IEnumerable"/> payloads.
/// </summary>
internal static class FilterValueExpander
{
    /// <summary>
    /// Expands a raw value into its element list. Throws when the value is not array-shaped.
    /// </summary>
    /// <param name="rawValue">The raw deserialized value.</param>
    /// <param name="field">The field name, used in error detail.</param>
    /// <returns>The expanded elements as raw (unconverted) objects.</returns>
    /// <exception cref="FilterValidationException">When the value is not an array.</exception>
    public static IReadOnlyList<object?> ToArray(object? rawValue, string field)
    {
        if (rawValue is JsonElement { ValueKind: JsonValueKind.Array } element)
        {
            List<object?> elements = [];
            foreach (JsonElement item in element.EnumerateArray())
            {
                elements.Add(item);
            }

            return elements;
        }

        if (rawValue is string)
        {
            throw NotAnArray(field);
        }

        if (rawValue is IEnumerable enumerable)
        {
            List<object?> elements = [];
            foreach (object? item in enumerable)
            {
                elements.Add(item);
            }

            return elements;
        }

        throw NotAnArray(field);
    }

    private static FilterValidationException NotAnArray(string field) =>
        new(
            FilterErrorCodes.INVALID_FILTER_VALUE,
            $"An array value is required for this operator on field '{field}'.");
}
