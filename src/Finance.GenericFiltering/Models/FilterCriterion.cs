namespace Finance.GenericFiltering.Models;

/// <summary>
/// A single client-supplied filter clause. <see cref="Operator"/> is the wire token
/// (e.g. <c>eq</c>, <c>in</c>, <c>between</c>) and <see cref="Value"/> is the raw,
/// deserialized payload (scalar, array, or <see langword="null"/>) coerced to the
/// target property type during expression building.
/// </summary>
public sealed record FilterCriterion
{
    /// <summary>The entity property name being filtered. MUST be marked <c>[Filterable]</c>.</summary>
    public required string Field { get; init; }

    /// <summary>The filter operator wire token (e.g. <c>eq</c>, <c>contains</c>, <c>between</c>).</summary>
    public required string Operator { get; init; }

    /// <summary>The raw filter value. May be a scalar, an array (for <c>in</c> / <c>between</c>), or <see langword="null"/>.</summary>
    public object? Value { get; init; }
}
