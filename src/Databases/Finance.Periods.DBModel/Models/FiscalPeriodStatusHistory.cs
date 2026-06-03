namespace Finance.Periods.DBModel.Models;

/// <summary>
/// An append-only record of one workflow state transition of a <see cref="FiscalPeriod"/>
/// (SDD-FIN-004 §2.4, §2.5; SDD-INFRA-008 §2.4). Written by the service inside the same transaction as the
/// transition it describes; never mutated or deleted.
/// </summary>
public sealed class FiscalPeriodStatusHistory
{
    /// <summary>Internal surrogate identifier.</summary>
    public int Id { get; set; }

    /// <summary>Foreign key to the period whose state changed.</summary>
    public int FiscalPeriodId { get; set; }

    /// <summary>Navigation to the owning period.</summary>
    public FiscalPeriod? FiscalPeriod { get; set; }

    /// <summary>The state the period transitioned from (<c>null</c> for the initial history row).</summary>
    public string? FromStatus { get; set; }

    /// <summary>The state the period transitioned to.</summary>
    public required string ToStatus { get; set; }

    /// <summary>The identifier of the user who performed the transition.</summary>
    public Guid ChangedBy { get; set; }

    /// <summary>The UTC-offset moment the transition occurred.</summary>
    public DateTimeOffset ChangedAt { get; set; }

    /// <summary>The ambient correlation identifier tying the row to the originating request.</summary>
    public required string CorrelationId { get; set; }

    /// <summary>Optional operator-supplied reason carried on sensitive transitions (close / reopen).</summary>
    public string? Reason { get; set; }
}
