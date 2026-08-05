using Finance.Payments.DBModel.Models;

namespace Finance.Payments.API.Services;

/// <summary>
/// The read shape produced by the allocation list query (SDD-PAY-002 §2.7): one allocation row LEFT-joined to
/// its invoice open item in the SAME database, which is the permitted way to enrich the row without any
/// cross-service read.
/// <para>The join is expressed in the query rather than through an EF navigation on purpose:
/// <c>PaymentAllocation.InvoiceId</c> is a CROSS-SERVICE reference and MUST NOT be a foreign key, and EF cannot
/// model a relationship without emitting one. <see cref="OpenItem"/> is therefore nullable, even though the
/// allocation invariant chain guarantees a projection row existed when the match was created.</para>
/// </summary>
public sealed record PaymentAllocationProjectionRow
{
    /// <summary>The allocation row being listed.</summary>
    public required PaymentAllocation Allocation { get; init; }

    /// <summary>The matched invoice's local projection row, or <c>null</c> when absent.</summary>
    public InvoiceOpenItem? OpenItem { get; init; }
}
