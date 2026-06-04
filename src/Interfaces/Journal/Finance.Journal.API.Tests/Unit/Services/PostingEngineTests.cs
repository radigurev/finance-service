using Finance.Common.Enums;
using Finance.Common.ErrorCodes;
using Finance.Common.Results;
using Finance.Country.Abstractions;
using Finance.Journal.API.Tests.Builders;
using Finance.Journal.API.Tests.Fixtures;
using Finance.ServiceModel.Journal;
using Finance.ServiceModel.Posting;
using Moq;
using NUnit.Framework;

namespace Finance.Journal.API.Tests.Unit.Services;

/// <summary>
/// Unit tests for <see cref="Finance.Journal.API.Services.PostingEngine"/> (SDD-FIN-006 §6.1) against a
/// mocked <see cref="Finance.Journal.API.Interfaces.IJournalEntryService"/>: the happy path resolves an
/// active rule, maps <c>Net</c>/<c>Tax</c>/<c>Gross</c> to balanced debit/credit lines, resolves account
/// codes via the faked reader, and delegates a balanced <see cref="CreateJournalEntryRequest"/> to the JE
/// service (posting when <c>PostImmediately</c>); the engine fails early with <c>POSTING_RULE_NOT_FOUND</c>
/// (unknown/inactive), <c>MISSING_POSTING_AMOUNT</c>, <c>POSTING_RULE_UNBALANCED</c>, and
/// <c>POSTING_RULE_ACCOUNT_NOT_FOUND</c> BEFORE any draft is created; account overrides redirect a line;
/// the base currency comes from the country strategy; a JE-path failure propagates; and the engine emits no
/// new event and does not double-audit (it never creates its own number — it delegates). Runs offline
/// against a SQLite in-memory context for rule resolution.
/// </summary>
[TestFixture]
[Category("SDD-FIN-006")]
public sealed class PostingEngineTests
{
    private static readonly DateTimeOffset EntryDate = new(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);

    private SqliteJournalDbContextScope _scope = null!;
    private PostingEngineTestHarness _harness = null!;

    /// <summary>Creates a fresh SQLite-backed posting-engine harness before each test.</summary>
    [SetUp]
    public void SetUp()
    {
        _scope = SqliteJournalDbContextFactory.Create();
        _harness = PostingEngineTestHarness.Build(_scope.Context);
        RegisterSaleInvoiceAccounts();
    }

    /// <summary>Disposes the SQLite scope after each test.</summary>
    [TearDown]
    public void TearDown()
    {
        _scope.Dispose();
    }

    [Test]
    public async Task Apply_ValidRule_BuildsBalancedCreateRequest_CallsCreateDraft()
    {
        // Arrange
        await _harness.SeedRule(PostingRuleBuilder.Create().WithRuleKey("SALE_INVOICE").Build());
        ApplyPostingRuleRequest request = SaleInvoiceApply(net: 100.00m, tax: 20.00m, gross: 120.00m);

        // Act
        Result<JournalEntryDto> result = await _harness.Engine.ApplyAsync(request, CancellationToken.None);

        // Assert
        Assert.That(result.IsSuccess, Is.True, result.ErrorCode);
        CreateJournalEntryRequest captured = _harness.CapturedCreateRequests.Single();
        decimal debits = captured.Lines.Sum(line => line.BaseDebitAmount);
        decimal credits = captured.Lines.Sum(line => line.BaseCreditAmount);
        Assert.Multiple(() =>
        {
            Assert.That(captured.Lines, Has.Count.EqualTo(3));
            Assert.That(debits, Is.EqualTo(credits));
            Assert.That(debits, Is.EqualTo(120.00m));
        });
    }

    [Test]
    public async Task Apply_PostImmediatelyTrue_CallsPostAsync_AfterCreateDraft()
    {
        // Arrange
        await _harness.SeedRule(PostingRuleBuilder.Create().WithRuleKey("SALE_INVOICE").Build());
        ApplyPostingRuleRequest request = SaleInvoiceApply(100.00m, 20.00m, 120.00m) with { PostImmediately = true };

        // Act
        Result<JournalEntryDto> result = await _harness.Engine.ApplyAsync(request, CancellationToken.None);

        // Assert
        Assert.That(result.IsSuccess, Is.True, result.ErrorCode);
        _harness.JournalServiceMock.Verify(
            s => s.PostAsync(It.IsAny<Guid>(), It.IsAny<PostJournalEntryRequest>(), It.IsAny<CancellationToken>()),
            Times.Once);
        Assert.That(result.Value!.Status, Is.EqualTo(JournalEntryStatus.Posted));
    }

    [Test]
    public async Task Apply_PostImmediatelyFalse_LeavesEntryAsDraft_NoPostCall()
    {
        // Arrange
        await _harness.SeedRule(PostingRuleBuilder.Create().WithRuleKey("SALE_INVOICE").Build());
        ApplyPostingRuleRequest request = SaleInvoiceApply(100.00m, 20.00m, 120.00m) with { PostImmediately = false };

        // Act
        Result<JournalEntryDto> result = await _harness.Engine.ApplyAsync(request, CancellationToken.None);

        // Assert
        Assert.That(result.IsSuccess, Is.True, result.ErrorCode);
        _harness.JournalServiceMock.Verify(
            s => s.PostAsync(It.IsAny<Guid>(), It.IsAny<PostJournalEntryRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
        Assert.That(result.Value!.Status, Is.EqualTo(JournalEntryStatus.Draft));
    }

    [Test]
    public async Task Apply_DebitLine_PlacesAmountOnDebitSide_CreditLineOnCreditSide()
    {
        // Arrange
        await _harness.SeedRule(PostingRuleBuilder.Create().WithRuleKey("SALE_INVOICE").Build());
        ApplyPostingRuleRequest request = SaleInvoiceApply(100.00m, 20.00m, 120.00m);

        // Act
        Result<JournalEntryDto> result = await _harness.Engine.ApplyAsync(request, CancellationToken.None);

        // Assert
        Assert.That(result.IsSuccess, Is.True, result.ErrorCode);
        CreateJournalEntryRequest captured = _harness.CapturedCreateRequests.Single();
        JournalEntryLineRequest receivable = captured.Lines.Single(line => line.AccountId == 411);
        JournalEntryLineRequest revenue = captured.Lines.Single(line => line.AccountId == 701);
        JournalEntryLineRequest vat = captured.Lines.Single(line => line.AccountId == 4532);
        Assert.Multiple(() =>
        {
            Assert.That(receivable.DebitAmount, Is.EqualTo(120.00m));
            Assert.That(receivable.CreditAmount, Is.EqualTo(0m));
            Assert.That(revenue.CreditAmount, Is.EqualTo(100.00m));
            Assert.That(revenue.DebitAmount, Is.EqualTo(0m));
            Assert.That(vat.CreditAmount, Is.EqualTo(20.00m));
        });
    }

    [Test]
    public async Task Apply_AmountSourceMapping_IsEnumDriven_NotClassPerSource()
    {
        // Arrange — a custom rule pulling each enum source onto a distinct account proves a single mapping
        // resolves all sources without a per-source type. Net+Tax debit (120) vs Gross credit (120).
        _harness.ReferenceData.RegisterAccountCode("100", 100);
        _harness.ReferenceData.RegisterAccountCode("200", 200);
        _harness.ReferenceData.RegisterAccountCode("300", 300);
        await _harness.SeedRule(PostingRuleBuilder.Create()
            .WithRuleKey("ALL_SOURCES")
            .WithLines(
                ("100", PostingDebitOrCredit.Debit, PostingAmountSource.Net),
                ("200", PostingDebitOrCredit.Debit, PostingAmountSource.Tax),
                ("300", PostingDebitOrCredit.Credit, PostingAmountSource.Gross))
            .Build());
        ApplyPostingRuleRequest request = new()
        {
            RuleKey = "ALL_SOURCES",
            Amounts = new Dictionary<PostingAmountSource, decimal>
            {
                [PostingAmountSource.Net] = 100.00m,
                [PostingAmountSource.Tax] = 20.00m,
                [PostingAmountSource.Gross] = 120.00m
            },
            CurrencyCode = "BGN",
            EntryDate = EntryDate
        };

        // Act
        Result<JournalEntryDto> result = await _harness.Engine.ApplyAsync(request, CancellationToken.None);

        // Assert
        Assert.That(result.IsSuccess, Is.True, result.ErrorCode);
        CreateJournalEntryRequest captured = _harness.CapturedCreateRequests.Single();
        Assert.Multiple(() =>
        {
            Assert.That(captured.Lines.Single(line => line.AccountId == 100).BaseDebitAmount, Is.EqualTo(100.00m));
            Assert.That(captured.Lines.Single(line => line.AccountId == 200).BaseDebitAmount, Is.EqualTo(20.00m));
            Assert.That(captured.Lines.Single(line => line.AccountId == 300).BaseCreditAmount, Is.EqualTo(120.00m));
        });
    }

    [Test]
    public async Task Apply_UnbalancedMaterializedLines_ReturnsPostingRuleUnbalanced_BeforeCreateDraft()
    {
        // Arrange — Gross (130) ≠ Net (100) + Tax (20); the materialized lines cannot net to zero.
        await _harness.SeedRule(PostingRuleBuilder.Create().WithRuleKey("SALE_INVOICE").Build());
        ApplyPostingRuleRequest request = SaleInvoiceApply(net: 100.00m, tax: 20.00m, gross: 130.00m);

        // Act
        Result<JournalEntryDto> result = await _harness.Engine.ApplyAsync(request, CancellationToken.None);

        // Assert
        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.ErrorCode, Is.EqualTo(PostingErrorCodes.POSTING_RULE_UNBALANCED));
        VerifyNoDraftCreated();
    }

    [Test]
    public async Task Apply_MissingRequiredAmount_ReturnsMissingPostingAmount_BeforeCreateDraft()
    {
        // Arrange — the SALE_INVOICE rule references Tax, but the context omits it.
        await _harness.SeedRule(PostingRuleBuilder.Create().WithRuleKey("SALE_INVOICE").Build());
        ApplyPostingRuleRequest request = new()
        {
            RuleKey = "SALE_INVOICE",
            Amounts = new Dictionary<PostingAmountSource, decimal>
            {
                [PostingAmountSource.Net] = 100.00m,
                [PostingAmountSource.Gross] = 120.00m
            },
            CurrencyCode = "BGN",
            EntryDate = EntryDate
        };

        // Act
        Result<JournalEntryDto> result = await _harness.Engine.ApplyAsync(request, CancellationToken.None);

        // Assert
        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.ErrorCode, Is.EqualTo(PostingErrorCodes.MISSING_POSTING_AMOUNT));
        VerifyNoDraftCreated();
    }

    [Test]
    public async Task Apply_UnknownRuleKey_ReturnsPostingRuleNotFound_NoCreateDraft()
    {
        // Arrange — no rule seeded for this key.
        ApplyPostingRuleRequest request = SaleInvoiceApply(100.00m, 20.00m, 120.00m) with { RuleKey = "DOES_NOT_EXIST" };

        // Act
        Result<JournalEntryDto> result = await _harness.Engine.ApplyAsync(request, CancellationToken.None);

        // Assert
        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.ErrorCode, Is.EqualTo(PostingErrorCodes.POSTING_RULE_NOT_FOUND));
        VerifyNoDraftCreated();
    }

    [Test]
    public async Task Apply_InactiveRule_ReturnsPostingRuleNotFound()
    {
        // Arrange
        await _harness.SeedRule(PostingRuleBuilder.Create().WithRuleKey("SALE_INVOICE").WithIsActive(false).Build());
        ApplyPostingRuleRequest request = SaleInvoiceApply(100.00m, 20.00m, 120.00m);

        // Act
        Result<JournalEntryDto> result = await _harness.Engine.ApplyAsync(request, CancellationToken.None);

        // Assert
        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.ErrorCode, Is.EqualTo(PostingErrorCodes.POSTING_RULE_NOT_FOUND));
        VerifyNoDraftCreated();
    }

    [Test]
    public async Task Apply_AccountOverride_RedirectsLineToOverriddenAccount()
    {
        // Arrange — redirect the 411 receivable line to a customer sub-account code 4111.
        _harness.ReferenceData.RegisterAccountCode("4111", 4111);
        await _harness.SeedRule(PostingRuleBuilder.Create().WithRuleKey("SALE_INVOICE").Build());
        ApplyPostingRuleRequest request = SaleInvoiceApply(100.00m, 20.00m, 120.00m) with
        {
            AccountOverrides = new Dictionary<string, string> { ["411"] = "4111" }
        };

        // Act
        Result<JournalEntryDto> result = await _harness.Engine.ApplyAsync(request, CancellationToken.None);

        // Assert
        Assert.That(result.IsSuccess, Is.True, result.ErrorCode);
        CreateJournalEntryRequest captured = _harness.CapturedCreateRequests.Single();
        Assert.Multiple(() =>
        {
            Assert.That(captured.Lines.Any(line => line.AccountId == 4111), Is.True);
            Assert.That(captured.Lines.Any(line => line.AccountId == 411), Is.False);
        });
    }

    [Test]
    public async Task Apply_BaseCurrencyFromCountryStrategy_PassedToCreateDraft()
    {
        // Arrange
        await _harness.SeedRule(PostingRuleBuilder.Create().WithRuleKey("SALE_INVOICE").Build());
        ApplyPostingRuleRequest request = SaleInvoiceApply(100.00m, 20.00m, 120.00m);

        // Act
        Result<JournalEntryDto> result = await _harness.Engine.ApplyAsync(request, CancellationToken.None);

        // Assert
        Assert.That(result.IsSuccess, Is.True, result.ErrorCode);
        Assert.That(_harness.CapturedBaseCurrencies.Single(), Is.EqualTo(PostingEngineTestHarness.BaseCurrencyCode));
    }

    [Test]
    public async Task Apply_AccountCodeUnresolved_ReturnsPostingRuleAccountNotFound()
    {
        // Arrange — seed a rule whose credit account code 999 is not registered with the reader.
        _harness.ReferenceData.RegisterAccountCode("411", 411);
        await _harness.SeedRule(PostingRuleBuilder.Create()
            .WithRuleKey("BAD_ACCOUNT")
            .WithLines(
                ("411", PostingDebitOrCredit.Debit, PostingAmountSource.Gross),
                ("999", PostingDebitOrCredit.Credit, PostingAmountSource.Gross))
            .Build());
        ApplyPostingRuleRequest request = new()
        {
            RuleKey = "BAD_ACCOUNT",
            Amounts = new Dictionary<PostingAmountSource, decimal> { [PostingAmountSource.Gross] = 120.00m },
            CurrencyCode = "BGN",
            EntryDate = EntryDate
        };

        // Act
        Result<JournalEntryDto> result = await _harness.Engine.ApplyAsync(request, CancellationToken.None);

        // Assert
        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.ErrorCode, Is.EqualTo(PostingErrorCodes.POSTING_RULE_ACCOUNT_NOT_FOUND));
        VerifyNoDraftCreated();
    }

    [Test]
    public async Task Apply_JournalServiceFailure_PropagatesAsResult_NotSwallowed()
    {
        // Arrange — the delegated CreateDraftAsync fails with a JE-path code; the engine must surface it.
        await _harness.SeedRule(PostingRuleBuilder.Create().WithRuleKey("SALE_INVOICE").Build());
        _harness.JournalServiceMock
            .Setup(s => s.CreateDraftAsync(
                It.IsAny<CreateJournalEntryRequest>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<JournalEntryDto>.Failure(JournalErrorCodes.ACCOUNT_NOT_POSTABLE, "Header account."));
        ApplyPostingRuleRequest request = SaleInvoiceApply(100.00m, 20.00m, 120.00m);

        // Act
        Result<JournalEntryDto> result = await _harness.Engine.ApplyAsync(request, CancellationToken.None);

        // Assert
        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.ErrorCode, Is.EqualTo(JournalErrorCodes.ACCOUNT_NOT_POSTABLE));
    }

    [Test]
    public async Task Apply_EmitsNoNewEvent_ReliesOnJournalPostedEvent()
    {
        // Arrange — the engine has no publish dependency; posting goes only through the JE service. The
        // delegated PostAsync is the sole posting path (and it owns the JournalEntryPostedEvent, SDD-FIN-002).
        await _harness.SeedRule(PostingRuleBuilder.Create().WithRuleKey("SALE_INVOICE").Build());
        ApplyPostingRuleRequest request = SaleInvoiceApply(100.00m, 20.00m, 120.00m) with { PostImmediately = true };

        // Act
        Result<JournalEntryDto> result = await _harness.Engine.ApplyAsync(request, CancellationToken.None);

        // Assert
        Assert.That(result.IsSuccess, Is.True, result.ErrorCode);
        Assert.Multiple(() =>
        {
            Assert.That(_harness.PostedEntryIds, Has.Count.EqualTo(1));
            Assert.That(typeof(Finance.Journal.API.Services.PostingEngine)
                .GetConstructors()
                .SelectMany(ctor => ctor.GetParameters())
                .Any(parameter => parameter.ParameterType == typeof(MassTransit.IPublishEndpoint)), Is.False);
        });
    }

    [Test]
    public async Task Apply_DoesNotDoubleAudit_EntryAuditOwnedByFin002()
    {
        // Arrange — the engine has no IAuditService dependency; the entry's audit belongs to the JE path.
        await _harness.SeedRule(PostingRuleBuilder.Create().WithRuleKey("SALE_INVOICE").Build());
        ApplyPostingRuleRequest request = SaleInvoiceApply(100.00m, 20.00m, 120.00m) with { PostImmediately = true };

        // Act
        Result<JournalEntryDto> result = await _harness.Engine.ApplyAsync(request, CancellationToken.None);

        // Assert
        Assert.That(result.IsSuccess, Is.True, result.ErrorCode);
        bool dependsOnAudit = typeof(Finance.Journal.API.Services.PostingEngine)
            .GetConstructors()
            .SelectMany(ctor => ctor.GetParameters())
            .Any(parameter => parameter.ParameterType == typeof(Finance.Infrastructure.Audit.Interfaces.IAuditService));
        Assert.That(dependsOnAudit, Is.False);
    }

    private void RegisterSaleInvoiceAccounts()
    {
        _harness.ReferenceData.RegisterAccountCode("411", 411);
        _harness.ReferenceData.RegisterAccountCode("701", 701);
        _harness.ReferenceData.RegisterAccountCode("4532", 4532);
    }

    private void VerifyNoDraftCreated()
    {
        _harness.JournalServiceMock.Verify(
            s => s.CreateDraftAsync(
                It.IsAny<CreateJournalEntryRequest>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private static ApplyPostingRuleRequest SaleInvoiceApply(decimal net, decimal tax, decimal gross) => new()
    {
        RuleKey = "SALE_INVOICE",
        Amounts = new Dictionary<PostingAmountSource, decimal>
        {
            [PostingAmountSource.Net] = net,
            [PostingAmountSource.Tax] = tax,
            [PostingAmountSource.Gross] = gross
        },
        CurrencyCode = "BGN",
        EntryDate = EntryDate
    };
}
