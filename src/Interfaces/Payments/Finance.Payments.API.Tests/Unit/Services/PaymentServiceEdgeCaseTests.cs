using Finance.Common.Enums;
using Finance.Common.ErrorCodes;
using Finance.Common.Results;
using Finance.Payments.API.Tests.Builders;
using Finance.Payments.API.Tests.Fixtures;
using Finance.Payments.DBModel;
using Finance.Payments.DBModel.Models;
using Finance.ServiceModel.Payments;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;

namespace Finance.Payments.API.Tests.Unit.Services;

/// <summary>
/// Unit tests for the SDD-PAY-001 §2.18 edge cases that need more than one request's view of the same row: the
/// CONCURRENT CONFIRM race, and the settlement account that is deactivated AFTER the payment left <c>Draft</c>.
/// <para>The race is modelled deterministically — a sibling <c>PaymentsDbContext</c> over the SAME SQLite
/// in-memory connection reads the draft BEFORE the winner commits, so the loser writes with the stale
/// <c>rowversion</c> token the caller round-tripped. No threads, no wall clock, no delays.</para>
/// <para>"Exactly ONE gapless number is consumed" is pinned here at the PERSISTED level — one document number
/// in the database, the first of the series, one status-history row, and one value drawn by the winner. The
/// counter itself is a per-service fake, so the counter's own rollback under real SQL <c>UPDLOCK, HOLDLOCK</c>
/// belongs to the deferred §6.7 integration row
/// <c>Confirm_AllocatesGaplessDocumentNumbers_NoGaps_PerDocumentType</c>.</para>
/// </summary>
[TestFixture]
[Category("SDD-PAY-001")]
public sealed class PaymentServiceEdgeCaseTests
{
    private SqlitePaymentsDbContextScope _scope = null!;
    private PaymentServiceTestHarness _harness = null!;
    private PaymentsDbContext? _siblingContext;

    /// <summary>Creates a fresh SQLite-backed harness before each test.</summary>
    [SetUp]
    public void SetUp()
    {
        _scope = SqlitePaymentsDbContextFactory.Create();
        _harness = PaymentServiceTestHarness.Build(_scope.Context);
    }

    /// <summary>Disposes the sibling context, when one was created, and then the SQLite scope.</summary>
    [TearDown]
    public void TearDown()
    {
        _siblingContext?.Dispose();
        _siblingContext = null;
        _scope.Dispose();
    }

    [Test]
    public async Task Confirm_TwoConcurrentConfirmsOfTheSameDraft_LoserReturnsConcurrentModification()
    {
        // Arrange
        PaymentDto draft = await CreateDraftAsync();
        PaymentServiceTestHarness loser = await BuildRacingHarnessAsync(draft.Id);

        // Act
        Result<PaymentDto> winnerResult = await ConfirmWithAsync(_harness, draft);
        Result<PaymentDto> loserResult = await ConfirmWithAsync(loser, draft);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(winnerResult.IsSuccess, Is.True, winnerResult.ErrorCode);
            Assert.That(loserResult.IsSuccess, Is.False);
            Assert.That(loserResult.ErrorCode, Is.EqualTo(CommonErrorCodes.CONCURRENT_MODIFICATION));
        });
    }

    [Test]
    public async Task Confirm_TwoConcurrentConfirmsOfTheSameDraft_ConsumeExactlyOneGaplessNumber()
    {
        // Arrange
        PaymentDto draft = await CreateDraftAsync();
        PaymentServiceTestHarness loser = await BuildRacingHarnessAsync(draft.Id);

        // Act
        Result<PaymentDto> winnerResult = await ConfirmWithAsync(_harness, draft);
        await ConfirmWithAsync(loser, draft);

        // Assert
        Payment stored = await LoadAsync(draft.Id);
        Assert.Multiple(() =>
        {
            Assert.That(stored.Status, Is.EqualTo(PaymentStatus.Confirmed));
            Assert.That(stored.DocumentNumber, Is.EqualTo(winnerResult.Value!.DocumentNumber));
            Assert.That(stored.DocumentNumber, Is.EqualTo("RCT-2026-000001"), "the winner keeps the first number");
            Assert.That(_harness.TotalSequenceValuesConsumed(), Is.EqualTo(1));
            Assert.That(
                _scope.Context.Payments.Count(payment => payment.DocumentNumber != null),
                Is.EqualTo(1),
                "the loser's rolled-back transaction never issues a second number");
            Assert.That(
                _scope.Context.PaymentStatusHistory.Count(row => row.PaymentId == draft.Id),
                Is.EqualTo(1),
                "exactly one Draft → Confirmed transition is recorded");
        });
    }

    [Test]
    public async Task Post_SettlementAccountDeactivatedAfterConfirm_StillLinksAndPosts()
    {
        // Arrange
        PaymentDto confirmed = await ConfirmAsync(await CreateDraftAsync());
        _harness.SettlementAccounts.RequestedAccountIds.Clear();
        _harness.SettlementAccounts.Outcome = FakeSettlementAccountReader.ReaderOutcome.Inactive;

        // Act
        Result result = await _harness.Service.LinkPostedJournalEntryAsync(
            confirmed.Id,
            Guid.NewGuid(),
            CancellationToken.None);

        // Assert
        Payment stored = await LoadAsync(confirmed.Id);
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True, result.ErrorCode);
            Assert.That(stored.Status, Is.EqualTo(PaymentStatus.Posted));
            Assert.That(
                _harness.SettlementAccounts.RequestedAccountIds,
                Is.Empty,
                "the account check runs on create/update/confirm only — never retroactively");
        });
    }

    [Test]
    public async Task Reverse_SettlementAccountDeactivatedAfterPost_StillReverses()
    {
        // Arrange
        Payment posted = await SeedAsync(PaymentBuilder.Create()
            .WithStatus(PaymentStatus.Posted)
            .WithJournalEntryId(Guid.NewGuid())
            .Build());
        _harness.SettlementAccounts.RequestedAccountIds.Clear();
        _harness.SettlementAccounts.Outcome = FakeSettlementAccountReader.ReaderOutcome.Inactive;

        // Act
        Result<PaymentDto> result = await _harness.Service.ReverseAsync(
            posted.Id,
            new ReversePaymentRequest
            {
                Reason = "Cash never cleared",
                RowVersion = Convert.ToBase64String(posted.RowVersion)
            },
            CancellationToken.None);

        // Assert
        Payment stored = await LoadAsync(posted.Id);
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True, result.ErrorCode);
            Assert.That(stored.Status, Is.EqualTo(PaymentStatus.Reversed));
            Assert.That(
                _harness.SettlementAccounts.RequestedAccountIds,
                Is.Empty,
                "a posted payment is never invalidated by a later account deactivation");
        });
    }

    /// <summary>
    /// Builds a second service over a sibling context and reads the draft through it, so the sibling holds the
    /// pre-confirm snapshot and its own stale <c>rowversion</c> original value when it later writes.
    /// </summary>
    /// <param name="paymentId">The draft both requests target.</param>
    /// <returns>The racing harness.</returns>
    private async Task<PaymentServiceTestHarness> BuildRacingHarnessAsync(Guid paymentId)
    {
        _siblingContext = SqlitePaymentsDbContextFactory.CreateSiblingContext(_scope);
        PaymentServiceTestHarness racing = PaymentServiceTestHarness.Build(_siblingContext);
        await _siblingContext.Payments.SingleAsync(payment => payment.Id == paymentId, CancellationToken.None);
        return racing;
    }

    /// <summary>Creates a valid draft payment through the production service path.</summary>
    /// <returns>The created payment DTO.</returns>
    private async Task<PaymentDto> CreateDraftAsync()
    {
        Result<PaymentDto> created = await _harness.Service.CreateDraftAsync(
            CreatePaymentRequestBuilder.Create().Build(),
            CancellationToken.None);

        Assert.That(created.IsSuccess, Is.True, created.ErrorCode);
        return created.Value!;
    }

    /// <summary>Confirms the supplied draft through the default harness and asserts it succeeded.</summary>
    /// <param name="draft">The draft to confirm.</param>
    /// <returns>The confirmed payment DTO.</returns>
    private async Task<PaymentDto> ConfirmAsync(PaymentDto draft)
    {
        Result<PaymentDto> confirmed = await ConfirmWithAsync(_harness, draft);
        Assert.That(confirmed.IsSuccess, Is.True, confirmed.ErrorCode);
        return confirmed.Value!;
    }

    /// <summary>Confirms the draft through the supplied harness using the token the caller originally read.</summary>
    /// <param name="harness">The harness issuing the confirm.</param>
    /// <param name="draft">The draft, carrying the base64 token captured at the prior read.</param>
    /// <returns>The confirm result.</returns>
    private static Task<Result<PaymentDto>> ConfirmWithAsync(
        PaymentServiceTestHarness harness,
        PaymentDto draft) => harness.Service.ConfirmAsync(
            draft.Id,
            new ConfirmPaymentRequest { RowVersion = draft.RowVersion },
            CancellationToken.None);

    /// <summary>Reads the persisted payment without tracking so the stored column values are asserted.</summary>
    /// <param name="id">The payment identifier.</param>
    /// <returns>The persisted payment.</returns>
    private Task<Payment> LoadAsync(Guid id) => _scope.Context.Payments
        .AsNoTracking()
        .SingleAsync(payment => payment.Id == id, CancellationToken.None);

    /// <summary>Persists a directly-built payment so a posted state can be exercised without the handshake.</summary>
    /// <param name="payment">The payment to persist.</param>
    /// <returns>The persisted payment.</returns>
    private async Task<Payment> SeedAsync(Payment payment)
    {
        _scope.Context.Payments.Add(payment);
        await _scope.Context.SaveChangesAsync(CancellationToken.None);
        return payment;
    }
}
