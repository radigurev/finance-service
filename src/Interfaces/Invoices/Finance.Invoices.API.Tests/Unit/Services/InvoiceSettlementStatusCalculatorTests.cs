using Finance.Common.Enums;
using Finance.Invoices.API.Services;
using Finance.Invoices.API.Workflow;
using Finance.Invoices.DBModel.Models;
using NUnit.Framework;

namespace Finance.Invoices.API.Tests.Unit.Services;

/// <summary>
/// Unit tests for <see cref="InvoiceSettlementStatusCalculator"/> — the SINGLE place this service derives
/// <see cref="SettlementStatus"/> (SDD-INV-001 §2.14, §6.7).
/// <para>Comparison is EXACT at two decimal places: there is no epsilon, no tolerance band, and no automatic
/// write-off, so a gross total less ONE CENT is <see cref="SettlementStatus.PartiallySettled"/>. The derivation is
/// also ORTHOGONAL to the lifecycle — the enum's members never appear in any workflow state's
/// <c>AllowedNextStates</c> set.</para>
/// </summary>
[TestFixture]
[Category("SDD-INV-001")]
[Category("SDD-PAY-002")]
public sealed class InvoiceSettlementStatusCalculatorTests
{
    private InvoiceSettlementStatusCalculator _sut = null!;

    /// <summary>Creates a fresh calculator before each test.</summary>
    [SetUp]
    public void SetUp()
    {
        _sut = new InvoiceSettlementStatusCalculator();
    }

    /// <summary>Exactly zero settled derives Unsettled (§2.14).</summary>
    [Test]
    public void Calculate_ZeroSettled_IsUnsettled()
    {
        // Arrange
        decimal settledAmount = 0.00m;

        // Act
        SettlementStatus status = _sut.Calculate(settledAmount, 1000.00m);

        // Assert
        Assert.That(status, Is.EqualTo(SettlementStatus.Unsettled));
    }

    /// <summary>A settled amount exactly equal to the gross total derives Settled by exact comparison (§2.14).</summary>
    [Test]
    public void Calculate_ExactGrossTotal_IsSettled_ExactDecimalComparison()
    {
        // Arrange
        decimal settledAmount = 1000.00m;

        // Act
        SettlementStatus status = _sut.Calculate(settledAmount, 1000.00m);

        // Assert
        Assert.That(status, Is.EqualTo(SettlementStatus.Settled));
    }

    /// <summary>
    /// One cent short of the gross total is PartiallySettled — there is NO tolerance band and no residual
    /// write-off (§2.14).
    /// </summary>
    [Test]
    public void Calculate_OneCentShortOfGrossTotal_IsPartiallySettled_NoTolerance()
    {
        // Arrange
        decimal settledAmount = 999.99m;

        // Act
        SettlementStatus status = _sut.Calculate(settledAmount, 1000.00m);

        // Assert
        Assert.That(status, Is.EqualTo(SettlementStatus.PartiallySettled));
    }

    /// <summary>The greater-than branch is defensive only and still derives Settled (§2.14).</summary>
    [Test]
    public void Calculate_AboveGrossTotal_IsSettled_DefensiveBranch()
    {
        // Arrange
        decimal settledAmount = 1000.01m;

        // Act
        SettlementStatus status = _sut.Calculate(settledAmount, 1000.00m);

        // Assert
        Assert.That(status, Is.EqualTo(SettlementStatus.Settled));
    }

    /// <summary>Any amount strictly between zero and the gross total derives PartiallySettled (§2.14).</summary>
    [TestCase(0.01, 1000.00)]
    [TestCase(300.00, 1000.00)]
    [TestCase(999.98, 1000.00)]
    public void Calculate_BetweenZeroAndGrossTotal_IsPartiallySettled(decimal settledAmount, decimal grossTotal)
    {
        // Arrange — calculator created in SetUp.

        // Act
        SettlementStatus status = _sut.Calculate(settledAmount, grossTotal);

        // Assert
        Assert.That(status, Is.EqualTo(SettlementStatus.PartiallySettled));
    }

    /// <summary>
    /// The shared enum owns exactly three members with the values SDD-PAY-002 §2.8 declares — there is no
    /// invoice-specific fork (§2.14).
    /// </summary>
    [Test]
    public void SettlementStatus_SharedEnum_DefinesUnsettledPartiallySettledAndSettled()
    {
        // Arrange & Act
        SettlementStatus[] values = Enum.GetValues<SettlementStatus>();

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(
                values,
                Is.EquivalentTo(new[]
                {
                    SettlementStatus.Unsettled,
                    SettlementStatus.PartiallySettled,
                    SettlementStatus.Settled
                }));
            Assert.That((int)SettlementStatus.Unsettled, Is.EqualTo(1));
            Assert.That((int)SettlementStatus.PartiallySettled, Is.EqualTo(2));
            Assert.That((int)SettlementStatus.Settled, Is.EqualTo(3));
        });
    }

    /// <summary>
    /// Settlement is ORTHOGONAL to the lifecycle: no settlement-status name appears in any invoice workflow
    /// state's AllowedNextStates set (§2.14, §6.7).
    /// </summary>
    [Test]
    public void SettlementStatus_IsNotAWorkflowState_AbsentFromEveryAllowedNextStatesSet()
    {
        // Arrange
        List<Finance.Common.Workflow.IWorkflowState<Invoice>> states =
        [
            new DraftInvoiceState(),
            new ConfirmedInvoiceState(),
            new PostedInvoiceState(),
            new CancelledInvoiceState(),
            new ReversedInvoiceState()
        ];
        HashSet<string> settlementNames = new(
            Enum.GetNames<SettlementStatus>(), StringComparer.Ordinal);

        // Act
        IEnumerable<string> declaredAndReachable = states
            .Select(state => state.StateName)
            .Concat(states.SelectMany(state => state.AllowedNextStates));

        // Assert
        Assert.That(declaredAndReachable.Intersect(settlementNames, StringComparer.Ordinal), Is.Empty);
    }
}
