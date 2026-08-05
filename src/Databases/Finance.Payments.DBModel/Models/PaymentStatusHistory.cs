namespace Finance.Payments.DBModel.Models;

/// <summary>
/// An append-only record of one workflow state transition of a <see cref="Payment"/>
/// (SDD-PAY-001 §2.4-§2.7, §2.10; SDD-INFRA-008 §2.4). Written by the service inside the same transaction as
/// the transition it describes. Rows are never UPDATEd or DELETEd — the only removal path is the FK cascade
/// when a <c>Draft</c> (which has no history rows) is deleted.
/// </summary>
public sealed class PaymentStatusHistory
{
    /// <summary>Internal surrogate identifier.</summary>
    public int Id { get; set; }

    /// <summary>Foreign key to the payment whose state changed.</summary>
    public Guid PaymentId { get; set; }

    /// <summary>Navigation to the owning payment.</summary>
    public Payment? Payment { get; set; }

    /// <summary>The state the payment transitioned from (<c>null</c> for the initial history row).</summary>
    public string? FromStatus { get; set; }

    /// <summary>The state the payment transitioned to.</summary>
    public required string ToStatus { get; set; }

    /// <summary>The identifier of the user who performed the transition.</summary>
    public Guid ChangedBy { get; set; }

    /// <summary>The UTC-offset moment the transition occurred.</summary>
    public DateTimeOffset ChangedAt { get; set; }

    /// <summary>The ambient correlation identifier tying the row to the originating request.</summary>
    public required string CorrelationId { get; set; }

    /// <summary>Operator-supplied reason carried on the sensitive cancel/reverse transitions.</summary>
    public string? Reason { get; set; }
}
