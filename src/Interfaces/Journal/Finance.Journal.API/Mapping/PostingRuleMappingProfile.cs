using AutoMapper;
using Finance.Journal.DBModel.Models;
using Finance.ServiceModel.Posting;

namespace Finance.Journal.API.Mapping;

/// <summary>
/// AutoMapper profile for the Posting Rules domain (SDD-FIN-006). Maps the <see cref="PostingRule"/>
/// aggregate and its lines to their API DTOs, encoding the <c>rowversion</c> token as base64 and ordering
/// lines by <see cref="PostingRuleLine.LineNumber"/>. Contains no domain logic.
/// </summary>
public sealed class PostingRuleMappingProfile : Profile
{
    /// <summary>Configures mappings between the posting-rule entities and their DTOs.</summary>
    public PostingRuleMappingProfile()
    {
        CreateMap<PostingRuleLine, PostingRuleLineDto>();

        CreateMap<PostingRule, PostingRuleDto>()
            .ForMember(
                dto => dto.RowVersion,
                options => options.MapFrom(rule => Convert.ToBase64String(rule.RowVersion)))
            .ForMember(
                dto => dto.Lines,
                options => options.MapFrom(rule => rule.Lines.OrderBy(line => line.LineNumber)));
    }
}
