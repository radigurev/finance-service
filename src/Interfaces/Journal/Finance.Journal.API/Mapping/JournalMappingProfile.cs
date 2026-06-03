using AutoMapper;
using Finance.Journal.DBModel.Models;
using Finance.ServiceModel.Journal;

namespace Finance.Journal.API.Mapping;

/// <summary>
/// AutoMapper profile for the Journal service. Maps the <see cref="JournalEntry"/> aggregate and its
/// lines to their API DTOs, encoding the <c>rowversion</c> token as base64 for round-tripping. Contains
/// no domain logic.
/// </summary>
public sealed class JournalMappingProfile : Profile
{
    /// <summary>Configures mappings between the journal entities and their DTOs.</summary>
    public JournalMappingProfile()
    {
        CreateMap<JournalEntryLine, JournalEntryLineDto>();

        CreateMap<JournalEntry, JournalEntryDto>()
            .ForMember(
                dto => dto.RowVersion,
                options => options.MapFrom(entry => Convert.ToBase64String(entry.RowVersion)))
            .ForMember(
                dto => dto.Lines,
                options => options.MapFrom(entry => entry.Lines.OrderBy(line => line.LineNumber)));
    }
}
