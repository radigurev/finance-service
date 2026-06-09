using AutoMapper;
using Finance.Invoices.DBModel.Models;
using Finance.ServiceModel.Invoices;

namespace Finance.Invoices.API.Mapping;

/// <summary>
/// AutoMapper profile for the Invoices service. Maps the <see cref="Invoice"/> aggregate and its lines to
/// their API DTOs, encoding the <c>rowversion</c> token as base64 for round-tripping. Contains no domain
/// logic.
/// </summary>
public sealed class InvoiceMappingProfile : Profile
{
    /// <summary>Configures mappings between the invoice entities and their DTOs.</summary>
    public InvoiceMappingProfile()
    {
        CreateMap<InvoiceLine, InvoiceLineDto>();

        CreateMap<Invoice, InvoiceDto>()
            .ForMember(
                dto => dto.RowVersion,
                options => options.MapFrom(invoice => Convert.ToBase64String(invoice.RowVersion)))
            .ForMember(
                dto => dto.Lines,
                options => options.MapFrom(invoice => invoice.Lines.OrderBy(line => line.LineNumber)));
    }
}
