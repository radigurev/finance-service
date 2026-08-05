using AutoMapper;
using Finance.Payments.API.Mapping;

namespace Finance.Payments.API.Tests.Fixtures;

/// <summary>
/// Builds the AutoMapper instance the Payments unit tests share, carrying the two shipped profiles
/// (<see cref="PaymentMappingProfile"/> and <see cref="AgingMappingProfile"/>) so DTO projection is the
/// production one.
/// </summary>
public static class PaymentTestMapper
{
    /// <summary>Creates a mapper carrying both shipped payment profiles.</summary>
    /// <returns>The configured mapper.</returns>
    public static IMapper Create()
    {
        return new MapperConfiguration(configuration =>
        {
            configuration.AddProfile<PaymentMappingProfile>();
            configuration.AddProfile<AgingMappingProfile>();
        }).CreateMapper();
    }
}
