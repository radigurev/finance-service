using Finance.Accounts.API.Tests.Builders;
using Finance.Accounts.API.Validators;
using Finance.Common.Enums;
using Finance.ServiceModel.Accounts;
using FluentValidation.Results;
using NUnit.Framework;

namespace Finance.Accounts.API.Tests.Unit.Validators;

/// <summary>
/// Unit tests for <see cref="CreateAccountRequestValidator"/> shape rules (SDD-ACCT-001 §3.1). Verifies
/// the Code/Name/Type rules and that failures carry the documented error codes.
/// </summary>
[TestFixture]
[Category("SDD-ACCT-001")]
public sealed class CreateAccountRequestValidatorTests
{
    private CreateAccountRequestValidator _sut = null!;

    /// <summary>Creates a fresh validator before each test.</summary>
    [SetUp]
    public void SetUp()
    {
        _sut = new CreateAccountRequestValidator();
    }

    /// <summary>A fully valid request passes validation.</summary>
    [Test]
    public void Validate_ValidRequest_IsValid()
    {
        // Arrange
        CreateAccountRequest request = CreateAccountRequestBuilder.Create().Build();

        // Act
        ValidationResult result = _sut.Validate(request);

        // Assert
        Assert.That(result.IsValid, Is.True);
    }

    /// <summary>An empty code fails with INVALID_ACCOUNT_CODE.</summary>
    [Test]
    public void Validate_RejectsEmptyCode()
    {
        // Arrange
        CreateAccountRequest request = CreateAccountRequestBuilder.Create().WithCode(string.Empty).Build();

        // Act
        ValidationResult result = _sut.Validate(request);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Errors, Has.Some.Matches<ValidationFailure>(
                f => f.PropertyName == nameof(CreateAccountRequest.Code) && f.ErrorCode == "INVALID_ACCOUNT_CODE"));
        });
    }

    /// <summary>A code longer than 20 characters fails with INVALID_ACCOUNT_CODE.</summary>
    [Test]
    public void Validate_RejectsTooLongCode()
    {
        // Arrange
        CreateAccountRequest request = CreateAccountRequestBuilder.Create().WithCode(new string('9', 21)).Build();

        // Act
        ValidationResult result = _sut.Validate(request);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Errors, Has.Some.Matches<ValidationFailure>(f => f.ErrorCode == "INVALID_ACCOUNT_CODE"));
        });
    }

    /// <summary>An empty name fails with INVALID_ACCOUNT_CODE.</summary>
    [Test]
    public void Validate_RejectsEmptyName()
    {
        // Arrange
        CreateAccountRequest request = CreateAccountRequestBuilder.Create().WithName(string.Empty).Build();

        // Act
        ValidationResult result = _sut.Validate(request);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Errors, Has.Some.Matches<ValidationFailure>(
                f => f.PropertyName == nameof(CreateAccountRequest.Name) && f.ErrorCode == "INVALID_ACCOUNT_CODE"));
        });
    }

    /// <summary>A name longer than 200 characters fails with INVALID_ACCOUNT_CODE.</summary>
    [Test]
    public void Validate_RejectsTooLongName()
    {
        // Arrange
        CreateAccountRequest request = CreateAccountRequestBuilder.Create().WithName(new string('x', 201)).Build();

        // Act
        ValidationResult result = _sut.Validate(request);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Errors, Has.Some.Matches<ValidationFailure>(
                f => f.PropertyName == nameof(CreateAccountRequest.Name) && f.ErrorCode == "INVALID_ACCOUNT_CODE"));
        });
    }

    /// <summary>A type outside the enum fails with INVALID_ACCOUNT_TYPE.</summary>
    [Test]
    public void Validate_RejectsInvalidType()
    {
        // Arrange
        CreateAccountRequest request = CreateAccountRequestBuilder.Create().WithType((AccountType)999).Build();

        // Act
        ValidationResult result = _sut.Validate(request);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Errors, Has.Some.Matches<ValidationFailure>(
                f => f.PropertyName == nameof(CreateAccountRequest.Type) && f.ErrorCode == "INVALID_ACCOUNT_TYPE"));
        });
    }
}
