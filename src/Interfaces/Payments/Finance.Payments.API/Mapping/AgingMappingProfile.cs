using AutoMapper;
using Finance.Payments.API.Services;
using Finance.Payments.DBModel.Models;
using Finance.ServiceModel.Payments;

namespace Finance.Payments.API.Mapping;

/// <summary>
/// AutoMapper profile for the SDD-PAY-003 aging read surface. It maps the projection-carried members of
/// <see cref="InvoiceOpenItem"/> onto <see cref="OpenItemDto"/> and NOTHING else.
/// <para>Every COMPUTED report member is deliberately ignored here and supplied by <see cref="AgingService"/>:
/// the as-of settled amount and the outstanding amount depend on the requested as-of date, the base-currency
/// amount goes through <c>ICountryStrategy.ApplyTaxRounding</c>, the days-past-due and bucket label come from the
/// pure <see cref="AgingBucketCalculator"/>, and the settlement status comes from the single
/// <see cref="SettlementStatusCalculator"/> that SDD-PAY-002 §2.8 forbids duplicating. The profile therefore
/// contains no domain logic at all.</para>
/// </summary>
public sealed class AgingMappingProfile : Profile
{
    /// <summary>Configures the open-item projection to report-DTO mapping.</summary>
    public AgingMappingProfile()
    {
        CreateMap<InvoiceOpenItem, OpenItemDto>()
            .ForMember(dto => dto.SettledAmount, options => options.Ignore())
            .ForMember(dto => dto.Outstanding, options => options.Ignore())
            .ForMember(dto => dto.BaseOutstanding, options => options.Ignore())
            .ForMember(dto => dto.DaysPastDue, options => options.Ignore())
            .ForMember(dto => dto.AgingBucket, options => options.Ignore())
            .ForMember(dto => dto.SettlementStatus, options => options.Ignore());
    }
}
