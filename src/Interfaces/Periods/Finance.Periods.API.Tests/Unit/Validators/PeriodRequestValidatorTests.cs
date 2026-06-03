using Finance.Common.ErrorCodes;
using Finance.Periods.API.Validators;
using Finance.ServiceModel.Periods;
using FluentValidation.Results;
using NUnit.Framework;

namespace Finance.Periods.API.Tests.Unit.Validators;

/// <summary>
/// Unit tests for the Periods FluentValidation request validators (SDD-FIN-004 §3.1, §6.3). Verifies the
/// field-level shape rules and that every failure carries the configured <c>PeriodErrorCodes</c> code.
/// </summary>
[TestFixture]
[Category("SDD-FIN-004")]
public sealed class PeriodRequestValidatorTests
{
    /// <summary>The close validator rejects an empty reason with CLOSE_REASON_REQUIRED (§3.1).</summary>
    [Test]
    public void CloseRequestValidator_RejectsEmptyReason()
    {
        // Arrange
        ClosePeriodRequestValidator validator = new();
        ClosePeriodRequest request = new() { Reason = "   ", RowVersion = "AAAAAAAAAAA=" };

        // Act
        ValidationResult result = validator.Validate(request);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsValid, Is.False);
            Assert.That(
                result.Errors.Exists(error => error.ErrorCode == PeriodErrorCodes.CLOSE_REASON_REQUIRED),
                Is.True);
        });
    }

    /// <summary>The close validator accepts a non-empty reason (§3.1).</summary>
    [Test]
    public void CloseRequestValidator_AcceptsNonEmptyReason()
    {
        // Arrange
        ClosePeriodRequestValidator validator = new();
        ClosePeriodRequest request = new() { Reason = "Month-end close", RowVersion = "AAAAAAAAAAA=" };

        // Act
        ValidationResult result = validator.Validate(request);

        // Assert
        Assert.That(result.IsValid, Is.True);
    }

    /// <summary>The reopen validator rejects an empty reason with REOPEN_REASON_REQUIRED (§3.1).</summary>
    [Test]
    public void ReopenRequestValidator_RejectsEmptyReason()
    {
        // Arrange
        ReopenPeriodRequestValidator validator = new();
        ReopenPeriodRequest request = new() { Reason = "", RowVersion = "AAAAAAAAAAA=" };

        // Act
        ValidationResult result = validator.Validate(request);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsValid, Is.False);
            Assert.That(
                result.Errors.Exists(error => error.ErrorCode == PeriodErrorCodes.REOPEN_REASON_REQUIRED),
                Is.True);
        });
    }

    /// <summary>The generate validator rejects an implausible fiscal year with INVALID_PERIOD (§3.1).</summary>
    [Test]
    public void GenerateRequestValidator_RejectsImplausibleYear()
    {
        // Arrange
        GeneratePeriodsRequestValidator validator = new();
        GeneratePeriodsRequest request = new() { FiscalYear = 1900 };

        // Act
        ValidationResult result = validator.Validate(request);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsValid, Is.False);
            Assert.That(
                result.Errors.Exists(error => error.ErrorCode == PeriodErrorCodes.INVALID_PERIOD),
                Is.True);
        });
    }

    /// <summary>The generate validator accepts a plausible fiscal year (§3.1).</summary>
    [Test]
    public void GenerateRequestValidator_AcceptsPlausibleYear()
    {
        // Arrange
        GeneratePeriodsRequestValidator validator = new();
        GeneratePeriodsRequest request = new() { FiscalYear = 2026 };

        // Act
        ValidationResult result = validator.Validate(request);

        // Assert
        Assert.That(result.IsValid, Is.True);
    }

    /// <summary>The create validator rejects a period number outside 1–12 with INVALID_PERIOD (§3.1).</summary>
    [Test]
    public void CreateRequestValidator_RejectsPeriodNumberOutOfRange()
    {
        // Arrange
        CreatePeriodRequestValidator validator = new();
        CreatePeriodRequest request = new()
        {
            FiscalYear = 2026,
            PeriodNumber = 13,
            StartDate = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            EndDate = new DateTimeOffset(2026, 1, 31, 23, 59, 59, TimeSpan.Zero)
        };

        // Act
        ValidationResult result = validator.Validate(request);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsValid, Is.False);
            Assert.That(
                result.Errors.Exists(error => error.ErrorCode == PeriodErrorCodes.INVALID_PERIOD),
                Is.True);
        });
    }

    /// <summary>The create validator rejects an end date not after the start date with INVALID_PERIOD (§3.1).</summary>
    [Test]
    public void CreateRequestValidator_RejectsEndDateNotAfterStartDate()
    {
        // Arrange
        CreatePeriodRequestValidator validator = new();
        CreatePeriodRequest request = new()
        {
            FiscalYear = 2026,
            PeriodNumber = 1,
            StartDate = new DateTimeOffset(2026, 1, 31, 0, 0, 0, TimeSpan.Zero),
            EndDate = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)
        };

        // Act
        ValidationResult result = validator.Validate(request);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsValid, Is.False);
            Assert.That(
                result.Errors.Exists(error => error.ErrorCode == PeriodErrorCodes.INVALID_PERIOD),
                Is.True);
        });
    }

    /// <summary>The create validator accepts a valid single-period request (§3.1).</summary>
    [Test]
    public void CreateRequestValidator_AcceptsValidRequest()
    {
        // Arrange
        CreatePeriodRequestValidator validator = new();
        CreatePeriodRequest request = new()
        {
            FiscalYear = 2026,
            PeriodNumber = 1,
            StartDate = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            EndDate = new DateTimeOffset(2026, 1, 31, 23, 59, 59, TimeSpan.Zero)
        };

        // Act
        ValidationResult result = validator.Validate(request);

        // Assert
        Assert.That(result.IsValid, Is.True);
    }
}
