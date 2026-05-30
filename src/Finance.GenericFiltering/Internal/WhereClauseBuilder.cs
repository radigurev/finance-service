using System.Linq.Expressions;
using System.Reflection;
using Finance.Common.ErrorCodes;
using Finance.GenericFiltering.Exceptions;
using Finance.GenericFiltering.Models;

namespace Finance.GenericFiltering.Internal;

/// <summary>
/// Composes the entity <c>Where</c> predicate from all AND-combined filter criteria plus
/// the optional OR-LIKE free-text search clause, validating field opt-in and operator
/// applicability along the way.
/// </summary>
internal static class WhereClauseBuilder
{
    /// <summary>
    /// Applies all filter criteria and the search term to the source query.
    /// </summary>
    /// <typeparam name="T">The entity type.</typeparam>
    /// <param name="source">The source query.</param>
    /// <param name="request">The filter request.</param>
    /// <param name="metadata">The entity filter metadata.</param>
    /// <returns>The filtered query.</returns>
    public static IQueryable<T> Apply<T>(
        IQueryable<T> source,
        FilterRequest request,
        EntityFilterMetadata metadata)
    {
        ParameterExpression parameter = Expression.Parameter(typeof(T), "e");
        Expression? body = BuildFilters(request, metadata, parameter);

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            Expression? searchClause = SearchPredicateBuilder.Build<T>(
                parameter, metadata.Searchable, request.Search);
            body = Combine(body, searchClause);
        }

        if (body is null)
        {
            return source;
        }

        Expression<Func<T, bool>> predicate = Expression.Lambda<Func<T, bool>>(body, parameter);
        return source.Where(predicate);
    }

    private static Expression? BuildFilters(
        FilterRequest request,
        EntityFilterMetadata metadata,
        ParameterExpression parameter)
    {
        Expression? body = null;

        foreach (FilterCriterion criterion in request.Filters)
        {
            if (!metadata.TryGetFilterable(criterion.Field, out PropertyInfo property))
            {
                throw new FilterValidationException(
                    FilterErrorCodes.INVALID_FILTER_FIELD,
                    $"Field '{criterion.Field}' is not a filterable property.");
            }

            FilterOperator op = FilterOperatorParser.Parse(criterion.Operator, criterion.Field);
            FilterOperatorValidator.EnsureValid(op, property.PropertyType, criterion.Field);

            MemberExpression access = Expression.Property(parameter, property);
            Expression clause = FilterPredicateBuilder.Build(access, op, criterion);
            body = Combine(body, clause);
        }

        return body;
    }

    private static Expression? Combine(Expression? left, Expression? right)
    {
        if (left is null)
        {
            return right;
        }

        return right is null ? left : Expression.AndAlso(left, right);
    }
}
