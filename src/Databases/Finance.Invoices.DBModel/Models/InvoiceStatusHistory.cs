namespace Finance.Invoices.DBModel.Models;

/// <summary>
/// An append-only record of one workflow state transition of an <see cref="Invoice"/>
/// (SDD-INV-001 §2.4-§2.7; SDD-INFRA-008 §2.4). Written by the service inside the same transaction as the
/// transition it describes.
/// </summary>
public sealed class InvoiceStatusHistory
{
    /// <summary>Internal surrogate identifier.</summary>
    public int Id { get; set; }

    /// <summary>Foreign key to the invoice whose state changed.</summary>
    public Guid InvoiceId { get; set; }

    /// <summary>Navigation to the owning invoice.</summary>
    public Invoice? Invoice { get; set; }

    /// <summary>The state the invoice transitioned from (<c>null</c> for the initial history row).</summary>
    public string? FromStatus { get; set; }

    /// <summary>The state the invoice transitioned to.</summary>
    public required string ToStatus { get; set; }

    /// <summary>The identifier of the user who performed the transition.</summary>
    public Guid ChangedBy { get; set; }

    /// <summary>The UTC-offset moment the transition occurred.</summary>
    public DateTimeOffset ChangedAt { get; set; }

    /// <summary>The ambient correlation identifier tying the row to the originating request.</summary>
    public required string CorrelationId { get; set; }

    /// <summary>Optional operator-supplied reason carried on sensitive transitions (e.g. cancellation).</summary>
    public string? Reason { get; set; }
}
