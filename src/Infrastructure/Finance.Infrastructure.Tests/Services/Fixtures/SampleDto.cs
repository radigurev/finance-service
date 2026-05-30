namespace Finance.Infrastructure.Tests.Services.Fixtures;

/// <summary>A sample DTO projected from <see cref="SampleEntity"/> in the service-layer tests.</summary>
public sealed record SampleDto
{
    /// <summary>The integer key copied from the entity.</summary>
    public required int Id { get; init; }

    /// <summary>The name copied from the entity.</summary>
    public required string Name { get; init; }
}
