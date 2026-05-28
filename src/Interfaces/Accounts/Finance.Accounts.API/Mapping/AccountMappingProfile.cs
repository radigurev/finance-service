using AutoMapper;
using Finance.Accounts.DBModel.Models;
using Finance.ServiceModel.Accounts;

namespace Finance.Accounts.API.Mapping;

/// <summary>
/// AutoMapper profile for the Chart of Accounts service.
/// </summary>
public sealed class AccountMappingProfile : Profile
{
    /// <summary>Configures mappings between <see cref="Account"/> and <see cref="AccountDto"/>.</summary>
    public AccountMappingProfile()
    {
        CreateMap<Account, AccountDto>();
    }
}
