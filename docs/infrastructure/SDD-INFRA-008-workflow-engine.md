# SDD-INFRA-008 — Workflow Engine (State Machine)

> Status: Active (Batch 2 — the concrete `WorkflowEngine<TAggregate>` + `AddWorkflowEngine<TAggregate>()` ship in `Finance.Infrastructure.Services`, with per-aggregate keyed state registration. The Batch-1 interfaces + `WorkflowContext` + `WorkflowErrorCodes` remain in `Finance.Common/Workflow`. In v1 the engine validates + runs hooks while the calling service owns `SaveChanges` / `RowVersion` / status-history — see §2.2.)
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
- **Batch 2 (`src/Infrastructure/Services/Finance.Infrastructure.Services/`) — concrete implementation, needs EF Core:**
  - `WorkflowEngine<TAggregate>` (the `TransitionAsync` orchestration in §2.2).
  - The `services.AddWorkflowEngine<TAggregate>()` DI extension that registers `IWorkflowState<TAggregate>` implementations into a **per-aggregate keyed registry** (keyed DI / a dictionary keyed by `StateName`); a duplicate `StateName` for the same aggregate fails at startup, and a missing state at transition time yields `STATE_NOT_REGISTERED`.
- **Resolved Decision (Batch 2) — v1 persistence ownership split:** in v1 the engine **validates the transition and runs the state hooks only**; the **calling service owns `SaveChanges`, `RowVersion` increment, and the status-history append**. The engine itself does not persist. Where the engine does perform a save (a future iteration), it MUST translate `DbUpdateConcurrencyException` to `Result.Failure(CommonErrorCodes.CONCURRENT_MODIFICATION)`. This supersedes the earlier note that placed `SaveChanges` / `RowVersion` / status-history inside the engine (§2.2, §2.3, §2.4 are updated to match).
- Rationale: keeping persistence in the calling service keeps the engine usable inside the service's existing unit-of-work / outbox transaction (SDD-INFRA-006) without the engine owning the `DbContext` lifecycle. Keeping the interfaces in `Finance.Common` lets domain assemblies declare states without taking an infrastructure dependency.

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
> **Resolved Decision (Batch 2) — v1 ownership:** the engine validates the transition and runs the state hooks; the **calling service** owns the actual `SaveChanges`, the `RowVersion` increment, and the status-history append (§2.3, §2.4). The engine does NOT persist in v1.

1. Resolve the current and target `IWorkflowState<TAggregate>` by `StateName` from the per-aggregate keyed registry. If either is missing, return `Result.Failure(STATE_NOT_REGISTERED)`.
2. Verify the target is in the current state's `AllowedNextStates`. If not, return `Result.Failure(INVALID_STATE_TRANSITION)`.
3. Run all registered `IChainValidator<WorkflowContext<TAggregate>>` guards (e.g., "period must be open for Confirmed → Posted"). If any returns Failure, short-circuit and return `Result.Failure(WORKFLOW_GUARD_FAILED)` carrying the failing guard's code/detail — no `OnExit`/`OnEnter` side effects run.
4. Call `OnExitAsync` on the current state.
5. Call `OnEnterAsync` on the target state (typical side effects: publish a domain event via outbox, allocate a document number, write an audit row).
6. Return `Result.Success`. The calling service then sets the aggregate's state field, increments `RowVersion`, appends the status-history row (§2.4), and calls `SaveChanges` inside its own unit of work (SDD-INFRA-006 outbox).
7. Where a future iteration moves persistence into the engine, the engine MUST keep `TransitionAsync` ≤ 50 lines and MUST translate `DbUpdateConcurrencyException` → `Result.Failure(CommonErrorCodes.CONCURRENT_MODIFICATION)`.

### 2.3 Optimistic concurrency (MUST)
- The aggregate MUST have a `RowVersion` (`byte[]`) column configured via `.IsRowVersion()`.
- Concurrent transition attempts MUST result in one of them failing with `DbUpdateConcurrencyException`.
- **Resolved Decision (Batch 2) — v1:** because the calling service owns `SaveChanges` (§2.2), the **service** is responsible for catching `DbUpdateConcurrencyException` and translating it to `Result.Failure(CommonErrorCodes.CONCURRENT_MODIFICATION)` — most simply by calling `BaseEntityService.SaveWithConcurrencyCheckAsync` (SDD-INFRA-009 §2.1), which performs exactly this translation. If a later iteration moves the save into the engine, the engine performs the translation instead.
- **Resolved Decision (Batch 1):** `CONCURRENT_MODIFICATION` has a **single source** — `CommonErrorCodes` in `src/Finance.Common/ErrorCodes/`. `WorkflowErrorCodes` does NOT redefine it; both this engine and `SDD-INFRA-009` reference `CommonErrorCodes.CONCURRENT_MODIFICATION`. The `DbUpdateConcurrencyException → Result.Failure(...)` translation lives in the Batch-2 `WorkflowEngine<TAggregate>` (EF Core dependency), not in the Batch-1 interfaces.

### 2.4 Audit trail (MUST)
- Every successful transition MUST write a row to the aggregate's `<Aggregate>StatusHistory` table: `(AggregateId, FromState, ToState, ChangedBy, ChangedAt, CorrelationId, Reason)`.
- **Resolved Decision (Batch 2) — v1:** the status-history row is written by the **calling service** (same unit of work as the state-field update + `RowVersion` increment + `SaveChanges`, §2.2), not by the engine. The engine surfaces the validated transition outcome; the service records it.
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

The tests below are scheduled against the batch that ships the code they exercise. **Resolved Decision (Batch 2):** the executable engine tests live in `src/Infrastructure/Finance.Infrastructure.Tests` and run as `[Unit]` — the engine itself does not persist in v1 (§2.2), so its transition logic, guards, and registry resolution are testable without a database. Tests that assert the caller-side `SaveChanges` / status-history / `RowVersion` behavior against a real DB carry `[Category("Integration")]` and are excluded from the default run (no SQL Server in this environment).

| Test | Kind | Batch |
|---|---|---|
| `Transition_AllowedNextState_Succeeds` | [Unit] | Batch 2 |
| `Transition_DisallowedNextState_ReturnsInvalidStateTransition` | [Unit] | Batch 2 |
| `Transition_UnknownState_ReturnsStateNotRegistered` | [Unit] | Batch 2 |
| `Transition_GuardFailure_ReturnsWorkflowGuardFailed_NoSideEffects` | [Unit] | Batch 2 |
| `Transition_RunsOnExitThenOnEnter` | [Unit] | Batch 2 |
| `Engine_FailsAtStartup_WhenDuplicateStateNamesRegistered` | [Unit] | Batch 2 |
| `Transition_AppendsStatusHistoryRow_WithCorrelationId` | [Integration] | Batch 2 (caller-side, real SQL Server) |
| `Transition_ConcurrentCallers_OneFailsWithConcurrentModification` | [Integration] | Batch 2 (caller-side, real SQL Server) |
| `JournalEntryReversal_CreatesSignFlippedNewEntry_LinkedToOriginal` | [Integration] | Batch 2 (caller-side, real SQL Server) |

Batch 1 ships only the interfaces, `WorkflowContext<TAggregate>`, and `WorkflowErrorCodes`. The first executable engine tests land in Batch 2 in `src/Infrastructure/Finance.Infrastructure.Tests` alongside `WorkflowEngine<TAggregate>`.

## 7. Resolved Decisions & Deferred Items

### Resolved (Batch 1)
- **Assembly split:** interfaces + `WorkflowContext` + `WorkflowErrorCodes` ship in `Finance.Common/Workflow` (+ `Finance.Common/ErrorCodes`); the concrete `WorkflowEngine<TAggregate>` and `AddWorkflowEngine<TAggregate>()` are deferred to Batch 2 in `Finance.Infrastructure.Services` (see §1 assembly-split decision).
- **`CONCURRENT_MODIFICATION` ownership:** single source in `CommonErrorCodes`; `WorkflowErrorCodes` references it, never redefines it.
- **State-hook return type:** `OnEnterAsync` / `OnExitAsync` return `Task` (see §2.1 decision).

### Resolved (Batch 2)
- **Engine location:** `WorkflowEngine<TAggregate>` + `AddWorkflowEngine<TAggregate>()` ship in `src/Infrastructure/Services/Finance.Infrastructure.Services/`.
- **State registry:** per-aggregate keyed registration (keyed DI / dictionary by `StateName`); duplicate `StateName` for one aggregate fails at startup; a missing state at transition time returns `STATE_NOT_REGISTERED` (§1, §2.2).
- **v1 persistence ownership:** the engine validates + runs hooks only; the calling service owns `SaveChanges` / `RowVersion` increment / status-history append, and (via `SaveWithConcurrencyCheckAsync`, SDD-INFRA-009) the `DbUpdateConcurrencyException` → `CONCURRENT_MODIFICATION` translation (§2.2–2.4).
- **Test environment:** engine transition/guard/registry tests are `[Unit]` in `src/Infrastructure/Finance.Infrastructure.Tests` and run without Docker; caller-side DB assertions are `[Category("Integration")]` (§6).

### Deferred
- Frontend exposure: `GET /api/v1/<aggregate>/{id}` including `availableTransitions: [...]` so React can disable buttons — adopt in the relevant frontend phase.
- Workflow visualization tooling — defer.
