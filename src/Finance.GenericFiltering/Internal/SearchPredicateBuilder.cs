using System.Linq.Expressions;
using System.Reflection;

namespace Finance.GenericFiltering.Internal;

/// <summary>
/// Builds the free-text search predicate: an OR of <c>Contains</c> calls across every
/// <c>[Searchable]</c> string property, guarding each access against <see langword="null"/>.
/// </summary>
internal static class SearchPredicateBuilder
{
    private static readonly MethodInfo StringContains =
        typeof(string).GetMethod(nameof(string.Contains), [typeof(string)])!;

    /// <summary>
    /// Builds the OR-LIKE search predicate, or <see langword="null"/> when there is nothing to search.
    /// </summary>
    /// <typeparam name="T">The entity type.</typeparam>
    /// <param name="parameter">The lambda parameter expression for the entity.</param>
    /// <param name="searchable">The searchable string properties.</param>
    /// <param name="term">The free-text search term.</param>
    /// <returns>A boolean <see cref="Expression"/>, or <see langword="null"/> when no clause applies.</returns>
    public static Expression? Build<T>(
        ParameterExpression parameter,
        IReadOnlyList<PropertyInfo> searchable,
        string term)
    {
        if (searchable.Count == 0 || string.IsNullOrWhiteSpace(term))
        {
            return null;
        }

        ConstantExpression termConstant = Expression.Constant(term, typeof(string));
        Expression? combined = null;

        foreach (PropertyInfo property in searchable)
        {
            MemberExpression access = Expression.Property(parameter, property);
            BinaryExpression notNull = Expression.NotEqual(access, Expression.Constant(null, typeof(string)));
            MethodCallExpression contains = Expression.Call(access, StringContains, termConstant);
            BinaryExpression guarded = Expression.AndAlso(notNull, contains);

            combined = combined is null ? guarded : Expression.OrElse(combined, guarded);
        }

        return combined;
    }
}
