using Finance.Nomenclature.API.Tests.Builders;
using Finance.Nomenclature.API.Validators;
using Finance.ServiceModel.Nomenclature;
using FluentValidation.Results;
using NUnit.Framework;

namespace Finance.Nomenclature.API.Tests.Unit.Validators;

/// <summary>
/// Unit tests for <see cref="UpdateCurrencyRequestValidator"/> shape rules (SDD-NOM-001 §2.1, §3). The
/// update body carries no ISO code (it is immutable and taken from the path); only Name and Symbol are
/// validated.
/// </summary>
[TestFixture]
[Category("SDD-NOM-001")]
public sealed class UpdateCurrencyRequestValidatorTests
{
    private UpdateCurrencyRequestValidator _sut = null!;

    /// <summary>Creates a fresh validator before each test.</summary>
    [SetUp]
    public void SetUp()
    {
        _sut = new UpdateCurrencyRequestValidator();
    }

    /// <summary>A fully valid update request passes validation.</summary>
    [Test]
    public void Validate_ValidRequest_IsValid()
    {
        // Arrange
        UpdateCurrencyRequest request = UpdateCurrencyRequestBuilder.Create().Build();

        // Act
        ValidationResult result = _sut.Validate(request);

        // Assert
        Assert.That(result.IsValid, Is.True);
    }

    /// <summary>An empty name fails with INVALID_CURRENCY_NAME.</summary>
    [Test]
    public void Validate_RejectsEmptyName_ReturnsInvalidCurrencyName()
    {
        // Arrange
        UpdateCurrencyRequest request = UpdateCurrencyRequestBuilder.Create().WithName(string.Empty).Build();

        // Act
        ValidationResult result = _sut.Validate(request);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Errors, Has.Some.Matches<ValidationFailure>(
                f => f.PropertyName == nameof(UpdateCurrencyRequest.Name)
                    && f.ErrorCode == "INVALID_CURRENCY_NAME"));
        });
    }

    /// <summary>A name longer than 100 characters fails with INVALID_CURRENCY_NAME.</summary>
    [Test]
    public void Validate_RejectsNameOverHundredChars_ReturnsInvalidCurrencyName()
    {
        // Arrange
        UpdateCurrencyRequest request = UpdateCurrencyRequestBuilder.Create().WithName(new string('x', 101)).Build();

        // Act
        ValidationResult result = _sut.Validate(request);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Errors, Has.Some.Matches<ValidationFailure>(
                f => f.ErrorCode == "INVALID_CURRENCY_NAME"));
        });
    }

    /// <summary>A symbol longer than five characters fails with INVALID_CURRENCY_SYMBOL.</summary>
    [Test]
    public void Validate_RejectsSymbolOverFiveChars_ReturnsInvalidCurrencySymbol()
    {
        // Arrange
        UpdateCurrencyRequest request = UpdateCurrencyRequestBuilder.Create().WithSymbol("123456").Build();

        // Act
        ValidationResult result = _sut.Validate(request);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Errors, Has.Some.Matches<ValidationFailure>(
                f => f.PropertyName == nameof(UpdateCurrencyRequest.Symbol)
                    && f.ErrorCode == "INVALID_CURRENCY_SYMBOL"));
        });
    }
}
