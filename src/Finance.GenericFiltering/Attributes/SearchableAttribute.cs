namespace Finance.GenericFiltering.Attributes;

/// <summary>
/// Marks a <see cref="string"/> entity property as eligible for the OR-LIKE
/// free-text search clause driven by <see cref="Finance.GenericFiltering.Models.FilterRequest.Search"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class SearchableAttribute : Attribute
{
}
