using Finance.GenericFiltering.Attributes;

namespace Finance.Infrastructure.Tests.Services.Fixtures;

/// <summary>A sample EF Core entity used by the service-layer tests.</summary>
public sealed class SampleEntity
{
    /// <summary>The integer primary key.</summary>
    public int Id { get; set; }

    /// <summary>A filterable and sortable name.</summary>
    [Filterable]
    [Sortable]
    public string Name { get; set; } = string.Empty;

    /// <summary>A filterable active flag used by the base-query-override test.</summary>
    [Filterable]
    public bool IsActive { get; set; }
}
