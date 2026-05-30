using System.Collections.Concurrent;
using System.Reflection;
using Finance.GenericFiltering.Attributes;

namespace Finance.GenericFiltering.Internal;

/// <summary>
/// Reflection metadata describing which properties of an entity type are filterable,
/// sortable, and searchable, plus the deterministic final-sort property. Instances are
/// cached per entity type to avoid repeated reflection.
/// </summary>
internal sealed class EntityFilterMetadata
{
    private static readonly ConcurrentDictionary<Type, EntityFilterMetadata> Cache = new();

    private readonly IReadOnlyDictionary<string, PropertyInfo> _filterable;
    private readonly IReadOnlyDictionary<string, PropertyInfo> _sortable;

    private EntityFilterMetadata(
        IReadOnlyDictionary<string, PropertyInfo> filterable,
        IReadOnlyDictionary<string, PropertyInfo> sortable,
        IReadOnlyList<PropertyInfo> searchable,
        PropertyInfo? finalSort)
    {
        _filterable = filterable;
        _sortable = sortable;
        Searchable = searchable;
        FinalSort = finalSort;
    }

    /// <summary>The string properties opted into the free-text search clause.</summary>
    public IReadOnlyList<PropertyInfo> Searchable { get; }

    /// <summary>The property used as the deterministic final sort key, or <see langword="null"/> if none exists.</summary>
    public PropertyInfo? FinalSort { get; }

    /// <summary>
    /// Gets (and caches) the filter metadata for the given entity type.
    /// </summary>
    /// <param name="entityType">The entity CLR type.</param>
    /// <returns>The cached metadata.</returns>
    public static EntityFilterMetadata For(Type entityType) =>
        Cache.GetOrAdd(entityType, Build);

    /// <summary>
    /// Resolves a filterable property by name (case-insensitive).
    /// </summary>
    /// <param name="field">The property name.</param>
    /// <param name="property">The resolved property, when found.</param>
    /// <returns><see langword="true"/> when the property exists and is filterable.</returns>
    public bool TryGetFilterable(string field, out PropertyInfo property) =>
        _filterable.TryGetValue(field, out property!);

    /// <summary>
    /// Resolves a sortable property by name (case-insensitive).
    /// </summary>
    /// <param name="field">The property name.</param>
    /// <param name="property">The resolved property, when found.</param>
    /// <returns><see langword="true"/> when the property exists and is sortable.</returns>
    public bool TryGetSortable(string field, out PropertyInfo property) =>
        _sortable.TryGetValue(field, out property!);

    private static EntityFilterMetadata Build(Type entityType)
    {
        PropertyInfo[] properties = entityType.GetProperties(BindingFlags.Public | BindingFlags.Instance);
        Dictionary<string, PropertyInfo> filterable = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, PropertyInfo> sortable = new(StringComparer.OrdinalIgnoreCase);
        List<PropertyInfo> searchable = [];

        foreach (PropertyInfo property in properties)
        {
            if (property.IsDefined(typeof(FilterableAttribute), inherit: true))
            {
                filterable[property.Name] = property;
            }

            if (property.IsDefined(typeof(SortableAttribute), inherit: true))
            {
                sortable[property.Name] = property;
            }

            if (property.PropertyType == typeof(string)
                && property.IsDefined(typeof(SearchableAttribute), inherit: true))
            {
                searchable.Add(property);
            }
        }

        PropertyInfo? finalSort = ResolveFinalSort(properties, sortable);
        return new EntityFilterMetadata(filterable, sortable, searchable, finalSort);
    }

    private static PropertyInfo? ResolveFinalSort(
        PropertyInfo[] properties,
        IReadOnlyDictionary<string, PropertyInfo> sortable)
    {
        PropertyInfo? id = Array.Find(
            properties,
            static p => string.Equals(p.Name, "Id", StringComparison.OrdinalIgnoreCase));

        if (id is not null)
        {
            return id;
        }

        return sortable.Count > 0 ? sortable.Values.First() : null;
    }
}
