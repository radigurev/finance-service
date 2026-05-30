using Finance.Common.ErrorCodes;
using Finance.Common.Validation;
using NUnit.Framework;

namespace Finance.Common.Tests.Validation;

/// <summary>
/// Unit tests for the <see cref="ChainValidationResult"/> factory methods used by the
/// validation chain mechanic (SDD-INFRA-007).
/// </summary>
[TestFixture]
[Category("SDD-INFRA-007")]
public sealed class ChainValidationResultTests
{
    /// <summary>The success factory produces a valid result with no error code or detail.</summary>
    [Test]
    public void Success_ProducesValidResult_WithNoErrorCodeOrDetail()
    {
        // Arrange & Act
        ChainValidationResult result = ChainValidationResult.Success();

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsValid, Is.True);
            Assert.That(result.ErrorCode, Is.Null);
            Assert.That(result.Detail, Is.Null);
        });
    }

    /// <summary>The failure factory carries the supplied error code and detail.</summary>
    [Test]
    public void Failure_CarriesErrorCodeAndDetail()
    {
        // Arrange & Act
        ChainValidationResult result =
            ChainValidationResult.Failure(CommonErrorCodes.VALIDATION_FAILED, "cross-cutting failure");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsValid, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo(CommonErrorCodes.VALIDATION_FAILED));
            Assert.That(result.Detail, Is.EqualTo("cross-cutting failure"));
        });
    }

    /// <summary>The failure factory leaves the detail null when none is supplied.</summary>
    [Test]
    public void Failure_AllowsNullDetail()
    {
        // Arrange & Act
        ChainValidationResult result = ChainValidationResult.Failure(CommonErrorCodes.GENERIC_ERROR);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsValid, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo(CommonErrorCodes.GENERIC_ERROR));
            Assert.That(result.Detail, Is.Null);
        });
    }

    /// <summary>Two failures with the same code and detail are value-equal (record struct semantics).</summary>
    [Test]
    public void Failure_RecordStructEquality_HoldsForSameCodeAndDetail()
    {
        // Arrange
        ChainValidationResult first = ChainValidationResult.Failure(CommonErrorCodes.VALIDATION_FAILED, "x");
        ChainValidationResult second = ChainValidationResult.Failure(CommonErrorCodes.VALIDATION_FAILED, "x");

        // Act
        bool equal = first.Equals(second);

        // Assert
        Assert.That(equal, Is.True);
    }
}
