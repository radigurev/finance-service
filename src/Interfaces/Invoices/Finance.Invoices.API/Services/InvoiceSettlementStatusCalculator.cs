using Finance.Common.Enums;

namespace Finance.Invoices.API.Services;

/// <summary>
/// The SINGLE place this service derives the invoice's <see cref="SettlementStatus"/> (SDD-INV-001 §2.14). The
/// derivation MUST NOT be duplicated in the AutoMapper profile, on the DTO, or in the allocation-event
/// consumers — all of them read this calculator.
/// <para>Settlement is a pure function of two decimals, so it is never stored as an independent workflow state
/// and never transitioned through <c>IWorkflowEngine&lt;Invoice&gt;</c>. Comparison is EXACT at two decimal
/// places: there is no epsilon, no tolerance band, and no automatic write-off, so a gross total less one cent
/// is <see cref="SettlementStatus.PartiallySettled"/>, not <see cref="SettlementStatus.Settled"/>.</para>
/// <para>This is an independent copy of the SDD-PAY-002 §2.8 derivation on the other side of the service
/// boundary — deliberate (neither service may depend on the other being reachable) and asserted equal by the
/// cross-service integration test. The two MUST NOT be allowed to diverge.</para>
/// </summary>
public sealed class InvoiceSettlementStatusCalculator
{
    /// <summary>
    /// Derives the settlement state from the invoice's mirrored settled amount and its gross total.
    /// </summary>
    /// <param name="settledAmount">The authoritative settled amount mirrored from the allocation events.</param>
    /// <param name="grossTotal">The invoice gross total the settled amount may never exceed.</param>
    /// <returns>
    /// <see cref="SettlementStatus.Unsettled"/> when nothing is matched;
    /// <see cref="SettlementStatus.PartiallySettled"/> when part is matched;
    /// <see cref="SettlementStatus.Settled"/> when the settled amount reaches the gross total. The
    /// greater-than branch is defensive only — SDD-PAY-002 §2.5 forbids over-allocation at the source, and the
    /// consumers reject a breach instead of persisting it.
    /// </returns>
    public SettlementStatus Calculate(decimal settledAmount, decimal grossTotal)
    {
        if (settledAmount == 0m)
        {
            return SettlementStatus.Unsettled;
        }

        if (settledAmount >= grossTotal)
        {
            return SettlementStatus.Settled;
        }

        return SettlementStatus.PartiallySettled;
    }
}
