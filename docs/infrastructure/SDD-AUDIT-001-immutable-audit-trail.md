# SDD-AUDIT-001 — Immutable Audit Trail

> Status: Active (library write-path: `IAuditService`, `AuditService<TContext>`, `IAuditDbContext`, `OperationsEvent` entity + config, `AuditEntry`, `AddFinanceAudit<TContext>`). Deferred: export endpoint, frontend audit panel, Parquet archival, DB-level INSERT-only grants/migration (Batch 4+), nightly tamper verification.
> Owner: Finance / Compliance
> Last updated: 2026-05-30
> Category: Infrastructure
> Related: SDD-INFRA-006 (audit-first-before-outbox ordering), SDD-EVTLOG-001, SDD-INFRA-008, SDD-OBS-001
> Implementation: `src/Infrastructure/Audit/Finance.Infrastructure.Audit/` (references `Finance.Common`)

---

## 1. Context & Scope

Bulgarian and EU accounting law require an **immutable** record of every change to financial data: who, what, when, why. Loki retains operational logs only ~30 days; a separate, long-retention store is required for audit. This spec defines `IAuditService` and the `audit.OperationsEvents` table that satisfies that requirement.

The audit trail is **complementary** to:
- Loki logs (short-term, technical detail)
- `eventlog.OperationsEvents` (cross-service domain-event archive, SDD-EVTLOG-001)
- The aggregate's own `<Aggregate>StatusHistory` table (SDD-INFRA-008 workflow trail)

Audit captures the **legally meaningful** intersection: any change that affects a ledger row, an invoice number, a payment allocation, a fiscal period state, or RBAC permissions on finance resources.

### Batch-3 resolved decisions (write-path only)

- **Scope this batch = the write path only.** Ships now: `IAuditService.RecordAsync(AuditEntry, ct)`, `AuditService<TContext>`, the `IAuditDbContext` seam, the `OperationsEvent` EF entity + `IEntityTypeConfiguration` (schema `audit`), the `AuditEntry` record, and `AddFinanceAudit<TContext>()`.
- **Deferred:** the export endpoint (§2.7), the frontend audit panel (§2.8), Parquet archival (§2.6), the DB-level INSERT-only grant + EF migration (§2.5 — a per-service migration concern, Batch 4+), and the nightly tamper-verification job (§2.5).
- **Library location:** `src/Infrastructure/Audit/Finance.Infrastructure.Audit/`, references `Finance.Common`. Built with `dotnet build` on its own `.csproj`; it does **not** add itself to `src/Finance.slnx` (the Integrate step does that).
- **`IAuditDbContext` pattern:** the library defines `IAuditDbContext { DbSet<OperationsEvent> OperationsEvents; }`. Each service DbContext implements it; `AuditService<TContext> where TContext : DbContext, IAuditDbContext` writes through the ambient context.
- **Audit-first-before-outbox ordering:** `RecordAsync` writes the `OperationsEvent` into the caller's open transaction; the library does NOT call `SaveChanges` itself unless the caller opts in. The audit row MUST be written **before** the MassTransit outbox row (SDD-INFRA-006) so compliance sees the change regardless of bus delivery.
- Error-code constants are referenced from `Finance.Common.ErrorCodes.AuditErrorCodes` (`AUDIT_REASON_REQUIRED`, `AUDIT_TAMPERING_DETECTED`) — never raw strings.

**In scope (this batch — write path):**
- `IAuditService` interface with `RecordAsync(AuditEntry entry, CancellationToken ct)`
- `AuditService<TContext>` implementation (`where TContext : DbContext, IAuditDbContext`)
- `IAuditDbContext { DbSet<OperationsEvent> OperationsEvents; }` seam implemented by each service DbContext
- `OperationsEvent` EF entity + `IEntityTypeConfiguration` mapped to schema `audit`
- `AuditEntry` sealed record (see §2.3)
- DI extension `services.AddFinanceAudit<TContext>()`

**Out of scope:**
- General-purpose change-data-capture (CDC) on every table — only legally-meaningful events
- Tamper-proof hashing (Merkle chain). Considered for Phase 8 if regulators demand it.
- Cross-system audit (Warehouse audit trail is owned by Warehouse — Finance only audits its own writes).
- **Export endpoint** (§2.7) — deferred.
- **Frontend audit panel** (§2.8) — deferred.
- **DB-level INSERT-only grant + `audit` schema migration** (§2.5) — a per-service migration concern, deferred to Batch 4+.
- **Parquet archival** (§2.6) and the **nightly tamper-verification job** (§2.5) — deferred.

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
`AuditEntry` is the caller-facing record. `AuditService` maps it onto the `OperationsEvent` EF entity persisted through the ambient `IAuditDbContext`.
```csharp
public sealed record AuditEntry
{
    public required string EventType { get; init; }       // "JournalEntryPosted", "InvoiceCancelled", ...
    public required string EntityType { get; init; }      // "JournalEntry", "Invoice"
    public required string EntityId { get; init; }        // stringified PK
    public required Guid UserId { get; init; }
    public required string Username { get; init; }
    public required DateTimeOffset OccurredAt { get; init; }
    public required string CorrelationId { get; init; }   // ambient correlation id
    public string? BeforeJson { get; init; }              // pre-change snapshot (null on create)
    public required string AfterJson { get; init; }       // post-change snapshot
    public string? Reason { get; init; }                  // operator-supplied "Why" for sensitive ops
}
```
The persisted `OperationsEvent` entity (schema `audit`, INSERT-only) MUST carry: `EventType`, `EntityType`, `EntityId`, `UserId`, `Username`, `OccurredAt` (`DateTimeOffset`), `CorrelationId`, `BeforeJson?`, `AfterJson`, `Reason?`.

### 2.4 Write semantics (MUST)
- `IAuditService.RecordAsync` MUST write the `OperationsEvent` into the ambient `IAuditDbContext` inside the SAME EF Core transaction as the change it describes. Either both commit or neither does.
- `RecordAsync` MUST NOT call `SaveChanges` itself unless the caller explicitly opts in — the caller owns the transaction boundary (consistent with SDD-INFRA-009 service patterns).
- If the change publishes a MassTransit event (SDD-INFRA-006), the audit row MUST be written **before** the outbox row to preserve the legal "audit-first" ordering (visible to compliance regardless of bus delivery).

### 2.5 Immutability enforcement (MUST — DB grant deferred to Batch 4+)
- The `OperationsEvent` entity is mapped to a dedicated `audit` schema by the library so a per-service migration can apply INSERT-only grants.
- **Deferred (per-service migration, Batch 4+):** a SQL migration MUST grant `audit` schema usage `WITH SELECT, INSERT` only on the application user. `UPDATE` and `DELETE` MUST be DENIED.
- **Deferred:** a nightly job SHOULD verify the row count is monotonically non-decreasing (anyone who tampered would have to bypass DB ACLs).

### 2.6 Retention (MUST — archival deferred)
- Audit rows MUST be retained for at least 10 years.
- **Deferred:** archival to cheap storage (Parquet on object storage) is permitted after 2 years online; the API MUST be able to seamlessly query both tiers.

### 2.7 Export (MUST — deferred)
**Deferred** (not in the write-path batch).
- `GET /api/v1/audit/export?from=...&to=...&format=csv|json` returns audit rows filtered by occurrence date.
- Requires permission `finance.audit:export`.
- Pagination via cursor (`continuationToken`) — exports can be large.

### 2.8 Frontend (MUST — deferred)
**Deferred** (not in the write-path batch).
- Each aggregate detail page (Journal Entry, Invoice, Payment, Period) MUST display its audit log in chronological order with `EventType`, `Username`, `OccurredAt`, `Reason`.
- The audit panel MUST allow filtering by date range and `EventType`.

## 3. Validation Rules

- `EventType`, `EntityType`, `EntityId`, `UserId`, `Username`, `OccurredAt`, `CorrelationId`, `AfterJson` MUST be non-null (enforced by `required` init members).
- `BeforeJson` MUST be `null` for create events and non-null for update / delete / state-change events.
- `Reason` MUST be supplied for high-sensitivity events (period close, journal reversal, account deactivation, permission revocation). When missing, `RecordAsync` MUST return `Result.Failure(AuditErrorCodes.AUDIT_REASON_REQUIRED)` rather than persist the row.

## 4. Error Rules

| Code | HTTP | Trigger |
|---|---|---|
| `AUDIT_REASON_REQUIRED` | 400 | Sensitive operation attempted without `reason` |
| `AUDIT_TAMPERING_DETECTED` | (alert) | Nightly verification detected row-count regression |

Constants live in `Finance.Common.ErrorCodes.AuditErrorCodes`.

## 5. Versioning Notes

v1 ships the **write path** (library `IAuditService` / `AuditService<TContext>` / `IAuditDbContext` / `OperationsEvent` / `AuditEntry` / `AddFinanceAudit<TContext>`) for the events listed in §2.1. The export endpoint, frontend panel, Parquet archival, DB-level INSERT-only grants, and nightly tamper verification are **deferred** (Batch 4+ / later phases). Adding more event types is additive. The shape of `AuditEntry` is **stable**: changes require a major bump and a migration plan (compliance impact).

## 6. Test Plan

Batch-3 unit tests live in `src/Infrastructure/Finance.Infrastructure.Tests`. EF-touching unit tests use SQLite in-memory (an in-memory `IAuditDbContext`) and run by default. Tests requiring real SQL Server (`audit` schema DENY grants, real outbox ordering) are `[Category("Integration")]` and excluded from the default offline run. Export and frontend tests are deferred with their features.

| Test | Kind |
|---|---|
| `RecordAsync_PersistsOperationsEventIntoAuditDbContext` | [Unit] (SQLite in-memory `IAuditDbContext`) |
| `RecordAsync_DoesNotCallSaveChanges_WhenCallerOwnsTransaction` | [Unit] (SQLite in-memory) |
| `RecordAsync_ReturnsFailure_WhenReasonMissing_ForSensitiveOp` | [Unit] |
| `RecordAsync_AllowsNullBeforeJson_OnCreate` | [Unit] |
| `RecordAsync_RequiresBeforeJson_OnUpdateOrStateChange` | [Unit] |
| `AuditEntry_FailsToConstruct_WhenRequiredFieldMissing` | [Unit] |
| `RecordAsync_PersistsRowInSameTransactionAsAggregateChange` | [Integration] (real SQL) |
| `RecordAsync_WritesBeforeOutboxRow` | [Integration] (real SQL + outbox) |
| `RecordAsync_RollsBack_WhenAggregateSaveFails` | [Integration] (real SQL) |
| `DbSchema_DenyUpdate_OnAuditOperationsEvents` | [Integration] (real SQL grants — deferred feature) |
| `DbSchema_DenyDelete_OnAuditOperationsEvents` | [Integration] (real SQL grants — deferred feature) |

## 7. Open Items

- **Deferred features (post-write-path):** export endpoint (§2.7), frontend audit panel (§2.8), Parquet archival (§2.6), DB-level INSERT-only grant + `audit` schema migration (§2.5, per-service, Batch 4+), nightly tamper-verification job (§2.5).
- Tamper-proof hash chain (Merkle DAG) — adopt only if regulators ask. Implementation cost is significant.
- Cross-system audit correlation: when a Warehouse `GoodsReceiptCompletedEvent` triggers a Finance Purchase Invoice creation, the Finance audit row references the Warehouse event by `CorrelationId` and `EventId` — but Warehouse's audit is separately stored. Decide if a joint Grafana panel suffices.
