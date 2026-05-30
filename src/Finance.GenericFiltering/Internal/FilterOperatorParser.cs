using Finance.Common.ErrorCodes;
using Finance.GenericFiltering.Exceptions;
using Finance.GenericFiltering.Models;

namespace Finance.GenericFiltering.Internal;

/// <summary>
/// Parses wire operator tokens (e.g. <c>eq</c>, <c>startsWith</c>) into the strongly
/// typed <see cref="FilterOperator"/> enum, rejecting unknown tokens with
/// <see cref="FilterValidationException"/>.
/// </summary>
internal static class FilterOperatorParser
{
    private static readonly IReadOnlyDictionary<string, FilterOperator> Tokens =
        new Dictionary<string, FilterOperator>(StringComparer.OrdinalIgnoreCase)
        {
            ["eq"] = FilterOperator.Eq,
            ["neq"] = FilterOperator.Neq,
            ["gt"] = FilterOperator.Gt,
            ["gte"] = FilterOperator.Gte,
            ["lt"] = FilterOperator.Lt,
            ["lte"] = FilterOperator.Lte,
            ["contains"] = FilterOperator.Contains,
            ["startswith"] = FilterOperator.StartsWith,
            ["endswith"] = FilterOperator.EndsWith,
            ["in"] = FilterOperator.In,
            ["between"] = FilterOperator.Between,
            ["isnull"] = FilterOperator.IsNull,
            ["isnotnull"] = FilterOperator.IsNotNull
        };

    /// <summary>
    /// Resolves a wire operator token to a <see cref="FilterOperator"/>.
    /// </summary>
    /// <param name="token">The wire operator token.</param>
    /// <param name="field">The field the operator applies to, used in error detail.</param>
    /// <returns>The parsed <see cref="FilterOperator"/>.</returns>
    /// <exception cref="FilterValidationException">When the token is null, empty, or unrecognized.</exception>
    public static FilterOperator Parse(string? token, string field)
    {
        if (!string.IsNullOrWhiteSpace(token) && Tokens.TryGetValue(token, out FilterOperator parsed))
        {
            return parsed;
        }

        throw new FilterValidationException(
            FilterErrorCodes.INVALID_OPERATOR,
            $"Operator '{token}' is not a recognized filter operator for field '{field}'.");
    }
}
