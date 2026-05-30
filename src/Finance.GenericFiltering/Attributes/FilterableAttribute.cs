namespace Finance.GenericFiltering.Attributes;

/// <summary>
/// Marks an entity property as eligible for client-supplied filtering.
/// Only properties decorated with this attribute may appear in
/// <see cref="Finance.GenericFiltering.Models.FilterRequest.Filters"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class FilterableAttribute : Attribute
{
}
