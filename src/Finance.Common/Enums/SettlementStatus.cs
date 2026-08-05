namespace Finance.Common.Enums;

/// <summary>
/// The settlement state of a financial document, DERIVED by exact <c>DECIMAL(18,2)</c> comparison of the
/// settled amount against the document gross total (SDD-PAY-002 §2.8). It is never stored as an independent
/// workflow state and is never transitioned through <c>IWorkflowEngine&lt;T&gt;</c>: settlement is a pure
/// function of two decimals, computed in exactly one place (<c>SettlementStatusCalculator</c>).
/// <para>There is exactly ONE settlement enum in the solution. It lives here precisely so both services share
/// it: the SDD-INV-001 settlement amendment binds <c>Invoice.SettlementStatus</c> to this type and SDD-PAY-003
/// reports it — neither declares a parallel <c>InvoiceSettlementStatus</c>. There is no tolerance band and no
/// automatic write-off: a gross total less one cent is <see cref="PartiallySettled"/>, not
/// <see cref="Settled"/>.</para>
/// </summary>
public enum SettlementStatus
{
    /// <summary>Nothing has been matched against the document (settled amount is exactly <c>0.00</c>).</summary>
    Unsettled = 1,

    /// <summary>Part of the document is matched (settled amount is above zero and below the gross total).</summary>
    PartiallySettled = 2,

    /// <summary>The document is fully matched (settled amount equals — or defensively exceeds — the gross total).</summary>
    Settled = 3
}
