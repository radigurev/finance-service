using Finance.Accounts.API.Tests.Builders;
using Finance.Accounts.API.Validators;
using Finance.ServiceModel.Accounts;
using FluentValidation.Results;
using NUnit.Framework;

namespace Finance.Accounts.API.Tests.Unit.Validators;

/// <summary>
/// Unit tests for <see cref="UpdateAccountRequestValidator"/> shape rules (SDD-ACCT-001 §3.1). Only Name
/// is shape-validated; IsActive is accepted as supplied.
/// </summary>
[TestFixture]
[Category("SDD-ACCT-001")]
public sealed class UpdateAccountRequestValidatorTests
{
    private UpdateAccountRequestValidator _sut = null!;

    /// <summary>Creates a fresh validator before each test.</summary>
    [SetUp]
    public void SetUp()
    {
        _sut = new UpdateAccountRequestValidator();
    }

    /// <summary>A valid name passes validation regardless of the IsActive value.</summary>
    [Test]
    public void Validate_ValidRequest_IsValid()
    {
        // Arrange
        UpdateAccountRequest request = UpdateAccountRequestBuilder.Create().WithName("Доставчици").Build();

        // Act
        ValidationResult result = _sut.Validate(request);

        // Assert
        Assert.That(result.IsValid, Is.True);
    }

    /// <summary>An empty name fails with INVALID_ACCOUNT_CODE.</summary>
    [Test]
    public void Validate_RejectsEmptyName()
    {
        // Arrange
        UpdateAccountRequest request = UpdateAccountRequestBuilder.Create().WithName(string.Empty).Build();

        // Act
        ValidationResult result = _sut.Validate(request);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Errors, Has.Some.Matches<ValidationFailure>(
                f => f.PropertyName == nameof(UpdateAccountRequest.Name) && f.ErrorCode == "INVALID_ACCOUNT_CODE"));
        });
    }

    /// <summary>A name longer than 200 characters fails with INVALID_ACCOUNT_CODE.</summary>
    [Test]
    public void Validate_RejectsTooLongName()
    {
        // Arrange
        UpdateAccountRequest request = UpdateAccountRequestBuilder.Create().WithName(new string('x', 201)).Build();

        // Act
        ValidationResult result = _sut.Validate(request);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Errors, Has.Some.Matches<ValidationFailure>(f => f.ErrorCode == "INVALID_ACCOUNT_CODE"));
        });
    }

    /// <summary>IsActive set to false is accepted as supplied (no shape failure).</summary>
    [Test]
    public void Validate_AcceptsIsActiveFalse()
    {
        // Arrange
        UpdateAccountRequest request = UpdateAccountRequestBuilder.Create()
            .WithName("Доставчици")
            .WithIsActive(false)
            .Build();

        // Act
        ValidationResult result = _sut.Validate(request);

        // Assert
        Assert.That(result.IsValid, Is.True);
    }
}
