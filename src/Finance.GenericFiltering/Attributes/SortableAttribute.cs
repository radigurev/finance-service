namespace Finance.GenericFiltering.Attributes;

/// <summary>
/// Marks an entity property as eligible for client-supplied sorting.
/// Only properties decorated with this attribute may appear in
/// <see cref="Finance.GenericFiltering.Models.FilterRequest.Sort"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class SortableAttribute : Attribute
{
}
