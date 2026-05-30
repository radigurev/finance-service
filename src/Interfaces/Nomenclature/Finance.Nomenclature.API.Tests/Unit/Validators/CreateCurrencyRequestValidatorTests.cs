using Finance.Nomenclature.API.Tests.Builders;
using Finance.Nomenclature.API.Validators;
using Finance.ServiceModel.Nomenclature;
using FluentValidation.Results;
using NUnit.Framework;

namespace Finance.Nomenclature.API.Tests.Unit.Validators;

/// <summary>
/// Unit tests for <see cref="CreateCurrencyRequestValidator"/> shape rules (SDD-NOM-001 §3). Verifies the
/// ISO-code, name, and symbol rules and that failures carry the documented error codes.
/// </summary>
[TestFixture]
[Category("SDD-NOM-001")]
public sealed class CreateCurrencyRequestValidatorTests
{
    private CreateCurrencyRequestValidator _sut = null!;

    /// <summary>Creates a fresh validator before each test.</summary>
    [SetUp]
    public void SetUp()
    {
        _sut = new CreateCurrencyRequestValidator();
    }

    /// <summary>A fully valid request passes validation.</summary>
    [Test]
    public void Validate_ValidRequest_IsValid()
    {
        // Arrange
        CreateCurrencyRequest request = CreateCurrencyRequestBuilder.Create().Build();

        // Act
        ValidationResult result = _sut.Validate(request);

        // Assert
        Assert.That(result.IsValid, Is.True);
    }

    /// <summary>A lowercase code fails with INVALID_CURRENCY_CODE.</summary>
    [Test]
    public void CurrencyValidator_RejectsLowercaseCode_ReturnsInvalidCurrencyCode()
    {
        // Arrange
        CreateCurrencyRequest request = CreateCurrencyRequestBuilder.Create().WithIsoCode("usd").Build();

        // Act
        ValidationResult result = _sut.Validate(request);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Errors, Has.Some.Matches<ValidationFailure>(
                f => f.PropertyName == nameof(CreateCurrencyRequest.IsoCode)
                    && f.ErrorCode == "INVALID_CURRENCY_CODE"));
        });
    }

    /// <summary>A code that is not exactly three letters fails with INVALID_CURRENCY_CODE.</summary>
    [Test]
    public void CurrencyValidator_RejectsCodeNotThreeLetters_ReturnsInvalidCurrencyCode()
    {
        // Arrange
        CreateCurrencyRequest request = CreateCurrencyRequestBuilder.Create().WithIsoCode("US").Build();

        // Act
        ValidationResult result = _sut.Validate(request);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Errors, Has.Some.Matches<ValidationFailure>(
                f => f.ErrorCode == "INVALID_CURRENCY_CODE"));
        });
    }

    /// <summary>A code with digits fails with INVALID_CURRENCY_CODE.</summary>
    [Test]
    public void CurrencyValidator_RejectsCodeWithDigits_ReturnsInvalidCurrencyCode()
    {
        // Arrange
        CreateCurrencyRequest request = CreateCurrencyRequestBuilder.Create().WithIsoCode("US1").Build();

        // Act
        ValidationResult result = _sut.Validate(request);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Errors, Has.Some.Matches<ValidationFailure>(
                f => f.ErrorCode == "INVALID_CURRENCY_CODE"));
        });
    }

    /// <summary>An empty name fails with INVALID_CURRENCY_NAME.</summary>
    [Test]
    public void CurrencyValidator_RejectsEmptyName_ReturnsInvalidCurrencyName()
    {
        // Arrange
        CreateCurrencyRequest request = CreateCurrencyRequestBuilder.Create().WithName(string.Empty).Build();

        // Act
        ValidationResult result = _sut.Validate(request);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Errors, Has.Some.Matches<ValidationFailure>(
                f => f.PropertyName == nameof(CreateCurrencyRequest.Name)
                    && f.ErrorCode == "INVALID_CURRENCY_NAME"));
        });
    }

    /// <summary>A name longer than 100 characters fails with INVALID_CURRENCY_NAME.</summary>
    [Test]
    public void CurrencyValidator_RejectsNameOverHundredChars_ReturnsInvalidCurrencyName()
    {
        // Arrange
        CreateCurrencyRequest request = CreateCurrencyRequestBuilder.Create().WithName(new string('x', 101)).Build();

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
    public void CurrencyValidator_RejectsSymbolOverFiveChars_ReturnsInvalidCurrencySymbol()
    {
        // Arrange
        CreateCurrencyRequest request = CreateCurrencyRequestBuilder.Create().WithSymbol("123456").Build();

        // Act
        ValidationResult result = _sut.Validate(request);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Errors, Has.Some.Matches<ValidationFailure>(
                f => f.PropertyName == nameof(CreateCurrencyRequest.Symbol)
                    && f.ErrorCode == "INVALID_CURRENCY_SYMBOL"));
        });
    }

    /// <summary>A null symbol is allowed (the symbol is optional).</summary>
    [Test]
    public void Validate_NullSymbol_IsValid()
    {
        // Arrange
        CreateCurrencyRequest request = CreateCurrencyRequestBuilder.Create().WithSymbol(null).Build();

        // Act
        ValidationResult result = _sut.Validate(request);

        // Assert
        Assert.That(result.IsValid, Is.True);
    }
}
