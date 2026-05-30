using AutoMapper;
using Finance.Accounts.API.Mapping;
using Finance.Accounts.API.Tests.Builders;
using Finance.Accounts.DBModel.Models;
using Finance.Common.Enums;
using Finance.ServiceModel.Accounts;
using NUnit.Framework;

namespace Finance.Accounts.API.Tests.Unit.Mapping;

/// <summary>
/// Unit tests for <see cref="AccountMappingProfile"/> (SDD-ACCT-001 §2.10). Verifies the configuration is
/// internally valid and that the RowVersion byte array is projected to a base64 string for round-tripping.
/// </summary>
[TestFixture]
[Category("SDD-ACCT-001")]
public sealed class AccountMappingProfileTests
{
    private IMapper _mapper = null!;

    /// <summary>Builds a mapper from the profile under test before each test.</summary>
    [SetUp]
    public void SetUp()
    {
        _mapper = new MapperConfiguration(cfg => cfg.AddProfile<AccountMappingProfile>()).CreateMapper();
    }

    /// <summary>The AutoMapper configuration is internally consistent.</summary>
    [Test]
    public void Configuration_IsValid()
    {
        // Arrange & Act & Assert
        Assert.That(() => _mapper.ConfigurationProvider.AssertConfigurationIsValid(), Throws.Nothing);
    }

    /// <summary>An account maps to a DTO with matching scalar fields and a base64 RowVersion.</summary>
    [Test]
    public void Map_AccountToDto_MapsScalarsAndBase64RowVersion()
    {
        // Arrange
        Account account = AccountBuilder.Create()
            .WithCode("401")
            .WithName("Доставчици")
            .WithType(AccountType.Liability)
            .WithCountryCode("BG")
            .Build();
        account.Id = 42;
        account.RowVersion = [1, 2, 3, 4, 5, 6, 7, 8];

        // Act
        AccountDto dto = _mapper.Map<AccountDto>(account);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(dto.Id, Is.EqualTo(42));
            Assert.That(dto.Code, Is.EqualTo("401"));
            Assert.That(dto.Name, Is.EqualTo("Доставчици"));
            Assert.That(dto.Type, Is.EqualTo(AccountType.Liability));
            Assert.That(dto.CountryCode, Is.EqualTo("BG"));
            Assert.That(dto.RowVersion, Is.EqualTo(Convert.ToBase64String(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 })));
        });
    }
}
