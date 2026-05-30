using AutoMapper;
using Finance.Nomenclature.DBModel.Models;
using Finance.ServiceModel.Nomenclature;

namespace Finance.Nomenclature.API.Mapping;

/// <summary>
/// AutoMapper profile for the Nomenclature service. Maps entities to DTOs; the <c>RowVersion</c>
/// byte array is projected to a base64 string for optimistic-concurrency round-tripping.
/// </summary>
public sealed class NomenclatureMappingProfile : Profile
{
    /// <summary>Configures the entity-to-DTO mappings owned by the Nomenclature service.</summary>
    public NomenclatureMappingProfile()
    {
        CreateMap<Currency, CurrencyDto>()
            .ForMember(
                dto => dto.RowVersion,
                options => options.MapFrom(currency => Convert.ToBase64String(currency.RowVersion)));

        CreateMap<ExchangeRate, ExchangeRateDto>();
    }
}
