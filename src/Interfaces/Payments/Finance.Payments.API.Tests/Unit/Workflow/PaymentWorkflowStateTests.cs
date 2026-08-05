using Finance.Common.Enums;
using Finance.Common.Workflow;
using Finance.Payments.API.Workflow;
using Finance.Payments.DBModel.Models;
using NUnit.Framework;

namespace Finance.Payments.API.Tests.Unit.Workflow;

/// <summary>
/// Unit tests for the five <see cref="IWorkflowState{Payment}"/> implementations (SDD-PAY-001 §2.1, §6.1). They pin
/// the state machine itself: <c>Draft → { Confirmed, Cancelled }</c>, <c>Confirmed → { Posted }</c> with
/// <c>Cancelled</c> DELIBERATELY absent, <c>Posted → { Reversed }</c>, and both terminal states empty.
/// </summary>
[TestFixture]
[Category("SDD-PAY-001")]
public sealed class PaymentWorkflowStateTests
{
    [Test]
    public void Workflow_DraftAllowsConfirmedAndCancelled_ConfirmedAllowsPostedOnly_PostedAllowsReversed()
    {
        // Arrange
        DraftPaymentState draft = new();
        ConfirmedPaymentState confirmed = new();
        PostedPaymentState posted = new();

        // Act
        IReadOnlySet<string> fromDraft = draft.AllowedNextStates;
        IReadOnlySet<string> fromConfirmed = confirmed.AllowedNextStates;
        IReadOnlySet<string> fromPosted = posted.AllowedNextStates;

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(draft.StateName, Is.EqualTo(nameof(PaymentStatus.Draft)));
            Assert.That(fromDraft, Is.EquivalentTo(new[]
            {
                nameof(PaymentStatus.Confirmed),
                nameof(PaymentStatus.Cancelled)
            }));
            Assert.That(confirmed.StateName, Is.EqualTo(nameof(PaymentStatus.Confirmed)));
            Assert.That(fromConfirmed, Is.EquivalentTo(new[] { nameof(PaymentStatus.Posted) }));
            Assert.That(
                fromConfirmed,
                Does.Not.Contain(nameof(PaymentStatus.Cancelled)),
                "Confirmed → Cancelled is deliberately absent (SDD-PAY-001 §2.1).");
            Assert.That(posted.StateName, Is.EqualTo(nameof(PaymentStatus.Posted)));
            Assert.That(fromPosted, Is.EquivalentTo(new[] { nameof(PaymentStatus.Reversed) }));
        });
    }

    [Test]
    public void Workflow_CancelledAndReversed_AreTerminal()
    {
        // Arrange
        CancelledPaymentState cancelled = new();
        ReversedPaymentState reversed = new();

        // Act
        IReadOnlySet<string> fromCancelled = cancelled.AllowedNextStates;
        IReadOnlySet<string> fromReversed = reversed.AllowedNextStates;

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(fromCancelled, Is.Empty);
            Assert.That(fromReversed, Is.Empty);
            Assert.That(cancelled.StateName, Is.EqualTo(nameof(PaymentStatus.Cancelled)));
            Assert.That(reversed.StateName, Is.EqualTo(nameof(PaymentStatus.Reversed)));
        });
    }

    [Test]
    public async Task Workflow_StateEntryAndExit_RunNoSideEffects()
    {
        // Arrange
        DraftPaymentState draft = new();
        WorkflowContext<Payment> context = new()
        {
            Aggregate = new Payment
            {
                CurrencyCode = "BGN",
                BaseCurrencyCode = "BGN",
                CorrelationId = "correlation"
            },
            CurrentState = nameof(PaymentStatus.Draft),
            TargetState = nameof(PaymentStatus.Confirmed),
            CorrelationId = "correlation"
        };

        // Act
        Task enter = draft.OnEnterAsync(context, CancellationToken.None);
        Task exit = draft.OnExitAsync(context, CancellationToken.None);
        await Task.WhenAll(enter, exit);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(enter.IsCompletedSuccessfully, Is.True);
            Assert.That(exit.IsCompletedSuccessfully, Is.True);
            Assert.That(context.Aggregate.Status, Is.EqualTo(PaymentStatus.Draft));
        });
    }
}
