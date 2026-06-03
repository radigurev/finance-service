using Finance.Common.ErrorCodes;
using Finance.Common.Results;
using Finance.Common.Validation;
using Finance.Journal.API.Services;
using Finance.Journal.API.Tests.Builders;
using Finance.Journal.API.Tests.Fixtures;
using Finance.Journal.API.Validation;
using Finance.Journal.API.Validators;
using Finance.ServiceModel.Journal;
using NUnit.Framework;

namespace Finance.Journal.API.Tests.Unit.Validation;

/// <summary>
/// Unit tests for the double-entry validation surface (<see cref="JournalEntryValidator"/>) covering the
/// balance invariant, per-line debit-XOR-credit / no-zero / no-negative rules, the minimum-two-lines rule,
/// account postability, currency validity, and multi-currency base-amount reconciliation
/// (SDD-FIN-001 §2.3-§2.9, §6.1-§6.3). The surface is pure: it runs the FluentValidation shape rules first,
/// then the cross-aggregate chain, with the in-memory <see cref="FakeReferenceDataReader"/> standing in for
/// the gateway reads so the tests run fully offline.
/// </summary>
[TestFixture]
[Category("SDD-FIN-001")]
public sealed class JournalEntryValidatorTests
{
    private const string BaseCurrency = "BGN";

    private FakeReferenceDataReader _referenceData = null!;
    private JournalEntryValidator _validator = null!;

    /// <summary>Builds a fresh validator with an all-postable/all-active reference reader before each test.</summary>
    [SetUp]
    public void SetUp()
    {
        _referenceData = new FakeReferenceDataReader();
        _validator = new JournalEntryValidator(
            new JournalEntryShapeValidator(),
            new ValidationChain<JournalEntryValidationContext>(
            [
                new BalanceValidator(),
                new LineBaseAmountValidator(),
                new AccountPostabilityValidator(_referenceData),
                new LineCurrencyValidator(_referenceData)
            ]));
    }

    /// <summary>A balanced single-currency entry passes the full surface (SDD-FIN-001 §2.3, §6.1).</summary>
    [Test]
    public async Task Validate_BalancedSingleCurrencyEntry_Succeeds()
    {
        // Arrange
        JournalEntryValidationContext context = ContextFor(
            JournalEntryLineRequestBuilder.Create().WithAccountId(1).AsDebit(100.00m).Build(),
            JournalEntryLineRequestBuilder.Create().WithAccountId(2).AsCredit(100.00m).Build());

        // Act
        Result result = await _validator.ValidateAsync(context, CancellationToken.None);

        // Assert
        Assert.That(result.IsSuccess, Is.True, result.ErrorCode);
    }

    /// <summary>A multi-currency entry that balances in base currency passes (SDD-FIN-001 §2.9, §6.1).</summary>
    [Test]
    public async Task Validate_BalancedMultiCurrencyEntry_BalancesInBaseCurrency_Succeeds()
    {
        // Arrange — EUR debit of 100.00 (base 195.58) against a BGN credit of 195.58 (base 195.58).
        JournalEntryLineRequest eurDebit = JournalEntryLineRequestBuilder.Create()
            .WithAccountId(1)
            .WithCurrency("EUR")
            .WithExchangeRate(1.955800m)
            .WithRawAmounts(100.00m, 0m)
            .WithBaseAmounts(195.58m, 0m)
            .Build();
        JournalEntryLineRequest bgnCredit = JournalEntryLineRequestBuilder.Create()
            .WithAccountId(2)
            .AsCredit(195.58m)
            .Build();
        JournalEntryValidationContext context = ContextFor(eurDebit, bgnCredit);

        // Act
        Result result = await _validator.ValidateAsync(context, CancellationToken.None);

        // Assert
        Assert.That(result.IsSuccess, Is.True, result.ErrorCode);
    }

    /// <summary>Base debits not equal to base credits fails with UNBALANCED_ENTRY (SDD-FIN-001 §2.3, §6.1).</summary>
    [Test]
    public async Task Validate_BaseDebitsNotEqualBaseCredits_ReturnsUnbalancedEntry()
    {
        // Arrange
        JournalEntryValidationContext context = ContextFor(
            JournalEntryLineRequestBuilder.Create().WithAccountId(1).AsDebit(100.00m).Build(),
            JournalEntryLineRequestBuilder.Create().WithAccountId(2).AsCredit(90.00m).Build());

        // Act
        Result result = await _validator.ValidateAsync(context, CancellationToken.None);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo(JournalErrorCodes.UNBALANCED_ENTRY));
        });
    }

    /// <summary>An off-by-one-cent residual fails with UNBALANCED_ENTRY (SDD-FIN-001 §2.9, §6.1).</summary>
    [Test]
    public async Task Validate_OffByOneCent_ReturnsUnbalancedEntry()
    {
        // Arrange
        JournalEntryValidationContext context = ContextFor(
            JournalEntryLineRequestBuilder.Create().WithAccountId(1).AsDebit(100.00m).Build(),
            JournalEntryLineRequestBuilder.Create().WithAccountId(2).AsCredit(100.01m).Build());

        // Act
        Result result = await _validator.ValidateAsync(context, CancellationToken.None);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo(JournalErrorCodes.UNBALANCED_ENTRY));
        });
    }

    /// <summary>All lines on the debit side fails with UNBALANCED_ENTRY (SDD-FIN-001 §2.9, §6.1).</summary>
    [Test]
    public async Task Validate_AllLinesOnDebitSide_ReturnsUnbalancedEntry()
    {
        // Arrange
        JournalEntryValidationContext context = ContextFor(
            JournalEntryLineRequestBuilder.Create().WithAccountId(1).AsDebit(50.00m).Build(),
            JournalEntryLineRequestBuilder.Create().WithAccountId(2).AsDebit(50.00m).Build());

        // Act
        Result result = await _validator.ValidateAsync(context, CancellationToken.None);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo(JournalErrorCodes.UNBALANCED_ENTRY));
        });
    }

    /// <summary>A line carrying both a debit and credit fails LINE_DEBIT_AND_CREDIT_SET (SDD-FIN-001 §2.4, §6.1).</summary>
    [Test]
    public async Task Validate_LineWithBothDebitAndCredit_ReturnsLineDebitAndCreditSet()
    {
        // Arrange
        JournalEntryLineRequest bothSides = JournalEntryLineRequestBuilder.Create()
            .WithAccountId(1)
            .WithRawAmounts(100.00m, 100.00m)
            .WithBaseAmounts(100.00m, 100.00m)
            .Build();
        JournalEntryValidationContext context = ContextFor(
            bothSides,
            JournalEntryLineRequestBuilder.Create().WithAccountId(2).AsCredit(100.00m).Build());

        // Act
        Result result = await _validator.ValidateAsync(context, CancellationToken.None);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo(JournalErrorCodes.LINE_DEBIT_AND_CREDIT_SET));
        });
    }

    /// <summary>A zero-amount line fails LINE_HAS_NO_AMOUNT (SDD-FIN-001 §2.4, §6.1).</summary>
    [Test]
    public async Task Validate_LineWithZeroAmounts_ReturnsLineHasNoAmount()
    {
        // Arrange
        JournalEntryLineRequest zeroLine = JournalEntryLineRequestBuilder.Create()
            .WithAccountId(1)
            .WithRawAmounts(0m, 0m)
            .WithBaseAmounts(0m, 0m)
            .Build();
        JournalEntryValidationContext context = ContextFor(
            zeroLine,
            JournalEntryLineRequestBuilder.Create().WithAccountId(2).AsCredit(100.00m).Build());

        // Act
        Result result = await _validator.ValidateAsync(context, CancellationToken.None);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo(JournalErrorCodes.LINE_HAS_NO_AMOUNT));
        });
    }

    /// <summary>A negative-amount line fails LINE_HAS_NO_AMOUNT (SDD-FIN-001 §2.4, §6.1).</summary>
    [Test]
    public async Task Validate_LineWithNegativeAmount_ReturnsLineHasNoAmount()
    {
        // Arrange
        JournalEntryLineRequest negativeLine = JournalEntryLineRequestBuilder.Create()
            .WithAccountId(1)
            .WithRawAmounts(-100.00m, 0m)
            .WithBaseAmounts(-100.00m, 0m)
            .Build();
        JournalEntryValidationContext context = ContextFor(
            negativeLine,
            JournalEntryLineRequestBuilder.Create().WithAccountId(2).AsCredit(100.00m).Build());

        // Act
        Result result = await _validator.ValidateAsync(context, CancellationToken.None);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo(JournalErrorCodes.LINE_HAS_NO_AMOUNT));
        });
    }

    /// <summary>A single-line entry fails MIN_TWO_LINES_REQUIRED (SDD-FIN-001 §2.5, §6.1).</summary>
    [Test]
    public async Task Validate_EntryWithSingleLine_ReturnsMinTwoLinesRequired()
    {
        // Arrange
        JournalEntryValidationContext context = ContextFor(
            JournalEntryLineRequestBuilder.Create().WithAccountId(1).AsDebit(100.00m).Build());

        // Act
        Result result = await _validator.ValidateAsync(context, CancellationToken.None);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo(JournalErrorCodes.MIN_TWO_LINES_REQUIRED));
        });
    }

    /// <summary>An entry with no lines fails MIN_TWO_LINES_REQUIRED (SDD-FIN-001 §2.5, §6.1).</summary>
    [Test]
    public async Task Validate_EntryWithNoLines_ReturnsMinTwoLinesRequired()
    {
        // Arrange
        JournalEntryValidationContext context = ContextFor();

        // Act
        Result result = await _validator.ValidateAsync(context, CancellationToken.None);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo(JournalErrorCodes.MIN_TWO_LINES_REQUIRED));
        });
    }

    /// <summary>A line to an inactive account fails ACCOUNT_NOT_POSTABLE (SDD-FIN-001 §2.6, §6.2).</summary>
    [Test]
    public async Task Validate_LineToInactiveAccount_ReturnsAccountNotPostable()
    {
        // Arrange
        _referenceData.MarkAccountNotPostable(2);
        JournalEntryValidationContext context = ContextFor(
            JournalEntryLineRequestBuilder.Create().WithAccountId(1).AsDebit(100.00m).Build(),
            JournalEntryLineRequestBuilder.Create().WithAccountId(2).AsCredit(100.00m).Build());

        // Act
        Result result = await _validator.ValidateAsync(context, CancellationToken.None);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo(JournalErrorCodes.ACCOUNT_NOT_POSTABLE));
        });
    }

    /// <summary>A line to a header/parent account fails ACCOUNT_NOT_POSTABLE (SDD-FIN-001 §2.6, §2.9, §6.2).</summary>
    [Test]
    public async Task Validate_LineToHeaderAccount_ReturnsAccountNotPostable()
    {
        // Arrange — a header account is reported as not-postable by the reference reader.
        _referenceData.MarkAccountNotPostable(1);
        JournalEntryValidationContext context = ContextFor(
            JournalEntryLineRequestBuilder.Create().WithAccountId(1).AsDebit(100.00m).Build(),
            JournalEntryLineRequestBuilder.Create().WithAccountId(2).AsCredit(100.00m).Build());

        // Act
        Result result = await _validator.ValidateAsync(context, CancellationToken.None);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo(JournalErrorCodes.ACCOUNT_NOT_POSTABLE));
        });
    }

    /// <summary>A line to a missing account fails ACCOUNT_NOT_POSTABLE (SDD-FIN-001 §2.6, §6.2).</summary>
    [Test]
    public async Task Validate_LineToMissingAccount_ReturnsAccountNotPostable()
    {
        // Arrange — a missing account is reported as not-postable.
        _referenceData.MarkAccountNotPostable(999);
        JournalEntryValidationContext context = ContextFor(
            JournalEntryLineRequestBuilder.Create().WithAccountId(999).AsDebit(100.00m).Build(),
            JournalEntryLineRequestBuilder.Create().WithAccountId(2).AsCredit(100.00m).Build());

        // Act
        Result result = await _validator.ValidateAsync(context, CancellationToken.None);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo(JournalErrorCodes.ACCOUNT_NOT_POSTABLE));
        });
    }

    /// <summary>A line to a leaf active account passes postability (SDD-FIN-001 §2.6, §6.2).</summary>
    [Test]
    public async Task Validate_LineToLeafActiveAccount_Succeeds()
    {
        // Arrange — default reader treats every account as postable.
        JournalEntryValidationContext context = ContextFor(
            JournalEntryLineRequestBuilder.Create().WithAccountId(10).AsDebit(100.00m).Build(),
            JournalEntryLineRequestBuilder.Create().WithAccountId(20).AsCredit(100.00m).Build());

        // Act
        Result result = await _validator.ValidateAsync(context, CancellationToken.None);

        // Assert
        Assert.That(result.IsSuccess, Is.True, result.ErrorCode);
    }

    /// <summary>A malformed currency code fails INVALID_LINE_CURRENCY at the shape stage (SDD-FIN-001 §3.1, §6.2).</summary>
    [Test]
    public async Task Validate_LineWithMalformedCurrency_ReturnsInvalidLineCurrency()
    {
        // Arrange
        JournalEntryLineRequest malformed = JournalEntryLineRequestBuilder.Create()
            .WithAccountId(1)
            .AsDebit(100.00m)
            .WithCurrency("bg")
            .Build();
        JournalEntryValidationContext context = ContextFor(
            malformed,
            JournalEntryLineRequestBuilder.Create().WithAccountId(2).AsCredit(100.00m).Build());

        // Act
        Result result = await _validator.ValidateAsync(context, CancellationToken.None);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo(JournalErrorCodes.INVALID_LINE_CURRENCY));
        });
    }

    /// <summary>A well-formed but inactive currency fails INVALID_LINE_CURRENCY via the chain (SDD-FIN-001 §2.7, §6.2).</summary>
    [Test]
    public async Task Validate_LineWithInactiveCurrency_ReturnsInvalidLineCurrency()
    {
        // Arrange — USD is well-formed but the reference reader reports it inactive.
        _referenceData.MarkCurrencyInactive("USD");
        JournalEntryLineRequest usdDebit = JournalEntryLineRequestBuilder.Create()
            .WithAccountId(1)
            .WithCurrency("USD")
            .WithExchangeRate(1.800000m)
            .WithRawAmounts(100.00m, 0m)
            .WithBaseAmounts(180.00m, 0m)
            .Build();
        JournalEntryValidationContext context = ContextFor(
            usdDebit,
            JournalEntryLineRequestBuilder.Create().WithAccountId(2).AsCredit(180.00m).Build());

        // Act
        Result result = await _validator.ValidateAsync(context, CancellationToken.None);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo(JournalErrorCodes.INVALID_LINE_CURRENCY));
        });
    }

    /// <summary>A base-currency line with a rate other than 1.000000 fails (SDD-FIN-001 §2.7, §6.3).</summary>
    [Test]
    public async Task Validate_BaseCurrencyLine_RequiresRateOfOne()
    {
        // Arrange — a BGN line carrying a non-unit rate.
        JournalEntryLineRequest badRate = JournalEntryLineRequestBuilder.Create()
            .WithAccountId(1)
            .WithCurrency(BaseCurrency)
            .WithExchangeRate(1.200000m)
            .WithRawAmounts(100.00m, 0m)
            .WithBaseAmounts(100.00m, 0m)
            .Build();
        JournalEntryValidationContext context = ContextFor(
            badRate,
            JournalEntryLineRequestBuilder.Create().WithAccountId(2).AsCredit(100.00m).Build());

        // Act
        Result result = await _validator.ValidateAsync(context, CancellationToken.None);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo(JournalErrorCodes.INVALID_LINE_BASE_AMOUNT));
        });
    }

    /// <summary>A foreign line whose base amount equals amount × rate passes (SDD-FIN-001 §2.7, §6.3).</summary>
    [Test]
    public async Task Validate_ForeignLine_BaseAmountReconcilesWithAmountTimesRate_Succeeds()
    {
        // Arrange — EUR debit 50.00 × 1.955800 = 97.79 base.
        JournalEntryLineRequest eurDebit = JournalEntryLineRequestBuilder.Create()
            .WithAccountId(1)
            .WithCurrency("EUR")
            .WithExchangeRate(1.955800m)
            .WithRawAmounts(50.00m, 0m)
            .WithBaseAmounts(97.79m, 0m)
            .Build();
        JournalEntryValidationContext context = ContextFor(
            eurDebit,
            JournalEntryLineRequestBuilder.Create().WithAccountId(2).AsCredit(97.79m).Build());

        // Act
        Result result = await _validator.ValidateAsync(context, CancellationToken.None);

        // Assert
        Assert.That(result.IsSuccess, Is.True, result.ErrorCode);
    }

    /// <summary>A foreign line whose base amount mismatches amount × rate fails (SDD-FIN-001 §2.7, §6.3).</summary>
    [Test]
    public async Task Validate_ForeignLine_BaseAmountMismatch_ReturnsInvalidLineBaseAmount()
    {
        // Arrange — EUR debit 50.00 × 1.955800 ≈ 97.79 base, but 90.00 supplied.
        JournalEntryLineRequest eurDebit = JournalEntryLineRequestBuilder.Create()
            .WithAccountId(1)
            .WithCurrency("EUR")
            .WithExchangeRate(1.955800m)
            .WithRawAmounts(50.00m, 0m)
            .WithBaseAmounts(90.00m, 0m)
            .Build();
        JournalEntryValidationContext context = ContextFor(
            eurDebit,
            JournalEntryLineRequestBuilder.Create().WithAccountId(2).AsCredit(90.00m).Build());

        // Act
        Result result = await _validator.ValidateAsync(context, CancellationToken.None);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo(JournalErrorCodes.INVALID_LINE_BASE_AMOUNT));
        });
    }

    /// <summary>
    /// A foreign line with a zero exchange rate fails INVALID_LINE_BASE_AMOUNT (SDD-FIN-001 §2.9, §6.3). The
    /// base amounts are kept balanced so the balance check passes first and the rate rule is the one tripped.
    /// </summary>
    [Test]
    public async Task Validate_ForeignLine_ZeroRate_ReturnsInvalidLineBaseAmount()
    {
        // Arrange — a zero-rate EUR debit whose base amount (97.79) nonetheless balances the credit line.
        JournalEntryLineRequest eurDebit = JournalEntryLineRequestBuilder.Create()
            .WithAccountId(1)
            .WithCurrency("EUR")
            .WithExchangeRate(0m)
            .WithRawAmounts(50.00m, 0m)
            .WithBaseAmounts(97.79m, 0m)
            .Build();
        JournalEntryValidationContext context = ContextFor(
            eurDebit,
            JournalEntryLineRequestBuilder.Create().WithAccountId(2).AsCredit(97.79m).Build());

        // Act
        Result result = await _validator.ValidateAsync(context, CancellationToken.None);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo(JournalErrorCodes.INVALID_LINE_BASE_AMOUNT));
        });
    }

    private static JournalEntryValidationContext ContextFor(params JournalEntryLineRequest[] lines)
    {
        return new JournalEntryValidationContext
        {
            BaseCurrencyCode = BaseCurrency,
            Lines = lines
        };
    }
}
