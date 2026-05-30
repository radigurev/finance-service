using System.Linq.Expressions;
using System.Reflection;
using Finance.Common.ErrorCodes;
using Finance.GenericFiltering.Exceptions;
using Finance.GenericFiltering.Models;

namespace Finance.GenericFiltering.Internal;

/// <summary>
/// Applies the client-supplied sort terms followed by a deterministic final sort key,
/// validating each requested field against the <c>[Sortable]</c> metadata.
/// </summary>
internal static class SortApplier
{
    /// <summary>
    /// Applies ordering to the source query, always appending the deterministic final key.
    /// </summary>
    /// <typeparam name="T">The entity type.</typeparam>
    /// <param name="source">The source query.</param>
    /// <param name="sort">The requested sort criteria.</param>
    /// <param name="metadata">The entity filter metadata.</param>
    /// <returns>The ordered query.</returns>
    public static IQueryable<T> Apply<T>(
        IQueryable<T> source,
        IReadOnlyList<SortCriterion> sort,
        EntityFilterMetadata metadata)
    {
        IOrderedQueryable<T>? ordered = null;

        foreach (SortCriterion criterion in sort)
        {
            if (!metadata.TryGetSortable(criterion.Field, out PropertyInfo property))
            {
                throw new FilterValidationException(
                    FilterErrorCodes.INVALID_SORT_FIELD,
                    $"Field '{criterion.Field}' is not a sortable property.");
            }

            bool descending = string.Equals(criterion.Direction, "desc", StringComparison.OrdinalIgnoreCase);
            ordered = ApplyTerm(source, ordered, property, descending);
        }

        if (metadata.FinalSort is not null)
        {
            ordered = ApplyTerm(source, ordered, metadata.FinalSort, descending: false);
        }

        return ordered ?? source;
    }

    private static IOrderedQueryable<T> ApplyTerm<T>(
        IQueryable<T> source,
        IOrderedQueryable<T>? ordered,
        PropertyInfo property,
        bool descending)
    {
        ParameterExpression parameter = Expression.Parameter(typeof(T), "e");
        MemberExpression access = Expression.Property(parameter, property);
        LambdaExpression selector = Expression.Lambda(access, parameter);

        string method = ResolveMethodName(ordered is null, descending);
        MethodCallExpression call = Expression.Call(
            typeof(Queryable),
            method,
            [typeof(T), property.PropertyType],
            (ordered ?? source).Expression,
            Expression.Quote(selector));

        return (IOrderedQueryable<T>)(ordered ?? source).Provider.CreateQuery<T>(call);
    }

    private static string ResolveMethodName(bool first, bool descending)
    {
        if (first)
        {
            return descending ? nameof(Queryable.OrderByDescending) : nameof(Queryable.OrderBy);
        }

        return descending ? nameof(Queryable.ThenByDescending) : nameof(Queryable.ThenBy);
    }
}
