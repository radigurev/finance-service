# CHG-FIX-004 — Journal reversal bypasses the posting-period guard (Posted entry insertable into a closed period)

> Created: 2026-08-05
> Author: adversarial spec review (Payments batch — round 2)
> Status: Proposed
> Related specs: SDD-FIN-002 (Journal Entry Lifecycle — authoritative for reversal, §2.6/§2.7), SDD-FIN-004 (Fiscal Period Management — the violated close invariant), SDD-FIN-003 (General Ledger / Trial Balance — consumer), SDD-INV-001 (Invoice Lifecycle — affected document type), SDD-PAY-001 (Payment Recording Lifecycle — partial mitigation, §2.7/§2.9/§3.2), SDD-AUDIT-001 (immutability / audit narrative)
> Originating ticket: adversarial review of the SDD-PAY-001/-002/-003 batch — round-2 finding V2 (deeper defect) recorded per V11

---

## 1. Summary

`POST /api/v1/journal-entries/{id}/reverse` posts a **new** journal entry that reuses the original entry's `EntryDate`, and it stamps that new entry `Posted` **directly** instead of transitioning it through `IWorkflowEngine<JournalEntry>`. The only place the fiscal-period guard is wired is `PostingPeriodWorkflowGuard`, which returns success immediately unless the requested `TargetState` is `Posted` — and the only transition the engine sees during a reversal is `Posted → Reversed` on the **original**. The guard is therefore inert on the entire reversal path: **any** reversal (manual journal entry, invoice-derived entry, and — once shipped — payment-derived entry) can insert a `Posted` entry into a **closed** fiscal period, silently changing a closed period's trial balance after close. Posting the identical ledger effect through `POST /{id}/post` is correctly rejected with `POSTING_PERIOD_CLOSED`; the same effect through `POST /{id}/reverse` is permitted.

This change spec records the defect only. **It changes no code and no system spec.**

## 2. Evidence — verified against shipped code

| # | File : line | What the code does |
|---|---|---|
| 1 | `src/Interfaces/Journal/Finance.Journal.API/Services/JournalEntryService.cs:203-233` | `ReverseAsync` guards exactly four things: non-empty `Reason` (210-213), entry exists (215-219), `Status == Posted` (221-224), decodable `RowVersion` (226-230). **There is no period lookup of any kind** before delegating to `ReverseInTransactionAsync` (232). |
| 2 | `…/JournalEntryService.cs:465-508` | `ReverseInTransactionAsync`: transitions the **original** `Posted → Reversed` via the workflow engine (475-476), builds the reversal (482), allocates a fresh gapless number (485), then calls `StampPosted(reversal, …)` (486). The reversal row **never** passes through `_workflow.TransitionAsync`. |
| 3 | `…/JournalEntryService.cs:576-594` (`BuildReversal`) | `EntryDate = original.EntryDate` (582) and `Status = JournalEntryStatus.Posted` (585). The reversing entry is dated into the **original's** period by construction. |
| 4 | `…/JournalEntryService.cs:551-557` (`StampPosted`) | Sets `EntryNumber`, `PostedAt`, `PostedBy`, `Status = Posted`. It consults no guard and is reachable from both `PostInTransactionAsync` (437, which *is* guarded upstream by the engine) and `ReverseInTransactionAsync` (486, which is not). |
| 5 | `src/Interfaces/Journal/Finance.Journal.API/Workflow/PostingPeriodWorkflowGuard.cs:34-37` | `if (!string.Equals(request.TargetState, nameof(JournalEntryStatus.Posted), …)) return ChainValidationResult.Success();` — the guard short-circuits for every target state other than `Posted`, so `EnsurePostableAsync` (40) is never invoked on `Posted → Reversed`. |
| 6 | `src/Interfaces/Journal/Finance.Journal.API/Workflow/PostedJournalEntryState.cs:19-20` | `AllowedNextStates = { Reversed }` — the reversal path requests `Reversed`, never `Posted`, confirming evidence #5 makes the guard structurally unreachable here. |
| 7 | `src/Interfaces/Journal/Finance.Journal.API/Workflow/GatewayPostingPeriodGuard.cs:34-71` | The **real** period guard: `Open` ⇒ success; `Closed`, `404 NO_PERIOD_FOR_DATE`, or any upstream/unreachable error ⇒ `POSTING_PERIOD_CLOSED` (fails closed). It is registered in production (`src/Interfaces/Journal/Finance.Journal.API/Program.cs:140`) and reachable **only** through `PostingPeriodWorkflowGuard` (`Program.cs:135`) — i.e. only on `Draft → Posted`. |
| 8 | `src/Interfaces/Journal/Finance.Journal.API/Controllers/JournalEntriesController.cs:160-161` | The hole is externally reachable: `[HttpPost("{id:guid}/reverse")]` with `[RequirePermission("finance.journal:reverse")]`. |
| 9 | `src/Interfaces/Journal/Finance.Journal.API/Consumers/InvoiceConfirmedEventConsumer.cs:87-100` | Invoice-derived entries are dated `EntryDate = message.IssueDate` (97) and posted immediately (99), so an invoice's GL entry is an ordinary reversible `JournalEntry` — the hole is not specific to manual entries. |
| 10 | `src/Interfaces/Journal/Finance.Journal.API.Tests/Unit/Services/JournalEntryServiceTests.cs:313, 335, 354, 376, 602, 626, 644, 667, 695, 730, 762, 792, 830, 853` | Fourteen shipped `Reverse_*` unit tests cover state, reason, sign-flip, audit, event, and numbering. **None** asserts a period check; no `Reverse_ClosedPeriod_*` test exists. |
| 11 | `src/Interfaces/Journal/Finance.Journal.API.Tests/Integration/JournalEndpointIntegrationTests.cs:254` | `Reverse_CreatesSignFlippedLinkedEntry_AndFlipsOriginalToReversed` — the shipped integration coverage likewise never exercises a closed period. |
| 12 | `docs/core/SDD-FIN-002-journal-entry-lifecycle.md:108-112` (§2.7) | The spec itself scopes the guard to the `Draft → Posted` transition only: *"The `Draft → Posted` transition MUST consult an `IPostingPeriodGuard`…"*. §2.6 (95-107) describes the reversal's seven ordered steps and never mentions a period check. **The code matches the spec; the spec has the hole.** This is spec-level under-specification, not implementation drift. |

## 3. Failing scenario (dated, Bulgaria / BGN, monthly periods)

1. **2026-03-15** — Sale invoice `INV-2026-000412` is confirmed. `InvoiceConfirmedEventConsumer` posts `JE-2026-001188` (Dr 411 / Cr 701 + Cr 4532) with `EntryDate = 2026-03-15`, inside the then-`Open` period `2026/03`.
2. **2026-04-10** — Period `2026/03` is closed via `POST /api/v1/periods/{id}/close` with a `Reason` (SDD-FIN-004 §2.4). The March trial balance (SDD-FIN-003) is extracted and the March VAT return is filed with НАП.
3. **2026-05-02** — The operator discovers the invoice named the wrong counterparty and calls
   `POST /api/v1/journal-entries/{JE-2026-001188}/reverse` with `Reason = "wrong counterparty"` and a valid `RowVersion`.
4. What happens: `ReverseAsync` passes all four guards (evidence #1). The engine transitions the original `Posted → Reversed`; `PostingPeriodWorkflowGuard` returns success without calling the Periods service (evidence #5/#6). `BuildReversal` copies `EntryDate = 2026-03-15` (evidence #3). `StampPosted` sets `Status = Posted` and burns a fresh gapless `JE` number (evidence #2/#4). Audit rows and `JournalEntryReversedEvent` commit. **HTTP 200.**
5. End state: a **`Posted` journal entry dated 2026-03-15 now exists in the closed `2026/03` period.** March's trial balance no longer matches the copy extracted on 2026-04-10; the filed VAT return no longer reconciles to the ledger; SDD-FIN-004's close invariant is broken with no error surfaced to anyone. The audit trail (SDD-AUDIT-001) records the reversal as a legitimate, reasoned operation — it contains nothing that flags the period breach.
6. **The asymmetry that proves it is a defect:** creating the *same* offsetting entry as a fresh draft on 2026-05-02 with `EntryDate = 2026-03-15` and calling `POST /{id}/post` is correctly rejected with `POSTING_PERIOD_CLOSED` (409). The identical ledger effect is blocked on one endpoint and permitted on the other.

## 4. Blast radius

- **Every document type whose GL effect is a `JournalEntry`.**
  - Manual journal entries — SDD-FIN-002 §2.6, directly.
  - Invoice-derived entries — SDD-INV-001. The invoice aggregate exposes no reverse endpoint (`IInvoiceService` has no `ReverseAsync`; grep over `src/Interfaces/Invoices/Finance.Invoices.API` finds `Reversed` only in `ReversedInvoiceState`, `PostedInvoiceState.cs:19`, `Program.cs:129`, and doc comments), but its posted entry is reversible **directly** through the Journal endpoint, which is exactly the scenario in §3.
  - Payment-derived entries — SDD-PAY-001 §2.7 routes its `PaymentReversedEvent` consumer through the shipped `IJournalEntryService.ReverseAsync`, inheriting the hole.
- **SDD-FIN-003** — trial balance and GL for a closed period become non-reproducible after close.
- **SDD-FIN-004** — the close invariant ("no posting into a closed period") is enforced on one path and not the other, so `Closed` does not mean what the spec says it means.
- **SDD-AUDIT-001** — the audit trail stays internally consistent while describing an operation that should have been impossible; there is no marker a reviewer could search for.
- **Regulatory** — for Bulgaria, output/input VAT on a reversed line moves the period's VAT position after the return is filed; the amendment path is manual and outside the system's audit narrative.

## 5. Partial mitigation already in flight (and what it does NOT cover)

SDD-PAY-001 (Drafted, this batch) adds an **endpoint-level** period pre-check on `POST /api/v1/payments/{id}/reverse`, evaluated over the **linked journal entry's** `EntryDate`, surfacing `PAYMENT_PERIOD_CLOSED` (409) through the gateway-backed period guard — see `docs/domain/SDD-PAY-001-payment-recording-lifecycle.md` §2.7 / §2.9 / §3.2 (guard table row *"Posted → Reversed (operator endpoint)"*), §2.18 edge case *"Reversing a payment whose original period has closed"*, and the named test `Reverse_ClosedPeriodOnLinkedEntryDate_ReturnsPaymentPeriodClosed_NoTransitionNoEvent` (§6.5). PAY-001 also states explicitly that v1 does **not** re-date the reversing entry into the current open period.

**The generic hole remains.** The PAY-001 pre-check:
- guards only the **payment-initiated** route, not `POST /api/v1/journal-entries/{id}/reverse`;
- is bypassed entirely if an operator with `finance.journal:reverse` reverses the payment's linked entry directly on the Journal endpoint;
- does nothing for manual entries or invoice-derived entries;
- lives in a different service, so it cannot be reused — it is defence-in-depth at the wrong layer for a defect whose root cause is in `JournalEntryService`/`PostingPeriodWorkflowGuard`.

## 6. Scope

### In scope (for the future fix this spec proposes)
- Make the reversal path period-guarded, over the date the reversing entry will actually carry (`original.EntryDate`).
- Guarantee no partial effect on rejection: no transition, no sequence allocation, no audit row, no outbox message.
- Add the missing spec rules to SDD-FIN-002 §2.6/§2.7 and the missing edge case + tests.

### Out of scope (explicit)
- **Re-dating the reversing entry** into the current open period. v1 keeps `EntryDate = original.EntryDate` (evidence #3, SDD-FIN-002 §2.6 step 1), so a closed original period is a **hard block** requiring an SDD-FIN-004 §2.5 reopen. Re-dating changes the meaning of a reversal and needs accountant sign-off; it is a separate change.
- The sign-flip arithmetic, the `ReversesEntryId` linkage, the audit-row shape (`CHG-FIX-002`), the gapless numbering, and the `JournalEntryReversedEvent` contract — all unchanged.
- SDD-INFRA-003 sequence behavior. (Note the ordering requirement in §7 rule 2: the number must not be burned by a rejected reversal.)
- Any change to `POST /{id}/post`, which is already correctly guarded.
- Adding an invoice-level reverse endpoint (SDD-INV-001 §2.7's full-offset detection remains its own deferred item).

## 7. Proposed behavior (testable rules — for the batch that fixes this)

**Preferred approach — an explicit pre-check in the reversal path (guard the row that is actually inserted).**

1. `ReverseAsync` MUST call `IPostingPeriodGuard.EnsurePostableAsync(original.EntryDate, ct)` and MUST fail with `POSTING_PERIOD_CLOSED` (409) when it does not succeed.
2. The check MUST run **before** the `Posted → Reversed` transition, **before** the sequence allocation, **before** any audit row, and **before** the outbox enqueue — a rejected reversal MUST leave the original `Posted`, MUST NOT consume a `JE` number (gaplessness, SDD-INFRA-003), MUST write no audit row, and MUST publish no event.
3. The evaluated date MUST be `original.EntryDate` — the date `BuildReversal` copies onto the reversing entry — and MUST NOT be the request/clock time.
4. The reversing entry MUST keep `EntryDate = original.EntryDate`; the fix MUST NOT re-date it. A closed original period MUST therefore be resolvable only by reopening the period (SDD-FIN-004 §2.5) — consistent with SDD-PAY-001 §2.7.
5. A reversal whose original `EntryDate` falls in an `Open` period MUST behave exactly as today (all fourteen shipped `Reverse_*` assertions MUST continue to hold).
6. The guard MUST fail **closed** on an unreachable or erroring Periods service, matching `GatewayPostingPeriodGuard` (evidence #7) and SDD-PAY-001 §2.9 — and this MUST be pinned by its own test, because it makes a correction path unavailable during a Periods outage.
7. Journal-side reversal **consumers** (`PaymentReversedEvent`; any future invoice reversal) MUST treat an already-`Reversed` linked entry as a success no-op before this guard can dead-letter a replay (the prerequisite SDD-PAY-001 §2.7 records as round-2 finding V10).
8. SDD-FIN-002 §2.7 MUST be reworded: the period guard applies to **every transition that results in a `Posted` row**, not only `Draft → Posted`.

**Rejected alternatives (recorded so the fixing batch does not re-litigate).**
- *Make `PostingPeriodWorkflowGuard` also fire on `→ Reversed`.* Rejected as the primary fix: the reversing **entry** never goes through the engine at all (evidence #2/#4), so this guards the original's state flag rather than the row being inserted, and leaves `StampPosted` an unguarded write path for any future caller. It MAY be added as cheap defence-in-depth on top of rule 1.
- *Route the reversing entry through the engine as `Draft → Posted`.* Rejected for v1: it contradicts SDD-FIN-002 §2.6 step 2 (`Status = Posted` by construction, never passing through `Draft`), requires a transient draft row, and rewrites shipped tests for no accounting gain.
- *Publish a warning and allow the reversal.* Rejected: a closed period that can still move is not closed.

## 8. Affected specs / code

| Spec / file | Change the fixing batch would make |
|---|---|
| `SDD-FIN-002` §2.6 | Add the period pre-check as an ordered step **0** of reversal, with the no-partial-effect guarantee. |
| `SDD-FIN-002` §2.7 | Broaden the seam's contract from "`Draft → Posted`" to "every transition producing a `Posted` row"; state the fail-closed consequence for the correction path. |
| `SDD-FIN-002` §2.12 / §4 | New edge case (reverse into a closed period) and `POSTING_PERIOD_CLOSED` reachability note for reverse. |
| `SDD-FIN-002` §5 | New version entry — **BREAKING** (a 200 becomes a 409 on a shipped endpoint). |
| `SDD-FIN-004` §2.7 | Note that the fulfilled guard now also covers reversal, so `Closed` blocks all `Posted` inserts. |
| `SDD-INV-001` §2.7 | Cross-reference: an invoice's posted entry cannot be reversed into a closed period. |
| `SDD-PAY-001` §2.7 | Note its endpoint-level pre-check becomes defence-in-depth once the generic guard lands (no behavior change, no code change). |
| `docs/cross-reference-map.md` | New test names on the SDD-FIN-002 row. |
| `src/Interfaces/Journal/Finance.Journal.API/Services/JournalEntryService.cs` | `ReverseAsync` pre-check (inject `IPostingPeriodGuard`). |
| `src/Interfaces/Journal/Finance.Journal.API/Workflow/PostingPeriodWorkflowGuard.cs` | Optional additional `→ Reversed` branch (defence-in-depth only). |

No database change. No new error code (`POSTING_PERIOD_CLOSED` already exists in `JournalErrorCodes` and is already mapped). No event-contract change. Frontend: the existing `errors.POSTING_PERIOD_CLOSED` keys already cover the new 409, but the invoice/journal reversal UI copy SHOULD gain the "reopen the period first" guidance in both `en.ts` and `bg.ts` in the same PR.

## 9. Testing (to add with the fix)

- `Reverse_OriginalEntryDateInClosedPeriod_ReturnsPostingPeriodClosed` — [Unit]
- `Reverse_ClosedPeriod_LeavesOriginalPosted_NoTransition` — [Unit]
- `Reverse_ClosedPeriod_AllocatesNoSequenceNumber` — [Unit] (gaplessness)
- `Reverse_ClosedPeriod_WritesNoAuditRow_AndEnqueuesNoEvent` — [Unit]
- `Reverse_EvaluatesPeriodGuardOverOriginalEntryDate_NotRequestDate` — [Unit]
- `Reverse_NoPeriodForOriginalEntryDate_ReturnsPostingPeriodClosed` — [Unit]
- `Reverse_PeriodsServiceUnreachable_FailsClosed_ReturnsPostingPeriodClosed` — [Unit]
- `Reverse_OpenPeriod_StillPostsSignFlippedEntry_WithOriginalEntryDate` — [Unit] (regression over the fourteen shipped assertions)
- `Integration_ReverseIntoClosedPeriod_Returns409PostingPeriodClosed` — [Integration]
- `Integration_ReverseAfterPeriodReopened_Returns200` — [Integration]
- `Integration_ReverseInvoiceDerivedEntryInClosedPeriod_Returns409` — [Integration] (the §3 scenario end-to-end)

## 10. Risks (of the fix, not of the defect)

- **Breaking behavior change on a shipped endpoint.** Reversals of entries in closed periods start returning 409 where they returned 200. Any runbook or script that corrects historical entries must reopen the period first. This needs an announcement and a version entry on SDD-FIN-002, not a silent patch.
- **Availability coupling on the correction path.** `GatewayPostingPeriodGuard` fails closed (evidence #7, lines 61-70), so a Periods/Gateway outage would block **all** reversals, including same-period ones. Reversal is used under time pressure (a wrong-customer invoice found at month-end), so fail-closed hurts more here than on posting. It matches the shipped convention and SDD-PAY-001 §2.9, so accept it — but pin it with a test and document it in the runbook.
- **Dead-letter amplification.** Journal-side reversal consumers would start failing when the period is closed; without the already-`Reversed` no-op (rule 7) a DLQ replay after the 7-day Redis idempotency TTL self-renews the dead letter.
- **Test-suite churn.** Fourteen `Reverse_*` unit tests plus one integration test construct `JournalEntryService`; adding a constructor dependency touches every harness. Mechanical, but it is the reason this is not a one-line change.
- **Residual after the fix:** an entry dated inside a period that is later closed **and then reopened** can still be reversed — correct by design, but it means "closed" is only as strong as the reopen policy (SDD-FIN-004 §2.5 out-of-order reopen guard).

## 11. Why this is NOT fixed in the current batch

- **Wrong spec, wrong service, wrong suite.** The Payments batch owns SDD-PAY-001/-002/-003. The fix lands in `JournalEntryService.ReverseAsync` and the SDD-FIN-002 §2.6/§2.7 surface — a different bounded context with its own shipped, `Implemented` spec and its own 14-test reversal suite plus integration coverage.
- **It is a breaking change to `Implemented` behavior.** Doc governance requires a new SDD-FIN-002 version entry classified breaking, plus a rollout note, rather than an amendment smuggled into a batch whose stated deliverable is Payments.
- **The blast radius spans three specs outside the batch** (SDD-FIN-002, SDD-FIN-004, SDD-INV-001) and must be proven against a real closed period end-to-end. That is Testcontainers integration work; the current offline gate runs unit tests only.
- **SDD-PAY-001's own §2.7 pre-check is already authored and tested against this batch's acceptance criteria.** Landing the generic guard simultaneously would change that pre-check's meaning mid-authoring (from *the* guard to defence-in-depth). Sequencing the generic fix **after** PAY-001 ships keeps each spec's guarantee independently testable and keeps the payments batch reviewable.
- The defect is **pre-existing** (shipped in Batch 10, unchanged by this batch) and is not made worse by anything in the Payments batch — PAY-001 strictly narrows the exposure.

## 12. Status

**Proposed.** No code, no system spec, and no test changed by this document. Recommended sequencing: schedule immediately after the Payments batch, before any environment closes its first production period, and before SDD-INV-001 §2.7's offset-driven reversal is implemented.

## 13. Open questions

- Should a reversal of an entry in a closed period be allowed with a **new** `EntryDate` in the current open period (re-dated reversal) as a separate, explicitly-permissioned operation? That is standard practice in some jurisdictions and would remove the reopen requirement — it needs accountant sign-off and a distinct error/UX path, so it is deliberately excluded from §7.
- Should `StampPosted` be made private-by-contract (e.g. funnelled through a single guarded `PostRow` helper) so no future caller can insert a `Posted` row without a period check?
