using AutoMapper;
using Finance.Periods.DBModel.Models;
using Finance.ServiceModel.Periods;

namespace Finance.Periods.API.Mapping;

/// <summary>
/// AutoMapper profile for the Periods service. Maps the <see cref="FiscalPeriod"/> aggregate to its API DTO,
/// encoding the <c>rowversion</c> token as base64 for round-tripping. Contains no domain logic.
/// </summary>
public sealed class PeriodMappingProfile : Profile
{
    /// <summary>Configures mappings between the fiscal-period entity and its DTO.</summary>
    public PeriodMappingProfile()
    {
        CreateMap<FiscalPeriod, FiscalPeriodDto>()
            .ForMember(
                dto => dto.RowVersion,
                options => options.MapFrom(period => Convert.ToBase64String(period.RowVersion)));
    }
}
