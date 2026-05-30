using System.Globalization;
using System.Text.Json;
using Finance.Common.ErrorCodes;
using Finance.GenericFiltering.Exceptions;

namespace Finance.GenericFiltering.Internal;

/// <summary>
/// Coerces raw, deserialized filter values into the target property CLR type.
/// Dates use ISO-8601, enums match by name, and parse failures raise
/// <see cref="FilterValidationException"/> with <c>INVALID_FILTER_VALUE</c>.
/// </summary>
internal static class FilterValueConverter
{
    /// <summary>
    /// Converts a raw value to the requested target type (unwrapping nullables).
    /// </summary>
    /// <param name="rawValue">The raw deserialized value (scalar, string, or <see cref="JsonElement"/>).</param>
    /// <param name="targetType">The destination CLR type (may be a <see cref="Nullable{T}"/>).</param>
    /// <param name="field">The field name, used in error detail.</param>
    /// <returns>The converted value, boxed.</returns>
    /// <exception cref="FilterValidationException">When the value cannot be coerced.</exception>
    public static object Convert(object? rawValue, Type targetType, string field)
    {
        Type effectiveType = Nullable.GetUnderlyingType(targetType) ?? targetType;

        if (rawValue is null)
        {
            throw new FilterValidationException(
                FilterErrorCodes.INVALID_FILTER_VALUE,
                $"A non-null value is required for field '{field}'.");
        }

        string text = ExtractString(rawValue);

        try
        {
            return ConvertText(text, effectiveType, field);
        }
        catch (FilterValidationException)
        {
            throw;
        }
        catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException or ArgumentException)
        {
            throw new FilterValidationException(
                FilterErrorCodes.INVALID_FILTER_VALUE,
                $"Value '{text}' could not be parsed to type '{effectiveType.Name}' for field '{field}'.");
        }
    }

    private static object ConvertText(string text, Type effectiveType, string field)
    {
        if (effectiveType.IsEnum)
        {
            return ConvertEnum(text, effectiveType, field);
        }

        if (effectiveType == typeof(Guid))
        {
            return Guid.Parse(text);
        }

        if (effectiveType == typeof(DateTimeOffset))
        {
            return DateTimeOffset.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        }

        if (effectiveType == typeof(DateTime))
        {
            return DateTime.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        }

        if (effectiveType == typeof(string))
        {
            return text;
        }

        return System.Convert.ChangeType(text, effectiveType, CultureInfo.InvariantCulture);
    }

    private static object ConvertEnum(string text, Type enumType, string field)
    {
        if (Enum.TryParse(enumType, text, ignoreCase: true, out object? parsed) && parsed is not null
            && Enum.IsDefined(enumType, parsed))
        {
            return parsed;
        }

        throw new FilterValidationException(
            FilterErrorCodes.INVALID_FILTER_VALUE,
            $"Value '{text}' is not a defined '{enumType.Name}' member for field '{field}'.");
    }

    private static string ExtractString(object rawValue)
    {
        if (rawValue is JsonElement element)
        {
            return element.ValueKind == JsonValueKind.String
                ? element.GetString() ?? string.Empty
                : element.GetRawText();
        }

        return System.Convert.ToString(rawValue, CultureInfo.InvariantCulture) ?? string.Empty;
    }
}
