using Finance.Accounts.API.Tests.Builders;
using Finance.Accounts.API.Tests.Fixtures;
using Finance.Accounts.API.Validators;
using Finance.Accounts.DBModel.Models;
using Finance.Common.Validation;
using Finance.ServiceModel.Accounts;
using NUnit.Framework;

namespace Finance.Accounts.API.Tests.Unit.Validators;

/// <summary>
/// Unit tests for <see cref="DuplicateAccountCodeValidator"/> (SDD-ACCT-001 §2.3, §3.2). Runs against a
/// SQLite in-memory <c>AccountsDbContext</c> so the uniqueness rule is exercised over real data.
/// </summary>
[TestFixture]
[Category("SDD-ACCT-001")]
public sealed class DuplicateAccountCodeValidatorTests
{
    private SqliteAccountsDbContextScope _scope = null!;
    private DuplicateAccountCodeValidator _sut = null!;

    /// <summary>Creates a fresh SQLite-backed validator before each test.</summary>
    [SetUp]
    public void SetUp()
    {
        _scope = SqliteAccountsDbContextFactory.Create();
        _sut = new DuplicateAccountCodeValidator(_scope.Context, TestConfiguration.WithCountry("BG"));
    }

    /// <summary>Disposes the SQLite scope after each test.</summary>
    [TearDown]
    public void TearDown()
    {
        _scope.Dispose();
    }

    /// <summary>A code already present in the country yields DUPLICATE_ACCOUNT_CODE.</summary>
    [Test]
    public async Task ValidateAsync_FailsWithDuplicateAccountCode_WhenCodePresentInCountry()
    {
        // Arrange
        await SeedAsync(AccountBuilder.Create().WithCountryCode("BG").WithCode("401"));
        CreateAccountRequest request = CreateAccountRequestBuilder.Create().WithCode("401").Build();

        // Act
        ChainValidationResult result = await _sut.ValidateAsync(request, CancellationToken.None);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsValid, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo("DUPLICATE_ACCOUNT_CODE"));
        });
    }

    /// <summary>An unused code passes the uniqueness check.</summary>
    [Test]
    public async Task ValidateAsync_Passes_WhenCodeUnused()
    {
        // Arrange
        await SeedAsync(AccountBuilder.Create().WithCountryCode("BG").WithCode("401"));
        CreateAccountRequest request = CreateAccountRequestBuilder.Create().WithCode("501").Build();

        // Act
        ChainValidationResult result = await _sut.ValidateAsync(request, CancellationToken.None);

        // Assert
        Assert.That(result.IsValid, Is.True);
    }

    /// <summary>The same code in a different country does not clash (uniqueness is scoped per country).</summary>
    [Test]
    public async Task ValidateAsync_Passes_WhenSameCodeInDifferentCountry()
    {
        // Arrange
        await SeedAsync(AccountBuilder.Create().WithCountryCode("DE").WithCode("401"));
        CreateAccountRequest request = CreateAccountRequestBuilder.Create().WithCode("401").Build();

        // Act
        ChainValidationResult result = await _sut.ValidateAsync(request, CancellationToken.None);

        // Assert
        Assert.That(result.IsValid, Is.True);
    }

    private async Task SeedAsync(AccountBuilder builder)
    {
        Account account = builder.Build();
        _scope.Context.Accounts.Add(account);
        await _scope.Context.SaveChangesAsync(CancellationToken.None);
    }
}
