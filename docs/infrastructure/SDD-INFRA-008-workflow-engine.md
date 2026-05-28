# SDD-INFRA-008 — Workflow Engine (State Machine)

> Status: Planned
> Owner: Platform
> Last updated: 2026-05-28
> Category: Infrastructure
> Related: SDD-INFRA-001, SDD-FIN-002 (Journal Entry lifecycle), SDD-INV-001 (Invoice lifecycle), SDD-PAY-001 (Payment lifecycle), SDD-FIN-004 (Fiscal Period)
> Mirrors: Warehouse `Warehouse.Common.Workflow`

---

## 1. Context & Scope

This spec defines `Finance.Common.Workflow`, a small state-machine framework used for the lifecycle of every Finance aggregate that has more than two states:

- **Journal Entry:** `Draft → Posted → Reversed`
- **Invoice (Purchase or Sale):** `Draft → Confirmed → Posted → Settled → Archived` (with `Cancelled` from any non-terminal state)
- **Credit Note / Debit Note:** `Draft → Issued → Settled`
- **Payment:** `Draft → Recorded → Allocated → Settled` (with `Reversed`)
- **Fiscal Period:** `Open → Closing → Closed → Locked`

The engine enforces "no illegal transitions" at the **type system** level (each state class declares which next states it allows) and runs transition guards + side effects via DI-registered handlers.

**In scope:**
- `IWorkflowState<TAggregate>` — one state per concrete type
- `IWorkflowEngine<TAggregate>` — orchestrator
- `WorkflowContext<TAggregate>` — carries the aggregate, requested target state, and request metadata
- Transition guards (sync DB checks before allowing the move)
- Transition side effects (publish event, write audit row, allocate document number)
- Concurrency control via EF Core `RowVersion` on each transition-eligible aggregate
- DI extension `services.AddWorkflowEngine<TAggregate>()` that scans for state implementations

**Out of scope:**
- Long-running, durable workflows (saga). Defer to MassTransit Saga when needed.
- UI-side state machines (the React app derives allowed actions from a `transitions` array returned by the API).
- Persistence of in-flight workflow context — every transition is atomic.

## 2. Behavior

### 2.1 State interface (MUST)
```csharp
public interface IWorkflowState<TAggregate>
{
    string StateName { get; }
    IReadOnlySet<string> AllowedNextStates { get; }
    Task<Result> OnEnterAsync(WorkflowContext<TAggregate> context, CancellationToken ct);
    Task<Result> OnExitAsync(WorkflowContext<TAggregate> context, CancellationToken ct);
}
```
- `StateName` MUST match the enum value stored on the aggregate.
- `AllowedNextStates` is a hard whitelist — no other transitions MAY be performed via the engine.

### 2.2 Engine `TransitionAsync` (MUST)
1. Load current state implementation from DI by name.
2. Verify the target is in `AllowedNextStates`. If not, return `Result.Failure(INVALID_STATE_TRANSITION)`.
3. Run all `IChainValidator<WorkflowContext<TAggregate>>` registered for this transition (e.g., "period must be open for Confirmed → Posted").
4. Call `OnExitAsync` on the current state.
5. Update the aggregate's state field. Increment `RowVersion`.
6. Call `OnEnterAsync` on the new state (typical side effects: publish a domain event via outbox, allocate a document number, write an audit row).
7. `SaveChanges` inside the existing UoW.
8. Return `Result.Success`.

### 2.3 Optimistic concurrency (MUST)
- The aggregate MUST have a `RowVersion` (`byte[]`) column configured via `.IsRowVersion()`.
- Concurrent transition attempts MUST result in one of them failing with `DbUpdateConcurrencyException`, which the engine MUST translate to `Result.Failure(CONCURRENT_MODIFICATION)`.

### 2.4 Audit trail (MUST)
- Every successful transition MUST write a row to the aggregate's `<Aggregate>StatusHistory` table: `(AggregateId, FromState, ToState, ChangedBy, ChangedAt, CorrelationId, Reason)`.
- The history table is append-only (no updates / deletes).

### 2.5 Reversal special case (MUST — Journal Entry)
- `Posted → Reversed` does NOT mutate the journal entry's lines. It MUST create a **new** journal entry whose lines are the sign-flipped lines of the original, linked via `ReversedJournalEntryId`. The original's state moves to `Reversed`; the new entry's state is `Posted`.

### 2.6 Cancellation special case (MUST — Documents)
- `Cancelled` is reachable from `Draft` and `Confirmed` only. Once `Posted`, an invoice MUST be reversed via a Credit Note rather than cancelled (financial regulators require an audit trail).

## 3. Validation Rules

- State name uniqueness: registering two `IWorkflowState<T>` with the same `StateName` MUST fail at startup.
- Reachability: a startup check SHOULD verify every declared state has at least one path from the initial state (warning, not error).

## 4. Error Rules

| Code | HTTP | Trigger |
|---|---|---|
| `INVALID_STATE_TRANSITION` | 409 | Target not in `AllowedNextStates` |
| `CONCURRENT_MODIFICATION` | 409 | RowVersion mismatch |
| `WORKFLOW_GUARD_FAILED` | 409 | A registered `IChainValidator<WorkflowContext<...>>` returned Failure |
| `STATE_NOT_REGISTERED` | 500 | Aggregate has a state value with no DI implementation |

Constants live in `Finance.Common.ErrorCodes.WorkflowErrorCodes`.

## 5. Versioning Notes

v1 mechanics described above. Adding a new state to an existing aggregate is a `CHG-ENH-*` and requires a database migration to extend the state enum + the `AllowedNextStates` declarations.

## 6. Test Plan

| Test | Kind |
|---|---|
| `Transition_AllowedNextState_Succeeds` | [Unit] |
| `Transition_DisallowedNextState_ReturnsInvalidStateTransition` | [Unit] |
| `Transition_RunsOnExitThenOnEnter` | [Unit] |
| `Transition_AppendsStatusHistoryRow_WithCorrelationId` | [Integration] |
| `Transition_ConcurrentCallers_OneFailsWithConcurrentModification` | [Integration] |
| `Transition_GuardFailure_ShortCircuits_NoSideEffects` | [Integration] |
| `JournalEntryReversal_CreatesSignFlippedNewEntry_LinkedToOriginal` | [Integration] |
| `Engine_FailsAtStartup_WhenDuplicateStateNamesRegistered` | [Unit] |

## 7. Open Items

- Frontend exposure: should `GET /api/v1/<aggregate>/{id}` include `availableTransitions: [...]` so React can disable buttons? Likely yes — adopt in Phase 4.
- Workflow visualization tooling — defer.
