using AutoMapper;
using Finance.Payments.API.Services;
using Finance.Payments.DBModel.Models;
using Finance.ServiceModel.Payments;

namespace Finance.Payments.API.Mapping;

/// <summary>
/// AutoMapper profile for the Payments service. Maps the <see cref="Payment"/> aggregate to its API DTO,
/// encoding the <c>rowversion</c> token as base64 for round-tripping and projecting the computed
/// <c>UnallocatedAmount</c> from <c>Amount − AllocatedAmount</c> so the expression translates inside
/// <c>ProjectTo</c> (the entity property itself is ignored by EF). It also maps the SDD-PAY-002 allocation list
/// row — the allocation joined to its local invoice open item — onto <see cref="PaymentAllocationDto"/>.
/// <para>Contains no domain logic. In particular the DERIVED settlement status is deliberately IGNORED here and
/// supplied by the single <see cref="SettlementStatusCalculator"/>: SDD-PAY-002 §2.8 forbids duplicating the
/// derivation in the DTO mapper.</para>
/// </summary>
public sealed class PaymentMappingProfile : Profile
{
    /// <summary>Configures mappings between the payment entities and their DTOs.</summary>
    public PaymentMappingProfile()
    {
        CreateMap<Payment, PaymentDto>()
            .ForMember(
                dto => dto.RowVersion,
                options => options.MapFrom(payment => Convert.ToBase64String(payment.RowVersion)))
            .ForMember(
                dto => dto.UnallocatedAmount,
                options => options.MapFrom(payment => payment.Amount - payment.AllocatedAmount));

        CreateMap<PaymentAllocationProjectionRow, PaymentAllocationDto>()
            .ForMember(dto => dto.Id, options => options.MapFrom(row => row.Allocation.Id))
            .ForMember(dto => dto.PaymentId, options => options.MapFrom(row => row.Allocation.PaymentId))
            .ForMember(dto => dto.InvoiceId, options => options.MapFrom(row => row.Allocation.InvoiceId))
            .ForMember(
                dto => dto.AllocatedAmount,
                options => options.MapFrom(row => row.Allocation.AllocatedAmount))
            .ForMember(
                dto => dto.BaseAllocatedAmount,
                options => options.MapFrom(row => row.Allocation.BaseAllocatedAmount))
            .ForMember(
                dto => dto.RealizedFxDifference,
                options => options.MapFrom(row => row.Allocation.RealizedFxDifference))
            .ForMember(dto => dto.AllocatedAt, options => options.MapFrom(row => row.Allocation.AllocatedAt))
            .ForMember(
                dto => dto.InvoiceDocumentNumber,
                options => options.MapFrom(row => row.OpenItem == null ? null : row.OpenItem.DocumentNumber))
            .ForMember(
                dto => dto.InvoiceDueDate,
                options => options.MapFrom(row =>
                    row.OpenItem == null ? (DateTimeOffset?)null : row.OpenItem.DueDate))
            .ForMember(
                dto => dto.InvoiceStatus,
                options => options.MapFrom(row => row.OpenItem == null ? null : row.OpenItem.InvoiceStatus))
            .ForMember(
                dto => dto.InvoiceGrossTotal,
                options => options.MapFrom(row => row.OpenItem == null ? (decimal?)null : row.OpenItem.GrossTotal))
            .ForMember(dto => dto.InvoiceSettlementStatus, options => options.Ignore());
    }
}
