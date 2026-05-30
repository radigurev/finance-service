using AutoMapper;

namespace Finance.Infrastructure.Tests.Services.Fixtures;

/// <summary>AutoMapper profile mapping <see cref="SampleEntity"/> to <see cref="SampleDto"/>.</summary>
public sealed class SampleMappingProfile : Profile
{
    /// <summary>Configures the sample entity-to-DTO mapping.</summary>
    public SampleMappingProfile()
    {
        CreateMap<SampleEntity, SampleDto>();
    }
}
