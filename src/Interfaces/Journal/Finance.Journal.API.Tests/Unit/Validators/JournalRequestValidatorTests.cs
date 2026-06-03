using Finance.Common.ErrorCodes;
using Finance.Journal.API.Tests.Builders;
using Finance.Journal.API.Validators;
using Finance.ServiceModel.Journal;
using FluentValidation.Results;
using NUnit.Framework;

namespace Finance.Journal.API.Tests.Unit.Validators;

/// <summary>
/// Unit tests for the request-level FluentValidation shape rules of the journal endpoints
/// (SDD-FIN-001 §3.1, SDD-FIN-002 §3.1): the required entry date on create and the mandatory reversal
/// reason. The stateful balance/postability/currency invariants are covered by
/// <see cref="Validation.JournalEntryValidatorTests"/>.
/// </summary>
[TestFixture]
[Category("SDD-FIN-001")]
public sealed class JournalRequestValidatorTests
{
    private CreateJournalEntryRequestValidator _createValidator = null!;
    private ReverseJournalEntryRequestValidator _reverseValidator = null!;

    /// <summary>Builds fresh request validators before each test.</summary>
    [SetUp]
    public void SetUp()
    {
        _createValidator = new CreateJournalEntryRequestValidator();
        _reverseValidator = new ReverseJournalEntryRequestValidator();
    }

    /// <summary>A create request with a default (missing) entry date fails INVALID_ENTRY_DATE (SDD-FIN-001 §3.1, §6.1).</summary>
    [Test]
    public void Validate_MissingEntryDate_ReturnsInvalidEntryDate()
    {
        // Arrange
        CreateJournalEntryRequest request = CreateJournalEntryRequestBuilder.Create()
            .WithEntryDate(default)
            .Build();

        // Act
        ValidationResult result = _createValidator.Validate(request);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Errors, Has.Some.Matches<ValidationFailure>(
                failure => failure.ErrorCode == JournalErrorCodes.INVALID_ENTRY_DATE));
        });
    }

    /// <summary>A reverse request with an empty reason fails REVERSAL_REASON_REQUIRED (SDD-FIN-002 §3.1).</summary>
    [Test]
    public void Validate_ReverseWithoutReason_ReturnsReversalReasonRequired()
    {
        // Arrange
        ReverseJournalEntryRequest request = new() { Reason = string.Empty, RowVersion = "AAAAAAAAAAA=" };

        // Act
        ValidationResult result = _reverseValidator.Validate(request);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Errors, Has.Some.Matches<ValidationFailure>(
                failure => failure.ErrorCode == JournalErrorCodes.REVERSAL_REASON_REQUIRED));
        });
    }
}
