using Finance.Common.Enums;

namespace Finance.Payments.API.Services;

/// <summary>
/// The SINGLE place the derived <see cref="SettlementStatus"/> is computed (SDD-PAY-002 §2.8). The derivation
/// MUST NOT be duplicated in the DTO mapper, in the events, or in SDD-PAY-003 — all of them read this
/// calculator.
/// <para>Settlement is a pure function of two decimals, so it is never stored as an independent workflow state
/// and never transitioned through <c>IWorkflowEngine&lt;T&gt;</c>. Comparison is EXACT at two decimal places:
/// there is no epsilon, no tolerance band, and no automatic write-off, so a gross total less one cent is
/// <see cref="SettlementStatus.PartiallySettled"/>, not <see cref="SettlementStatus.Settled"/>.</para>
/// </summary>
public sealed class SettlementStatusCalculator
{
    /// <summary>
    /// Derives the settlement state from the locally-owned settled amount and the document gross total.
    /// </summary>
    /// <param name="settledAmount">The sum of the invoice's allocation rows in this database.</param>
    /// <param name="grossTotal">The invoice gross total the allocations may never exceed.</param>
    /// <returns>
    /// <see cref="SettlementStatus.Unsettled"/> when nothing is matched;
    /// <see cref="SettlementStatus.PartiallySettled"/> when part is matched;
    /// <see cref="SettlementStatus.Settled"/> when the settled amount reaches the gross total. The
    /// greater-than branch is defensive only — over-allocation is forbidden by the invariant chain, so it is
    /// unreachable through the v1 paths.
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
