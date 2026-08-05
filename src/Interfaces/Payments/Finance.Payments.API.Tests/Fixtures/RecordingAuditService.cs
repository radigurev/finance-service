using Finance.Common.ErrorCodes;
using Finance.Common.Results;
using Finance.Infrastructure.Audit;
using Finance.Infrastructure.Audit.Interfaces;
using Finance.Infrastructure.Audit.Models;

namespace Finance.Payments.API.Tests.Fixtures;

/// <summary>
/// An <see cref="IAuditService"/> test double that records every entry onto a SHARED timeline (alongside the
/// published events) so the audit-first ordering of SDD-AUDIT-001 is assertable, while enforcing the SAME
/// invariants the real <c>AuditService&lt;TContext&gt;</c> enforces: a <c>Create</c> entry MUST carry a null
/// <c>BeforeJson</c>, every other operation MUST carry a non-empty one (the real service THROWS
/// <see cref="ArgumentException"/>), and a sensitive event type without a reason fails with
/// <c>AUDIT_REASON_REQUIRED</c>.
/// <para>Enforcing the invariants here is deliberate: a service that handed the audit layer an empty
/// <c>BeforeJson</c> would fault in production, so the double must not be more permissive than the real thing.</para>
/// </summary>
public sealed class RecordingAuditService : IAuditService
{
    private readonly List<object> _timeline;

    /// <summary>Creates the double writing onto the supplied shared timeline.</summary>
    /// <param name="timeline">The shared, ordered list of audit entries and published events.</param>
    public RecordingAuditService(List<object> timeline)
    {
        ArgumentNullException.ThrowIfNull(timeline);
        _timeline = timeline;
    }

    /// <inheritdoc />
    public Task<Result> RecordAsync(
        AuditEntry entry,
        CancellationToken cancellationToken,
        bool saveChanges = false)
    {
        ArgumentNullException.ThrowIfNull(entry);

        EnforceBeforeJsonInvariant(entry);

        if (SensitiveAuditEventTypes.RequiresReason(entry.EventType) && string.IsNullOrWhiteSpace(entry.Reason))
        {
            return Task.FromResult(Result.Failure(
                AuditErrorCodes.AUDIT_REASON_REQUIRED,
                $"A reason is required for the high-sensitivity audit event '{entry.EventType}'."));
        }

        _timeline.Add(entry);
        return Task.FromResult(Result.Success());
    }

    private static void EnforceBeforeJsonInvariant(AuditEntry entry)
    {
        if (entry.Operation == AuditOperation.Create)
        {
            if (entry.BeforeJson is not null)
            {
                throw new ArgumentException("BeforeJson must be null for a Create audit entry", nameof(entry));
            }

            return;
        }

        if (string.IsNullOrEmpty(entry.BeforeJson))
        {
            throw new ArgumentException(
                $"BeforeJson must be non-empty for a {entry.Operation} audit entry", nameof(entry));
        }
    }
}
