namespace Finance.Common.Enums;

/// <summary>
/// Lifecycle state of a fiscal period (SDD-FIN-004 §2.1). The transitions are <c>Open → Closed</c> (close)
/// and <c>Closed → Open</c> (reopen). The value is stored as its string name so the workflow engine
/// resolves states by <c>StateName</c>.
/// </summary>
public enum FiscalPeriodStatus
{
    /// <summary>The period accepts postings; transactions may be recorded into it.</summary>
    Open = 1,

    /// <summary>The period is closed; its balances are frozen and postings are rejected.</summary>
    Closed = 2
}
