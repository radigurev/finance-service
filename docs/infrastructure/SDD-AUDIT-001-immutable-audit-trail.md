# SDD-AUDIT-001 — Immutable Audit Trail

> Status: Planned
> Owner: Finance / Compliance
> Last updated: 2026-05-28
> Category: Infrastructure
> Related: SDD-INFRA-006, SDD-EVTLOG-001, SDD-INFRA-008, SDD-OBS-001

---

## 1. Context & Scope

Bulgarian and EU accounting law require an **immutable** record of every change to financial data: who, what, when, why. Loki retains operational logs only ~30 days; a separate, long-retention store is required for audit. This spec defines `IAuditService` and the `audit.OperationsEvents` table that satisfies that requirement.

The audit trail is **complementary** to:
- Loki logs (short-term, technical detail)
- `eventlog.OperationsEvents` (cross-service domain-event archive, SDD-EVTLOG-001)
- The aggregate's own `<Aggregate>StatusHistory` table (SDD-INFRA-008 workflow trail)

Audit captures the **legally meaningful** intersection: any change that affects a ledger row, an invoice number, a payment allocation, a fiscal period state, or RBAC permissions on finance resources.

**In scope:**
- `IAuditService` interface with `RecordAsync(AuditEntry)`
- `audit.OperationsEvents` table: append-only, INSERT-only permission for the application user
- Mandatory fields: `EventType`, `EntityType`, `EntityId`, `ChangedBy`, `ChangedAt`, `CorrelationId`, `BeforeJson`, `AfterJson`, `Reason`
- DB-level guard: `audit.OperationsEvents` is in a separate SQL Schema with a DENY UPDATE / DENY DELETE grant for the app user
- Retention: minimum 10 years (Bulgaria — Закон за счетоводството чл. 12)
- Export endpoint for tax-authority audits: `GET /api/v1/audit/export?from=...&to=...&format=csv|json`
- Frontend "audit log" panel on each aggregate's detail view

**Out of scope:**
- General-purpose change-data-capture (CDC) on every table — only legally-meaningful events
- Tamper-proof hashing (Merkle chain). Considered for Phase 8 if regulators demand it.
- Cross-system audit (Warehouse audit trail is owned by Warehouse — Finance only audits its own writes).

## 2. Behavior

### 2.1 What MUST be audited
- Every successful workflow transition (SDD-INFRA-008) — `OnEnterAsync` MUST call `IAuditService.RecordAsync`.
- Every journal entry post and reversal.
- Every invoice confirmation, posting, settlement, cancellation.
- Every payment recording, allocation, reversal.
- Every fiscal period state change (Open → Closing → Closed → Locked).
- Every chart-of-accounts mutation (create, update, deactivate).
- Every RBAC permission grant / revoke on `finance.*` permissions (the auth-service originates this; Finance only mirrors locally for legal export).

### 2.2 What MUST NOT be audited
- Read-only queries (GET endpoints).
- Cache invalidations, health-check pings, observability scrapes.
- Failed validation attempts — those are operational, not legally-binding.

### 2.3 Audit-entry shape (MUST)
```csharp
public sealed record AuditEntry
{
    public required string EventType { get; init; }       // "JournalEntryPosted", "InvoiceCancelled", ...
    public required string EntityType { get; init; }      // "JournalEntry", "Invoice"
    public required string EntityId { get; init; }        // stringified PK
    public required Guid UserId { get; init; }
    public required string Username { get; init; }
    public required DateTimeOffset OccurredAt { get; init; }
    public required Guid CorrelationId { get; init; }
    public string? BeforeJson { get; init; }              // pre-change snapshot (null on create)
    public required string AfterJson { get; init; }       // post-change snapshot
    public string? Reason { get; init; }                  // operator-supplied "Why" for sensitive ops
}
```

### 2.4 Write semantics (MUST)
- `IAuditService.RecordAsync` MUST write inside the SAME EF Core transaction as the change it describes. Either both commit or neither does.
- If the change publishes a MassTransit event (SDD-INFRA-006), the audit row MUST be written before the outbox row to preserve the legal "audit-first" ordering (visible to compliance regardless of bus delivery).

### 2.5 Immutability enforcement (MUST)
- A SQL migration MUST grant `audit` schema usage `WITH SELECT, INSERT` only on the application user. `UPDATE` and `DELETE` MUST be DENIED.
- A nightly job SHOULD verify the row count is monotonically non-decreasing (anyone who tampered would have to bypass DB ACLs).

### 2.6 Retention (MUST)
- Audit rows MUST be retained for at least 10 years.
- Archival to cheap storage (Parquet on object storage) is permitted after 2 years online; the API MUST be able to seamlessly query both tiers.

### 2.7 Export (MUST — Phase 8)
- `GET /api/v1/audit/export?from=...&to=...&format=csv|json` returns audit rows filtered by occurrence date.
- Requires permission `finance.audit:export`.
- Pagination via cursor (`continuationToken`) — exports can be large.

### 2.8 Frontend (MUST — Phase 4+)
- Each aggregate detail page (Journal Entry, Invoice, Payment, Period) MUST display its audit log in chronological order with `EventType`, `Username`, `OccurredAt`, `Reason`.
- The audit panel MUST allow filtering by date range and `EventType`.

## 3. Validation Rules

- `EventType`, `EntityType`, `EntityId`, `UserId`, `OccurredAt`, `CorrelationId`, `AfterJson` MUST be non-null.
- `BeforeJson` MUST be `null` for create events and non-null for update / delete / state-change events.
- `Reason` MUST be supplied for high-sensitivity events (period close, journal reversal, account deactivation, permission revocation).

## 4. Error Rules

| Code | HTTP | Trigger |
|---|---|---|
| `AUDIT_REASON_REQUIRED` | 400 | Sensitive operation attempted without `reason` |
| `AUDIT_TAMPERING_DETECTED` | (alert) | Nightly verification detected row-count regression |

Constants live in `Finance.Common.ErrorCodes.AuditErrorCodes`.

## 5. Versioning Notes

v1 covers the events listed in §2.1. Adding more event types is additive. The shape of `AuditEntry` is **stable**: changes require a major bump and a migration plan (compliance impact).

## 6. Test Plan

| Test | Kind |
|---|---|
| `RecordAsync_PersistsRowInSameTransactionAsAggregateChange` | [Integration] |
| `RecordAsync_WritesBeforeOutboxRow` | [Integration] |
| `RecordAsync_RollsBack_WhenAggregateSaveFails` | [Integration] |
| `DbSchema_DenyUpdate_OnAuditOperationsEvents` | [Integration] |
| `DbSchema_DenyDelete_OnAuditOperationsEvents` | [Integration] |
| `ReasonRequired_ForPeriodClose_Returns400_WhenMissing` | [Integration] |
| `Export_FiltersByDateRange_AndPaginatesByContinuationToken` | [Integration] |
| `NightlyVerification_FailsIfRowCountDecreased` | [Unit] |

## 7. Open Items

- Tamper-proof hash chain (Merkle DAG) — adopt only if regulators ask. Implementation cost is significant.
- Cross-system audit correlation: when a Warehouse `GoodsReceiptCompletedEvent` triggers a Finance Purchase Invoice creation, the Finance audit row references the Warehouse event by `CorrelationId` and `EventId` — but Warehouse's audit is separately stored. Decide if a joint Grafana panel suffices.
