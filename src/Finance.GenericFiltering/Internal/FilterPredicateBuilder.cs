using System.Linq.Expressions;
using System.Reflection;
using Finance.GenericFiltering.Models;

namespace Finance.GenericFiltering.Internal;

/// <summary>
/// Builds the boolean predicate <see cref="Expression"/> for a single filter criterion
/// against a property access expression. Each operator family is handled by a focused
/// helper to keep methods small and EF-translatable.
/// </summary>
internal static class FilterPredicateBuilder
{
    private static readonly MethodInfo StringContains =
        typeof(string).GetMethod(nameof(string.Contains), [typeof(string)])!;

    private static readonly MethodInfo StringStartsWith =
        typeof(string).GetMethod(nameof(string.StartsWith), [typeof(string)])!;

    private static readonly MethodInfo StringEndsWith =
        typeof(string).GetMethod(nameof(string.EndsWith), [typeof(string)])!;

    /// <summary>
    /// Builds the predicate body for one criterion.
    /// </summary>
    /// <param name="property">The member-access expression on the entity parameter.</param>
    /// <param name="op">The parsed operator.</param>
    /// <param name="criterion">The originating criterion (raw value, field name).</param>
    /// <returns>A boolean <see cref="Expression"/>.</returns>
    public static Expression Build(MemberExpression property, FilterOperator op, FilterCriterion criterion)
    {
        return op switch
        {
            FilterOperator.IsNull => Expression.Equal(property, NullConstant(property.Type)),
            FilterOperator.IsNotNull => Expression.NotEqual(property, NullConstant(property.Type)),
            FilterOperator.Contains => BuildStringCall(property, criterion, StringContains),
            FilterOperator.StartsWith => BuildStringCall(property, criterion, StringStartsWith),
            FilterOperator.EndsWith => BuildStringCall(property, criterion, StringEndsWith),
            FilterOperator.In => BuildIn(property, criterion),
            FilterOperator.Between => BuildBetween(property, criterion),
            _ => BuildComparison(property, op, criterion)
        };
    }

    private static Expression BuildComparison(MemberExpression property, FilterOperator op, FilterCriterion criterion)
    {
        object converted = FilterValueConverter.Convert(criterion.Value, property.Type, criterion.Field);
        ConstantExpression constant = TypedConstant(converted, property.Type);

        return op switch
        {
            FilterOperator.Eq => Expression.Equal(property, constant),
            FilterOperator.Neq => Expression.NotEqual(property, constant),
            FilterOperator.Gt => Expression.GreaterThan(property, constant),
            FilterOperator.Gte => Expression.GreaterThanOrEqual(property, constant),
            FilterOperator.Lt => Expression.LessThan(property, constant),
            FilterOperator.Lte => Expression.LessThanOrEqual(property, constant),
            _ => throw new InvalidOperationException($"Unhandled comparison operator '{op}'.")
        };
    }

    private static Expression BuildStringCall(MemberExpression property, FilterCriterion criterion, MethodInfo method)
    {
        object converted = FilterValueConverter.Convert(criterion.Value, typeof(string), criterion.Field);
        ConstantExpression argument = Expression.Constant(converted, typeof(string));
        return Expression.Call(property, method, argument);
    }

    private static Expression BuildIn(MemberExpression property, FilterCriterion criterion)
    {
        Type effectiveType = Nullable.GetUnderlyingType(property.Type) ?? property.Type;
        IReadOnlyList<object?> rawElements = FilterValueExpander.ToArray(criterion.Value, criterion.Field);
        Type listType = typeof(List<>).MakeGenericType(effectiveType);
        System.Collections.IList values = (System.Collections.IList)Activator.CreateInstance(listType)!;

        foreach (object? raw in rawElements)
        {
            values.Add(FilterValueConverter.Convert(raw, effectiveType, criterion.Field));
        }

        MethodInfo contains = ContainsMethodFor(effectiveType);
        Expression comparable = effectiveType == property.Type
            ? property
            : Expression.Convert(property, effectiveType);
        return Expression.Call(contains, Expression.Constant(values, listType), comparable);
    }

    private static Expression BuildBetween(MemberExpression property, FilterCriterion criterion)
    {
        IReadOnlyList<object?> bounds = FilterValueExpander.ToArray(criterion.Value, criterion.Field);
        if (bounds.Count != 2)
        {
            throw new Exceptions.FilterValidationException(
                Common.ErrorCodes.FilterErrorCodes.INVALID_FILTER_VALUE,
                $"The 'between' operator requires a 2-element array for field '{criterion.Field}'.");
        }

        ConstantExpression low = TypedConstant(
            FilterValueConverter.Convert(bounds[0], property.Type, criterion.Field), property.Type);
        ConstantExpression high = TypedConstant(
            FilterValueConverter.Convert(bounds[1], property.Type, criterion.Field), property.Type);

        return Expression.AndAlso(
            Expression.GreaterThanOrEqual(property, low),
            Expression.LessThanOrEqual(property, high));
    }

    private static MethodInfo ContainsMethodFor(Type elementType) =>
        typeof(Enumerable)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .First(m => m.Name == nameof(Enumerable.Contains) && m.GetParameters().Length == 2)
            .MakeGenericMethod(elementType);

    private static ConstantExpression TypedConstant(object value, Type targetType) =>
        Expression.Constant(value, targetType);

    private static ConstantExpression NullConstant(Type type) => Expression.Constant(null, type);
}
