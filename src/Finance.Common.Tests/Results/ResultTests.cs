using Finance.Common.ErrorCodes;
using Finance.Common.Results;
using NUnit.Framework;

namespace Finance.Common.Tests.Results;

/// <summary>
/// Unit tests for the canonical <see cref="Result"/> and <see cref="Result{T}"/> outcome types.
/// Covers the SDD-INFRA-009 Batch-1 <c>Result</c> / <c>Result&lt;T&gt;</c> test plan.
/// </summary>
[TestFixture]
[Category("SDD-INFRA-009")]
public sealed class ResultTests
{
    /// <summary>A success result has <c>IsSuccess</c> true and no error code or detail.</summary>
    [Test]
    public void Result_Success_HasIsSuccessTrueAndNullErrorCode()
    {
        // Arrange & Act
        Result result = Result.Success();

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.ErrorCode, Is.Null);
            Assert.That(result.Detail, Is.Null);
        });
    }

    /// <summary>A failure result carries the supplied error code and optional detail.</summary>
    [Test]
    public void Result_Failure_CarriesErrorCodeAndOptionalDetail()
    {
        // Arrange & Act
        Result result = Result.Failure(CommonErrorCodes.VALIDATION_FAILED, "bad input");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo(CommonErrorCodes.VALIDATION_FAILED));
            Assert.That(result.Detail, Is.EqualTo("bad input"));
        });
    }

    /// <summary>A failure result without detail leaves the detail null.</summary>
    [Test]
    public void Result_Failure_AllowsNullDetail()
    {
        // Arrange & Act
        Result result = Result.Failure(CommonErrorCodes.GENERIC_ERROR);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo(CommonErrorCodes.GENERIC_ERROR));
            Assert.That(result.Detail, Is.Null);
        });
    }

    /// <summary>A typed success result carries the value and reports success.</summary>
    [Test]
    public void ResultOfT_Success_CarriesValue_AndIsSuccessTrue()
    {
        // Arrange & Act
        Result<int> result = Result<int>.Success(42);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Value, Is.EqualTo(42));
            Assert.That(result.ErrorCode, Is.Null);
            Assert.That(result.Detail, Is.Null);
        });
    }

    /// <summary>A typed success result preserves a reference-type value.</summary>
    [Test]
    public void ResultOfT_Success_PreservesReferenceTypeValue()
    {
        // Arrange
        string value = "chart-of-accounts";

        // Act
        Result<string> result = Result<string>.Success(value);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Value, Is.EqualTo(value));
            Assert.That(result.ErrorCode, Is.Null);
        });
    }

    /// <summary>A typed failure result has the default value and carries the error code.</summary>
    [Test]
    public void ResultOfT_Failure_HasDefaultValue_AndCarriesErrorCode()
    {
        // Arrange & Act
        Result<string> result = Result<string>.Failure(CommonErrorCodes.CONCURRENT_MODIFICATION, "conflict");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Value, Is.Null);
            Assert.That(result.ErrorCode, Is.EqualTo(CommonErrorCodes.CONCURRENT_MODIFICATION));
            Assert.That(result.Detail, Is.EqualTo("conflict"));
        });
    }

    /// <summary>A typed failure on a value type leaves the value at its default and omits detail when unspecified.</summary>
    [Test]
    public void ResultOfT_Failure_ValueType_DefaultsValueAndAllowsNullDetail()
    {
        // Arrange & Act
        Result<int> result = Result<int>.Failure(CommonErrorCodes.GENERIC_ERROR);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Value, Is.EqualTo(0));
            Assert.That(result.ErrorCode, Is.EqualTo(CommonErrorCodes.GENERIC_ERROR));
            Assert.That(result.Detail, Is.Null);
        });
    }

    /// <summary>Two failures with the same code and detail are value-equal (record semantics).</summary>
    [Test]
    public void Result_Failure_RecordEquality_HoldsForSameCodeAndDetail()
    {
        // Arrange
        Result first = Result.Failure(CommonErrorCodes.VALIDATION_FAILED, "same");
        Result second = Result.Failure(CommonErrorCodes.VALIDATION_FAILED, "same");

        // Act
        bool equal = first == second;

        // Assert
        Assert.That(equal, Is.True);
    }
}
