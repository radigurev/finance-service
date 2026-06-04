using Finance.Common.ErrorCodes;
using Finance.Country.Abstractions;
using Finance.Journal.API.Validators;
using Finance.ServiceModel.Posting;
using FluentValidation.Results;
using NUnit.Framework;

namespace Finance.Journal.API.Tests.Unit.Validators;

/// <summary>
/// Unit tests for the request-level FluentValidation shape rules of the posting endpoints
/// (SDD-FIN-006 §3.1): the create-rule key/lines shape, the per-line account-selector/enum shape, and the
/// apply-request key/amounts/currency/date shape. The stateful invariants (duplicate key, balanceable,
/// rule resolution, missing amount, account resolution) are covered by the service/engine tests.
/// </summary>
[TestFixture]
[Category("SDD-FIN-006")]
public sealed class PostingRuleRequestValidatorTests
{
    private CreatePostingRuleRequestValidator _createValidator = null!;
    private CreatePostingRuleLineRequestValidator _lineValidator = null!;
    private ApplyPostingRuleRequestValidator _applyValidator = null!;

    /// <summary>Builds fresh posting-rule request validators before each test.</summary>
    [SetUp]
    public void SetUp()
    {
        _createValidator = new CreatePostingRuleRequestValidator();
        _lineValidator = new CreatePostingRuleLineRequestValidator();
        _applyValidator = new ApplyPostingRuleRequestValidator();
    }

    /// <summary>A well-formed create request passes the shape validator (SDD-FIN-006 §3.1).</summary>
    [Test]
    public void Validate_WellFormedCreateRequest_HasNoErrors()
    {
        // Arrange
        CreatePostingRuleRequest request = BuildCreateRequest("SALE_INVOICE");

        // Act
        ValidationResult result = _createValidator.Validate(request);

        // Assert
        Assert.That(result.IsValid, Is.True);
    }

    /// <summary>A non-uppercase rule key fails INVALID_POSTING_RULE_KEY (SDD-FIN-006 §3.1).</summary>
    [Test]
    public void Validate_NonUppercaseRuleKey_ReturnsInvalidPostingRuleKey()
    {
        // Arrange
        CreatePostingRuleRequest request = BuildCreateRequest("sale_invoice");

        // Act
        ValidationResult result = _createValidator.Validate(request);

        // Assert
        Assert.That(ErrorCodes(result), Does.Contain(PostingErrorCodes.INVALID_POSTING_RULE_KEY));
    }

    /// <summary>An empty rule key fails INVALID_POSTING_RULE_KEY (SDD-FIN-006 §3.1).</summary>
    [Test]
    public void Validate_EmptyRuleKey_ReturnsInvalidPostingRuleKey()
    {
        // Arrange
        CreatePostingRuleRequest request = BuildCreateRequest(string.Empty);

        // Act
        ValidationResult result = _createValidator.Validate(request);

        // Assert
        Assert.That(ErrorCodes(result), Does.Contain(PostingErrorCodes.INVALID_POSTING_RULE_KEY));
    }

    /// <summary>A rule key longer than 50 characters fails INVALID_POSTING_RULE_KEY (SDD-FIN-006 §3.1).</summary>
    [Test]
    public void Validate_RuleKeyExceeds50Chars_ReturnsInvalidPostingRuleKey()
    {
        // Arrange
        CreatePostingRuleRequest request = BuildCreateRequest(new string('A', 51));

        // Act
        ValidationResult result = _createValidator.Validate(request);

        // Assert
        Assert.That(ErrorCodes(result), Does.Contain(PostingErrorCodes.INVALID_POSTING_RULE_KEY));
    }

    /// <summary>A create request with zero lines fails POSTING_RULE_HAS_NO_LINES (SDD-FIN-006 §3.1).</summary>
    [Test]
    public void Validate_CreateRequestWithNoLines_ReturnsPostingRuleHasNoLines()
    {
        // Arrange
        CreatePostingRuleRequest request = new()
        {
            RuleKey = "SALE_INVOICE",
            Description = "Sale invoice",
            Lines = []
        };

        // Act
        ValidationResult result = _createValidator.Validate(request);

        // Assert
        Assert.That(ErrorCodes(result), Does.Contain(PostingErrorCodes.POSTING_RULE_HAS_NO_LINES));
    }

    /// <summary>An empty account selector on a line fails INVALID_POSTING_RULE_LINE (SDD-FIN-006 §3.1).</summary>
    [Test]
    public void Validate_LineWithEmptyAccountSelector_ReturnsInvalidPostingRuleLine()
    {
        // Arrange
        CreatePostingRuleLineRequest line = new()
        {
            AccountSelector = string.Empty,
            DebitOrCredit = PostingDebitOrCredit.Debit,
            AmountSource = PostingAmountSource.Net
        };

        // Act
        ValidationResult result = _lineValidator.Validate(line);

        // Assert
        Assert.That(ErrorCodes(result), Does.Contain(PostingErrorCodes.INVALID_POSTING_RULE_LINE));
    }

    /// <summary>An out-of-range DebitOrCredit enum on a line fails INVALID_POSTING_RULE_LINE (SDD-FIN-006 §3.1).</summary>
    [Test]
    public void Validate_LineWithInvalidDebitOrCreditEnum_ReturnsInvalidPostingRuleLine()
    {
        // Arrange
        CreatePostingRuleLineRequest line = new()
        {
            AccountSelector = "411",
            DebitOrCredit = (PostingDebitOrCredit)99,
            AmountSource = PostingAmountSource.Net
        };

        // Act
        ValidationResult result = _lineValidator.Validate(line);

        // Assert
        Assert.That(ErrorCodes(result), Does.Contain(PostingErrorCodes.INVALID_POSTING_RULE_LINE));
    }

    /// <summary>An out-of-range AmountSource enum on a line fails INVALID_POSTING_RULE_LINE (SDD-FIN-006 §3.1).</summary>
    [Test]
    public void Validate_LineWithInvalidAmountSourceEnum_ReturnsInvalidPostingRuleLine()
    {
        // Arrange
        CreatePostingRuleLineRequest line = new()
        {
            AccountSelector = "701",
            DebitOrCredit = PostingDebitOrCredit.Credit,
            AmountSource = (PostingAmountSource)99
        };

        // Act
        ValidationResult result = _lineValidator.Validate(line);

        // Assert
        Assert.That(ErrorCodes(result), Does.Contain(PostingErrorCodes.INVALID_POSTING_RULE_LINE));
    }

    /// <summary>A well-formed apply request passes the shape validator (SDD-FIN-006 §3.1).</summary>
    [Test]
    public void Validate_WellFormedApplyRequest_HasNoErrors()
    {
        // Arrange
        ApplyPostingRuleRequest request = BuildApplyRequest();

        // Act
        ValidationResult result = _applyValidator.Validate(request);

        // Assert
        Assert.That(result.IsValid, Is.True);
    }

    /// <summary>An empty apply rule key fails INVALID_POSTING_RULE_KEY (SDD-FIN-006 §3.1).</summary>
    [Test]
    public void Validate_ApplyWithEmptyRuleKey_ReturnsInvalidPostingRuleKey()
    {
        // Arrange
        ApplyPostingRuleRequest request = BuildApplyRequest() with { RuleKey = string.Empty };

        // Act
        ValidationResult result = _applyValidator.Validate(request);

        // Assert
        Assert.That(ErrorCodes(result), Does.Contain(PostingErrorCodes.INVALID_POSTING_RULE_KEY));
    }

    /// <summary>An empty apply amount map fails MISSING_POSTING_AMOUNT (SDD-FIN-006 §3.1).</summary>
    [Test]
    public void Validate_ApplyWithEmptyAmounts_ReturnsMissingPostingAmount()
    {
        // Arrange
        ApplyPostingRuleRequest request = BuildApplyRequest()
            with { Amounts = new Dictionary<PostingAmountSource, decimal>() };

        // Act
        ValidationResult result = _applyValidator.Validate(request);

        // Assert
        Assert.That(ErrorCodes(result), Does.Contain(PostingErrorCodes.MISSING_POSTING_AMOUNT));
    }

    /// <summary>A negative apply amount fails MISSING_POSTING_AMOUNT (SDD-FIN-006 §3.1).</summary>
    [Test]
    public void Validate_ApplyWithNegativeAmount_ReturnsMissingPostingAmount()
    {
        // Arrange
        ApplyPostingRuleRequest request = BuildApplyRequest()
            with { Amounts = new Dictionary<PostingAmountSource, decimal> { [PostingAmountSource.Net] = -1.00m } };

        // Act
        ValidationResult result = _applyValidator.Validate(request);

        // Assert
        Assert.That(ErrorCodes(result), Does.Contain(PostingErrorCodes.MISSING_POSTING_AMOUNT));
    }

    /// <summary>A non-3-letter currency code on apply fails INVALID_LINE_CURRENCY (SDD-FIN-006 §3.1).</summary>
    [Test]
    public void Validate_ApplyWithInvalidCurrencyLength_ReturnsInvalidLineCurrency()
    {
        // Arrange
        ApplyPostingRuleRequest request = BuildApplyRequest() with { CurrencyCode = "BG" };

        // Act
        ValidationResult result = _applyValidator.Validate(request);

        // Assert
        Assert.That(ErrorCodes(result), Does.Contain(JournalErrorCodes.INVALID_LINE_CURRENCY));
    }

    /// <summary>A default (missing) entry date on apply fails INVALID_ENTRY_DATE (SDD-FIN-006 §3.1).</summary>
    [Test]
    public void Validate_ApplyWithDefaultEntryDate_ReturnsInvalidEntryDate()
    {
        // Arrange
        ApplyPostingRuleRequest request = BuildApplyRequest() with { EntryDate = default };

        // Act
        ValidationResult result = _applyValidator.Validate(request);

        // Assert
        Assert.That(ErrorCodes(result), Does.Contain(JournalErrorCodes.INVALID_ENTRY_DATE));
    }

    private static CreatePostingRuleRequest BuildCreateRequest(string ruleKey)
    {
        return new CreatePostingRuleRequest
        {
            RuleKey = ruleKey,
            Description = "Sample rule",
            Lines =
            [
                new CreatePostingRuleLineRequest
                {
                    AccountSelector = "411",
                    DebitOrCredit = PostingDebitOrCredit.Debit,
                    AmountSource = PostingAmountSource.Gross
                },
                new CreatePostingRuleLineRequest
                {
                    AccountSelector = "701",
                    DebitOrCredit = PostingDebitOrCredit.Credit,
                    AmountSource = PostingAmountSource.Net
                }
            ]
        };
    }

    private static ApplyPostingRuleRequest BuildApplyRequest()
    {
        return new ApplyPostingRuleRequest
        {
            RuleKey = "SALE_INVOICE",
            Amounts = new Dictionary<PostingAmountSource, decimal>
            {
                [PostingAmountSource.Net] = 100.00m,
                [PostingAmountSource.Gross] = 120.00m
            },
            CurrencyCode = "BGN",
            EntryDate = new DateTimeOffset(2026, 6, 4, 0, 0, 0, TimeSpan.Zero)
        };
    }

    private static IReadOnlyCollection<string> ErrorCodes(ValidationResult result)
    {
        return result.Errors.Select(failure => failure.ErrorCode).ToList();
    }
}
