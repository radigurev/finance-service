using Finance.Accounts.API.Tests.Builders;
using Finance.Accounts.API.Tests.Fixtures;
using Finance.Accounts.API.Validators;
using Finance.Accounts.DBModel.Models;
using Finance.Common.Validation;
using Finance.ServiceModel.Accounts;
using NUnit.Framework;

namespace Finance.Accounts.API.Tests.Unit.Validators;

/// <summary>
/// Unit tests for <see cref="ParentAccountValidator"/> (SDD-ACCT-001 §2.3, §3.2). Runs against a SQLite
/// in-memory <c>AccountsDbContext</c> so the parent existence/country rule is exercised over real data.
/// </summary>
[TestFixture]
[Category("SDD-ACCT-001")]
public sealed class ParentAccountValidatorTests
{
    private SqliteAccountsDbContextScope _scope = null!;
    private ParentAccountValidator _sut = null!;

    /// <summary>Creates a fresh SQLite-backed validator before each test.</summary>
    [SetUp]
    public void SetUp()
    {
        _scope = SqliteAccountsDbContextFactory.Create();
        _sut = new ParentAccountValidator(_scope.Context, TestConfiguration.WithCountry("BG"));
    }

    /// <summary>Disposes the SQLite scope after each test.</summary>
    [TearDown]
    public void TearDown()
    {
        _scope.Dispose();
    }

    /// <summary>A null ParentId is treated as valid (no parent supplied).</summary>
    [Test]
    public async Task ValidateAsync_Passes_WhenNoParentSupplied()
    {
        // Arrange
        CreateAccountRequest request = CreateAccountRequestBuilder.Create().WithParentId(null).Build();

        // Act
        ChainValidationResult result = await _sut.ValidateAsync(request, CancellationToken.None);

        // Assert
        Assert.That(result.IsValid, Is.True);
    }

    /// <summary>An existing parent in the same country passes validation.</summary>
    [Test]
    public async Task ValidateAsync_Passes_WhenParentExistsInSameCountry()
    {
        // Arrange
        Account parent = await SeedAsync(AccountBuilder.Create().WithCountryCode("BG").WithCode("400"));
        CreateAccountRequest request = CreateAccountRequestBuilder.Create().WithParentId(parent.Id).Build();

        // Act
        ChainValidationResult result = await _sut.ValidateAsync(request, CancellationToken.None);

        // Assert
        Assert.That(result.IsValid, Is.True);
    }

    /// <summary>A missing parent yields INVALID_PARENT_ACCOUNT.</summary>
    [Test]
    public async Task ValidateAsync_FailsWithInvalidParentAccount_WhenParentMissing()
    {
        // Arrange
        CreateAccountRequest request = CreateAccountRequestBuilder.Create().WithParentId(7777).Build();

        // Act
        ChainValidationResult result = await _sut.ValidateAsync(request, CancellationToken.None);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsValid, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo("INVALID_PARENT_ACCOUNT"));
        });
    }

    /// <summary>A parent in a different country yields INVALID_PARENT_ACCOUNT.</summary>
    [Test]
    public async Task ValidateAsync_FailsWithInvalidParentAccount_WhenParentInDifferentCountry()
    {
        // Arrange
        Account foreignParent = await SeedAsync(AccountBuilder.Create().WithCountryCode("DE").WithCode("100"));
        CreateAccountRequest request = CreateAccountRequestBuilder.Create().WithParentId(foreignParent.Id).Build();

        // Act
        ChainValidationResult result = await _sut.ValidateAsync(request, CancellationToken.None);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsValid, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo("INVALID_PARENT_ACCOUNT"));
        });
    }

    private async Task<Account> SeedAsync(AccountBuilder builder)
    {
        Account account = builder.Build();
        _scope.Context.Accounts.Add(account);
        await _scope.Context.SaveChangesAsync(CancellationToken.None);
        return account;
    }
}
