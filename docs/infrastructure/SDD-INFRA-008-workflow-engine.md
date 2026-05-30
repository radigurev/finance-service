# SDD-INFRA-008 — Workflow Engine (State Machine)

> Status: Active (Batch 1 — interfaces + `WorkflowContext` + `WorkflowErrorCodes` in `Finance.Common/Workflow` shipping; concrete `WorkflowEngine<T>` + `AddWorkflowEngine` deferred to Batch 2)
> Owner: Platform
> Last updated: 2026-05-30
> Category: Infrastructure
> Related: SDD-INFRA-001, SDD-INFRA-007, SDD-INFRA-009, SDD-FIN-002 (Journal Entry lifecycle), SDD-INV-001 (Invoice lifecycle), SDD-PAY-001 (Payment lifecycle), SDD-FIN-004 (Fiscal Period)
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

### Resolved Decision — assembly split (Batch 1 vs Batch 2)
- **Batch 1 (`src/Finance.Common/Workflow/`) — interfaces and contracts only, no EF Core dependency:**
  - `IWorkflowState<TAggregate>` — `string StateName`, `IReadOnlySet<string> AllowedNextStates`, `Task OnEnterAsync(WorkflowContext<TAggregate>, CancellationToken)`, `Task OnExitAsync(WorkflowContext<TAggregate>, CancellationToken)`.
  - `IWorkflowEngine<TAggregate>` — `Task<Result> TransitionAsync(WorkflowContext<TAggregate>, CancellationToken)`.
  - `WorkflowContext<TAggregate>` — `TAggregate Aggregate`, `string TargetState`, `string? Reason`, `string CorrelationId`.
  - `WorkflowErrorCodes` (see §4) ships now via `src/Finance.Common/ErrorCodes/`.
- **Batch 2 (`src/Finance.Infrastructure.Services/`) — concrete implementation, needs EF Core:**
  - `WorkflowEngine<TAggregate>` (the `TransitionAsync` orchestration in §2.2, including `RowVersion` concurrency, status-history append, and `SaveChanges` within the UoW).
  - The `services.AddWorkflowEngine<TAggregate>()` DI extension that scans for `IWorkflowState<TAggregate>` implementations.
- Rationale: the orchestrator touches `DbContext`, `RowVersion`, and the outbox, so it cannot live in the pure `Finance.Common` library. Keeping the interfaces in `Finance.Common` lets domain assemblies declare states without taking an infrastructure dependency.

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
- **Resolved Decision (Batch 1) — authoritative interface signatures:** `OnEnterAsync` and `OnExitAsync` return `Task` (not `Task<Result>`); state-entry/-exit side effects either succeed or throw, and the engine surfaces transition outcome as a single `Result` from `IWorkflowEngine<TAggregate>.TransitionAsync`. Guard / validation failures are reported by the chain (§2.2 step 3, SDD-INFRA-007), not by the state hooks. The code block above is illustrative; the shipped `Finance.Common/Workflow` signatures are those recorded in the assembly-split decision in §1.

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
- Concurrent transition attempts MUST result in one of them failing with `DbUpdateConcurrencyException`, which the engine MUST translate to `Result.Failure(CommonErrorCodes.CONCURRENT_MODIFICATION)`.
- **Resolved Decision (Batch 1):** `CONCURRENT_MODIFICATION` has a **single source** — `CommonErrorCodes` in `src/Finance.Common/ErrorCodes/`. `WorkflowErrorCodes` does NOT redefine it; both this engine and `SDD-INFRA-009` reference `CommonErrorCodes.CONCURRENT_MODIFICATION`. The `DbUpdateConcurrencyException → Result.Failure(...)` translation lives in the Batch-2 `WorkflowEngine<TAggregate>` (EF Core dependency), not in the Batch-1 interfaces.

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

| Code | Source class | HTTP | Trigger |
|---|---|---|---|
| `INVALID_STATE_TRANSITION` | `WorkflowErrorCodes` | 409 | Target not in `AllowedNextStates` |
| `WORKFLOW_GUARD_FAILED` | `WorkflowErrorCodes` | 409 | A registered `IChainValidator<WorkflowContext<...>>` returned Failure |
| `STATE_NOT_REGISTERED` | `WorkflowErrorCodes` | 500 | Aggregate has a state value with no DI implementation |
| `CONCURRENT_MODIFICATION` | `CommonErrorCodes` | 409 | RowVersion mismatch |

`WorkflowErrorCodes` (`INVALID_STATE_TRANSITION`, `WORKFLOW_GUARD_FAILED`, `STATE_NOT_REGISTERED`) lives in `src/Finance.Common/ErrorCodes/WorkflowErrorCodes.cs` and ships in Batch 1. `CONCURRENT_MODIFICATION` is **not** redefined here — it is referenced from `CommonErrorCodes.CONCURRENT_MODIFICATION` (single source). Each constant is a `public const string` whose value equals its own name. The HTTP mapping is applied by `BaseApiController.ToActionResult` (SDD-INFRA-009) in the Batch-2 web layer.

## 5. Versioning Notes

v1 mechanics described above. Adding a new state to an existing aggregate is a `CHG-ENH-*` and requires a database migration to extend the state enum + the `AllowedNextStates` declarations.

## 6. Test Plan

The tests below are scheduled against the batch that ships the code they exercise. Integration tests need a real DB and are `[Category("Integration")]` (excluded from the default Batch-1 run — no SQL Server in this environment).

| Test | Kind | Batch |
|---|---|---|
| `Transition_AllowedNextState_Succeeds` | [Unit] | Batch 2 (needs `WorkflowEngine<T>`) |
| `Transition_DisallowedNextState_ReturnsInvalidStateTransition` | [Unit] | Batch 2 |
| `Transition_RunsOnExitThenOnEnter` | [Unit] | Batch 2 |
| `Transition_AppendsStatusHistoryRow_WithCorrelationId` | [Integration] | Batch 2 |
| `Transition_ConcurrentCallers_OneFailsWithConcurrentModification` | [Integration] | Batch 2 |
| `Transition_GuardFailure_ShortCircuits_NoSideEffects` | [Integration] | Batch 2 |
| `JournalEntryReversal_CreatesSignFlippedNewEntry_LinkedToOriginal` | [Integration] | Batch 2 |
| `Engine_FailsAtStartup_WhenDuplicateStateNamesRegistered` | [Unit] | Batch 2 |

Batch 1 ships only the interfaces, `WorkflowContext<TAggregate>`, and `WorkflowErrorCodes`; there is no executable engine behavior to unit-test yet. The first executable engine tests land in Batch 2 in `src/Finance.Infrastructure.Services.Tests` (created in Batch 2) alongside `WorkflowEngine<T>`.

## 7. Resolved Decisions & Deferred Items

### Resolved (Batch 1)
- **Assembly split:** interfaces + `WorkflowContext` + `WorkflowErrorCodes` ship in `Finance.Common/Workflow` (+ `Finance.Common/ErrorCodes`); the concrete `WorkflowEngine<TAggregate>` and `AddWorkflowEngine<TAggregate>()` are deferred to Batch 2 in `Finance.Infrastructure.Services` (see §1 assembly-split decision).
- **`CONCURRENT_MODIFICATION` ownership:** single source in `CommonErrorCodes`; `WorkflowErrorCodes` references it, never redefines it.
- **State-hook return type:** `OnEnterAsync` / `OnExitAsync` return `Task` (see §2.1 decision).

### Deferred
- Frontend exposure: `GET /api/v1/<aggregate>/{id}` including `availableTransitions: [...]` so React can disable buttons — adopt in the relevant frontend phase.
- Workflow visualization tooling — defer.
