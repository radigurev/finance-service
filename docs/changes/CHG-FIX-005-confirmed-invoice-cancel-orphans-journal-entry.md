# CHG-FIX-005 — Cancelling a `Confirmed` invoice orphans its already-posted journal entry

> Created: 2026-08-05
> Author: adversarial spec review (Payments batch — round 2)
> Status: Proposed
> Related specs: SDD-INV-001 (Invoice Lifecycle — authoritative, §2.1/§2.5/§2.6/§2.9/§2.11), SDD-FIN-002 (Journal Entry Lifecycle — holds the orphaned entry), SDD-FIN-003 (General Ledger / Trial Balance — overstated), SDD-FIN-006 (Posting Engine — posts unconditionally), SDD-AUDIT-001 (correct-by-reversal, immutability), SDD-PAY-001 (Payment Recording Lifecycle — the precedent fix: cancel from `Draft` only), SDD-PAY-002 / SDD-PAY-003 (sub-ledger vs control-account divergence), SDD-INT-WH-001 (Warehouse-originated drafts enter the same path)
> Originating ticket: adversarial review of the SDD-PAY-001/-002/-003 batch — round-2 finding V3 (the identical latent hole in SDD-INV-001) recorded per V11

---

## 1. Summary

SDD-INV-001 allows `Confirmed → Cancelled`. But `InvoiceConfirmedEvent` is already in flight the instant confirm commits, and the Journal-side `InvoiceConfirmedEventConsumer` posts the journal entry **unconditionally** — it never re-reads the invoice's current status. Cancelling inside that window leaves a **`Posted` journal entry in the general ledger with no supporting document**: the invoice is `Cancelled`, which is terminal (`AllowedNextStates = {}`); the `InvoicePostedEvent` back-event then fails with `INVOICE_NOT_CONFIRMED` and dead-letters, so the invoice can never reach `Posted`; `Reversed` is reachable only from `Posted`, and `IInvoiceService` exposes no reverse operation at all; `PUT`/`DELETE` return `INVOICE_POSTED_IMMUTABLE`; and nothing anywhere in `src/**` consumes `InvoiceCancelledEvent`, so the ledger is never compensated. **There is no in-service correction path.**

This is exactly the hole SDD-PAY-001 closed for payments in this batch by removing `Confirmed → Cancelled` (round-2 finding V3). The same fix is recommended for invoices — but SDD-INV-001's lifecycle is **shipped and tested**, so changing it needs its own batch.

This change spec records the defect only. **It changes no code and no system spec.**

## 2. Evidence — verified against shipped code

| # | File : line | What the code does |
|---|---|---|
| 1 | `src/Interfaces/Invoices/Finance.Invoices.API/Workflow/ConfirmedInvoiceState.cs:19-24` | `AllowedNextStates = { Posted, Cancelled }` — the engine permits `Confirmed → Cancelled`. |
| 2 | `src/Interfaces/Invoices/Finance.Invoices.API/Services/InvoiceService.cs:292-295` | `CancelAsync` accepts `Draft` **or** `Confirmed`: `if (invoice.Status is not (InvoiceStatus.Draft or InvoiceStatus.Confirmed)) return … INVALID_INVOICE_STATE_TRANSITION;`. |
| 3 | `…/InvoiceService.cs:537-580` (`ConfirmInTransactionAsync`) | Confirm allocates the gapless document number (553), stamps `Confirmed` (554), writes the audit row (557), **publishes `InvoiceConfirmedEvent` to the outbox (570)**, then commits (578). The event is in flight the moment confirm returns 200. |
| 4 | `src/Interfaces/Journal/Finance.Journal.API/Consumers/InvoiceConfirmedEventConsumer.cs:56-100` | The consumer builds `ApplyPostingRuleRequest` with `PostImmediately = true` (99) and `EntryDate = message.IssueDate` (97) and calls `IPostingEngine.ApplyAsync` (69-70). **It performs no status re-read of the invoice** — there is no callback, no query, no conditional. On failure it throws (78-80) so MassTransit retries/dead-letters. |
| 5 | `src/Interfaces/Invoices/Finance.Invoices.API/Workflow/CancelledInvoiceState.cs:18` | `AllowedNextStates` is an **empty** set — `Cancelled` is terminal. |
| 6 | `…/InvoiceService.cs:318-326` (`LinkPostedJournalEntryAsync`) | Returns `Success` only when already `Posted` (318-321); otherwise requires `Confirmed` and returns `INVOICE_NOT_CONFIRMED` (323-326). A `Cancelled` invoice therefore **fails** the link. `JournalEntryId` is assigned only on the success path (328), so a cancelled invoice keeps `JournalEntryId = null`. |
| 7 | `src/Interfaces/Invoices/Finance.Invoices.API/Consumers/InvoicePostedEventConsumer.cs:57-65` | On that failure the consumer **throws** → the back-event retries and then dead-letters. The posted journal entry is never linked to any document. |
| 8 | `…/InvoiceService.cs:187-190` + `docs/domain/SDD-INV-001-invoice-lifecycle.md:101` (§2.6) | `DELETE` against a non-`Draft` invoice returns `INVOICE_POSTED_IMMUTABLE`; §2.6 mandates the same for `PUT`. No edit path back. |
| 9 | `src/Interfaces/Invoices/Finance.Invoices.API/**` (grep) | `IInvoiceService` exposes **no** `ReverseAsync`. The only `Reversed` occurrences are `ReversedInvoiceState.cs`, its registration (`Program.cs:129`), `PostedInvoiceState.cs:19`, and doc comments. Combined with `PostedInvoiceState.cs:19` (`Posted → { Reversed }`), `Reversed` is unreachable for an invoice that never reached `Posted`. |
| 10 | `…/InvoiceService.cs:615` (publish) + `:789` (`BuildCancelledEvent`) + `src/Finance.ServiceModel/Events/Invoices/InvoiceCancelledEvent.cs:11` | `InvoiceCancelledEvent` **is published** on cancel. A grep for `InvoiceCancelledEvent` across `src/**` returns only the record definition, this publish, the `BuildCancelledEvent` factory, one unit test (`InvoiceServiceTests.cs:834`) and the test harness stub (`InvoiceServiceTestHarness.cs:126-127`). **There is no consumer anywhere** — in particular none in `Finance.Journal.API/Consumers/`, which contains exactly one file (`InvoiceConfirmedEventConsumer.cs`). The GL is never compensated. |
| 11 | `src/Interfaces/Invoices/Finance.Invoices.API.Tests/Unit/Services/InvoiceServiceTests.cs:424` and `docs/domain/SDD-INV-001-invoice-lifecycle.md:335` (§6.1) | `Cancel_ConfirmedInvoice_TransitionsToCancelled_KeepsDocumentNumber` — the transition is **shipped, specified, and asserted green**. This is what makes it a batch-sized change, not a patch. |
| 12 | `src/Country/Finance.Country.BG/BulgariaStrategy.cs:84-89` | `BuildDefaultRules()` ships exactly `SALE_INVOICE` (93), `PURCHASE_INVOICE` (106), `CUSTOMER_PAYMENT` (119) — **no `CREDIT_NOTE`, no `DEBIT_NOTE`**. Combined with evidence #4 (which resolves `CREDIT_NOTE`/`DEBIT_NOTE` at lines 113-114), a confirmed Credit/Debit Note **cannot post today** and its only exit is the very cancel this change would remove. That makes the fix order-dependent — see §10. |

**Note on spec-vs-code alignment:** the code faithfully implements SDD-INV-001 §2.1 (`docs/domain/SDD-INV-001-invoice-lifecycle.md:56`) and §2.6 (`:105-111`). This is **not** implementation drift — the specification itself contains the hole. SDD-INV-001 §2.6 anticipates orphaning only in the *settlement* direction (`INVOICE_HAS_SETTLEMENTS`, `:108-109`); it never considers the *ledger* direction.

## 3. Failing scenario (dated, Bulgaria / BGN, `SALE_INVOICE` = Dr 411 / Cr 701 + Cr 4532)

1. **2026-08-03 09:14:02** — Operator confirms a sale-invoice draft. `ConfirmInTransactionAsync` allocates `INV-2026-000731`, stamps `Confirmed`, audits, enqueues `InvoiceConfirmedEvent` (`NetTotal` 1000.00, `TaxTotal` 200.00, `GrossTotal` 1200.00, `IssueDate` 2026-08-03), commits. **200 OK.**
2. **09:14:03** — The outbox delivers the event. `InvoiceConfirmedEventConsumer` resolves rule `SALE_INVOICE` and `ApplyAsync(PostImmediately = true)` posts `JE-2026-002914`: **Dr 411 1200.00 / Cr 701 1000.00 / Cr 4532 200.00**, `EntryDate = 2026-08-03`. It publishes `InvoicePostedEvent`.
3. **09:14:03 (same second)** — The operator spots the wrong counterparty and calls `POST /api/v1/invoices/{id}/cancel` with `Reason = "wrong counterparty"`. Status is still `Confirmed` → passes evidence #2; the engine permits `Confirmed → Cancelled` (evidence #1); the audit `StateChange` row is written; `InvoiceCancelledEvent` is published; commit. **200 OK.**
4. **09:14:04** — `InvoicePostedEventConsumer` calls `LinkPostedJournalEntryAsync`. The invoice is now `Cancelled` — neither `Posted` nor `Confirmed` — so it returns `INVOICE_NOT_CONFIRMED` (evidence #6); the consumer throws (evidence #7); MassTransit retries (1s/5s/15s) and dead-letters.
5. **End state, permanent:**
   - `JE-2026-002914` is `Posted` in the GL, carrying **1200.00 of receivable on 411** and **200.00 of output VAT on 4532**, pointing at no live document.
   - `INV-2026-000731` is `Cancelled` (terminal), `JournalEntryId = null`, and keeps its gapless number (never recycled — НАП).
   - No consumer exists for `InvoiceCancelledEvent` (evidence #10), so no reversing entry is ever posted.
   - The August trial balance (SDD-FIN-003) and the August VAT position are overstated by 1200.00 / 200.00.
   - The only remedies are outside the invoice's audit trail and outside an invoicing clerk's permissions: a hand-written journal entry (`finance.journal:create`) or a direct Journal-side reversal of `JE-2026-002914` (`finance.journal:reverse`) — and the latter is itself exposed to **`CHG-FIX-004`** (it would land back in whatever period the entry is dated).

**The window is not narrow.** The exposure is the entire interval between *confirm commits* and *the back-event being applied*, which is unbounded in practice: outbox delivery latency, the back-event's own retry ladder (1s/5s/15s), a dead-lettered back-event being replayed, an Invoices-service restart, RabbitMQ backpressure, or a Redis `SETNX` idempotency hiccup. Worse, in the exact case where the Journal **cannot** post (evidence #12 — a confirmed `CreditNote`/`DebitNote` has no seeded rule, so `ApplyAsync` fails and the consumer dead-letters), the invoice sits in `Confirmed` **indefinitely** and cancel stays available the whole time — which is also why operators currently rely on it (§10).

## 4. Blast radius

- **SDD-INV-001** §2.1 (`:56`), §2.5 (posting handshake), §2.6 (`:105-111`), §2.9 (immutability), §2.11 (events), §6.1 (`:335`).
- **SDD-FIN-002 / SDD-FIN-003** — an orphaned `Posted` entry sits in the ledger and the trial balance forever; nothing in the Journal service knows the source document was voided.
- **SDD-FIN-006** — the posting engine is rule-driven and stateless with respect to the source document (evidence #4); it has, by design, no notion of "the invoice was cancelled after I was told to post". The defect cannot be fixed inside the engine.
- **SDD-AUDIT-001** — each service's audit trail is internally consistent while the two tell **contradictory stories** (invoice cancelled with a reason; journal entry posted and never reversed). No correlating reversal row exists for a reviewer to find, so the divergence is invisible to audit review and detectable only by reconciling 411 against the AR sub-ledger.
- **SDD-PAY-002 / SDD-PAY-003 (Drafted)** — the payments projection mirrors `InvoiceStatus`, and a `Cancelled` invoice is correctly excluded from allocation and from every aging bucket. So the **sub-ledger is right and the general ledger is wrong**: control account 411 diverges from the AR sub-ledger by the gross amount, permanently, with no document to reconcile against. That is precisely the divergence SDD-PAY-003's control-account reconciliation is meant to surface — it would report an unexplained residual with no drill-down.
- **Regulatory (BG)** — output VAT on 4532 is included in the period's VAT position while the document is void; a filed return overstates output VAT and the correction is manual.
- **SDD-INT-WH-001** — Warehouse-originated invoices are created as ordinary drafts by the inbound consumers, so they enter this path identically once confirmed.

## 5. Precedent already decided in this batch (SDD-PAY-001)

SDD-PAY-001 hit the identical structural hole and closed it by **removing the transition**, not by compensating it. Verified in `docs/domain/SDD-PAY-001-payment-recording-lifecycle.md`:

- `:37` — *"Cancel (from `Draft` only) and reverse (from `Posted` only, with its own period pre-check)…"*
- `:125` (§2.6) — *"`Confirmed → Cancelled` is deliberately NOT in `AllowedNextStates` (§2.1): the posting is already in flight, so a `Confirmed` payment is completed to `Posted` and then corrected by reversal (§2.7); a posted payment is reversed, never cancelled."*
- `:331` (§4) — `INVALID_PAYMENT_STATE_TRANSITION` (409) explicitly lists cancelling a `Confirmed` payment as unreachable-by-design.
- PAY-001 also records the residual **operational** case (a payment stuck in `Confirmed` because the Journal never posted) as resolved by fixing the cause and retrying the idempotent `POST /{id}/post` — no GL entry exists to orphan, so it is an operational concern, not a data-integrity one.

**Recommendation: apply the same rule to invoices.** Cancel from `Draft` only; a wrongly-issued *confirmed* invoice is completed to `Posted` and corrected by a Credit Note (SDD-INV-001 §2.7) — the SDD-AUDIT-001 principle already applied to posted journal entries and posted invoices.

## 6. Scope

### In scope (for the future fix this spec proposes)
- Remove `Confirmed → Cancelled` from the invoice state machine, from `CancelAsync`, from the spec's §2.1/§2.6/§4/§6 surface, and from the frontend.
- State the accounting rationale and the replacement operator procedure (post, then Credit Note).
- Document the residual operational case (`Confirmed` and un-postable) and its idempotent retry path.

### Out of scope (explicit)
- `Draft → Cancelled` — unchanged, including the mandatory `Reason`, the audit `StateChange` row, `InvoiceCancelledEvent`, and the best-effort `INVOICE_HAS_SETTLEMENTS` guard (SDD-INV-001 §2.6 `:108-109`).
- The `InvoiceCancelledEvent` contract and SDD-PAY-002's consumer of it (its orphaned-settlement detection stays exactly as specified).
- The gapless-numbering rule — a cancelled confirmed invoice keeps its number; nothing here recycles numbers.
- SDD-INV-001 §2.7's deferred full-offset `Posted → Reversed` detection.
- The `CREDIT_NOTE`/`DEBIT_NOTE` posting-rule templates themselves — owned by SDD-PAY-001 §2.13 in the current batch. This change **depends on** them (§10) but does not define them.
- `CHG-FIX-004` (reversal bypasses the period guard) — an independent defect on the Journal side; neither fix subsumes the other.

## 7. Proposed behavior (testable rules — for the batch that fixes this)

1. Cancel MUST be legal from `Draft` **only**. `Confirmed → Cancelled` MUST be removed from `ConfirmedInvoiceState.AllowedNextStates` and from `CancelAsync`'s accepted status set.
2. `POST /api/v1/invoices/{id}/cancel` against a `Confirmed` invoice MUST return `INVALID_INVOICE_STATE_TRANSITION` (409) **before** any audit row, any status-history row, and any outbox message — the request MUST have zero persisted effect.
3. The rationale MUST be stated in SDD-INV-001 §2.6: once an invoice is confirmed its posting is irrevocably in flight; a document whose journal entry may already be in the ledger is corrected by **reversal / credit note**, never by cancellation.
4. The replacement operator procedure MUST be specified: complete the invoice to `Posted` (`POST /{id}/post`), then issue a **Credit Note** per §2.7. The original keeps its gapless number and its ledger entry; the correction is a separate numbered document with its own offsetting entry.
5. The residual operational case MUST be documented: an invoice stuck in `Confirmed` because the Journal never posted (missing rule, DLQ, outage) MUST be resolved by fixing the cause and retrying `POST /api/v1/invoices/{id}/post`. That handshake is already idempotent — `PostAsync` returns `Success` for an already-`Posted` invoice (`InvoiceService.cs:248-251`) and `LinkPostedJournalEntryAsync` does the same (`:318-321`) — and while posting has failed **no GL entry exists to orphan**, so this is an operational concern, not a data-integrity one.
6. `Draft → Cancelled` behavior MUST be bit-for-bit unchanged (reason requirement, audit row, event, `INVOICE_HAS_SETTLEMENTS` guard).
7. The shipped spec/test pair MUST be updated together: `Cancel_ConfirmedInvoice_TransitionsToCancelled_KeepsDocumentNumber` (`InvoiceServiceTests.cs:424`, SDD-INV-001 §6.1 `:335`) MUST become `Cancel_ConfirmedInvoice_ReturnsInvalidInvoiceStateTransition`, and a companion MUST assert *nothing was written* (no audit row, no event, no status-history row).
8. The frontend MUST NOT offer a Cancel action on a `Confirmed` invoice row or detail view; the corresponding guidance copy MUST exist in **both** `en.ts` and `bg.ts` in the same PR (SDD-UI-001, SDD-UI-FIN-001).

**Rejected alternatives (recorded so the fixing batch does not re-litigate).**
- *Keep the transition and add a Journal-side `InvoiceCancelledEvent` consumer that reverses the linked entry.* Rejected: (a) in exactly the window that matters the invoice has **no** `JournalEntryId` (it is assigned only by `LinkPostedJournalEntryAsync`, `:328`), so the consumer cannot identify the entry without a new Journal-side lookup-by-source-document that does not exist; (b) it makes *cancellation* an accounting event that mutates the ledger, contradicting SDD-AUDIT-001's correct-by-reversal model where a reversal is itself a posted document; (c) the compensating reversal inherits `CHG-FIX-004` and would post into whatever period the original entry was dated. Removing the window beats compensating it.
- *Make `Cancelled` non-terminal (`Cancelled → Reversed`).* Rejected: it contradicts SDD-INV-001 §2.1's terminal-state model (`:58`) and would mean a cancelled document produced ledger movement.
- *Have the confirmed-event consumer re-read the invoice status before posting.* Rejected: it only narrows the race (the invoice can be cancelled after the read and before the post commits), it adds a synchronous cross-service read the plan forbids, and it makes the posting engine document-state-aware (SDD-FIN-006).

## 8. Affected specs / code

| Spec / file | Change the fixing batch would make |
|---|---|
| `SDD-INV-001` §2.1 (`:56`) | `Confirmed` → { `Posted` } only. |
| `SDD-INV-001` §2.6 (`:105-111`) | Cancel from `Draft` only; add the rationale and the post-then-credit-note procedure; keep `INVOICE_HAS_SETTLEMENTS` on `Draft → Cancelled`. |
| `SDD-INV-001` §2.13 | New edge case: cancel attempted on a `Confirmed` invoice → rejected, nothing written. |
| `SDD-INV-001` §4 | `INVALID_INVOICE_STATE_TRANSITION` reachability note for cancelling a `Confirmed` invoice. |
| `SDD-INV-001` §5 | New version entry — **BREAKING** (a shipped 200 becomes a 409). |
| `SDD-INV-001` §6.1 (`:335`) | Replace the cancel-from-confirmed test with its rejection counterpart + a nothing-written assertion. |
| `SDD-UI-FIN-001` | Remove the Cancel affordance for `Confirmed` rows; add the guidance copy. |
| `docs/cross-reference-map.md` | Updated SDD-INV-001 test list. |
| `src/Interfaces/Invoices/Finance.Invoices.API/Workflow/ConfirmedInvoiceState.cs:19-24` | Drop `Cancelled` from `AllowedNextStates`. |
| `src/Interfaces/Invoices/Finance.Invoices.API/Services/InvoiceService.cs:292-295` | Accept `Draft` only. |
| `frontend/src/**` + `frontend/src/shared/i18n/locales/{en,bg}.ts` | Hide the action; add EN + BG copy in the same PR. |

No database change. No new error code (`INVALID_INVOICE_STATE_TRANSITION` already exists and is already mapped to 409). No event-contract change — `InvoiceCancelledEvent` keeps its shape and simply can no longer be emitted for a confirmed invoice.

## 9. Testing (to add with the fix)

- `Cancel_ConfirmedInvoice_ReturnsInvalidInvoiceStateTransition` — [Unit] (replaces `InvoiceServiceTests.cs:424`)
- `Cancel_ConfirmedInvoice_WritesNoAuditRow_NoStatusHistory_NoEvent` — [Unit]
- `Cancel_DraftInvoice_StillTransitionsToCancelled_KeepsExistingBehavior` — [Unit] (regression)
- `Cancel_DraftInvoiceWithSettlements_StillReturnsInvoiceHasSettlements` — [Unit] (regression on the surviving path)
- `ConfirmedInvoiceState_AllowedNextStates_ContainsPostedOnly` — [Unit]
- `Post_ConfirmedInvoiceAfterFailedPosting_IsIdempotent_AndReachesPosted` — [Unit] (pins the §7 rule 5 escape hatch)
- `Integration_CancelConfirmedInvoice_Returns409InvalidInvoiceStateTransition` — [Integration]
- `Integration_CancelDraftInvoice_Returns200` — [Integration] (regression)
- `Integration_ConfirmThenPostThenCreditNote_LeavesLedgerNet` — [Integration] (proves the replacement correction path exists end-to-end; requires the §10 dependency)
- UI (Chrome DevTools MCP): Cancel action absent on a `Confirmed` invoice row and detail view; present on `Draft`; guidance copy renders in EN and BG with no raw key paths.

## 10. Ordering dependency — this fix MUST NOT ship first

`BulgariaStrategy.BuildDefaultRules()` ships only `SALE_INVOICE`, `PURCHASE_INVOICE`, `CUSTOMER_PAYMENT` (evidence #12), while `InvoiceConfirmedEventConsumer` resolves `CREDIT_NOTE`/`DEBIT_NOTE` for note documents (`:113-114`). A confirmed Credit or Debit Note therefore **cannot post today** — `ApplyAsync` fails, the consumer throws and dead-letters, and the note is stranded in `Confirmed`. Cancel is currently its **only** exit.

Removing `Confirmed → Cancelled` before the `CREDIT_NOTE`/`DEBIT_NOTE` templates are seeded would leave such a note permanently stuck in `Confirmed` with no exit at all — strictly worse than the defect being fixed. SDD-PAY-001 §2.13 specifies those two templates (with asserted debit/credit sides and an accountant sign-off gate) as a deliverable of the current batch.

**Required order:** (1) `CREDIT_NOTE`/`DEBIT_NOTE` templates seeded and signed off (SDD-PAY-001 §2.13) → (2) this change. Additionally, SDD-INV-001 §2.7's credit-note correction path must be operable, since §7 rule 4 makes it the replacement procedure.

## 11. Risks (of the fix, not of the defect)

- **Breaking change to a shipped, tested, specified lifecycle.** `POST /api/v1/invoices/{id}/cancel` returns 409 where it returned 200 for `Confirmed` invoices. It needs an SDD-INV-001 version entry classified breaking, a frontend change, EN + BG copy, and an operator announcement.
- **Operator cost.** Voiding a wrongly-issued confirmed invoice becomes post-then-credit-note: **two** numbered documents where one cancel sufficed, and the erroneous invoice stays visible in the books. That is accounting-correct (and what НАП expects of an issued document) but it changes daily workflow and needs accountant sign-off.
- **Stranded-`Confirmed` risk if the ordering in §10 is violated** — see §10. This is the single largest risk and is entirely mitigated by sequencing.
- **Warehouse-originated documents** (SDD-INT-WH-001) become harder to discard once confirmed; the inbound flow itself is unaffected (drafts stay cancellable), but operator training must cover it.
- **Test churn** across `InvoiceServiceTests` (68 shipped unit tests), `InvoiceEndpointIntegrationTests`, and the UI suite.
- **Residual after the fix:** the *window* disappears, but a `Confirmed` invoice whose posting genuinely fails still needs operational attention (DLQ monitoring, SDD-OBS-001). §7 rule 5 makes that explicit rather than papering over it with a cancel.

## 12. Why this is NOT fixed in the current batch

- **It changes a shipped, `Implemented`, explicitly-tested lifecycle.** SDD-INV-001 is `Implemented` (Batch 16, 68 unit tests green) and §6.1 `:335` asserts the exact transition being removed. Per doc governance, retiring specified behavior requires its own change spec, its own version entry classified breaking, and its own validation pass — not an in-flight edit inside a batch whose stated deliverable is Payments.
- **The current batch owns SDD-PAY-001/-002/-003 only.** Round-2 finding V3 fixed the payments lifecycle (which is still `Drafted`, so removing the transition there costs nothing) and explicitly ruled INV-001's identical hole out of scope: *"Do NOT change INV-001's shipped, tested lifecycle here — record it as a change spec."*
- **It is blocked by an ordering dependency the current batch is still producing** — the `CREDIT_NOTE`/`DEBIT_NOTE` templates (§10). Shipping the removal before the templates would make things worse.
- **It is not a backend-only change.** It removes a user-facing action, so it needs the frontend phase plus EN/BG i18n and a UI validation pass — phases 6-7 of the pipeline, which a spec-only Payments batch does not run.
- **It needs accountant sign-off** on the replacement procedure (post-then-credit-note instead of cancel), which is an external approval, not an engineering decision.
- The defect is **pre-existing** (shipped in Batch 16) and is not worsened by anything in the Payments batch; PAY-001's own lifecycle is already correct, so nothing in the batch depends on this being fixed first.

## 13. Status

**Proposed.** No code, no system spec, and no test changed by this document. Recommended sequencing: immediately after the Payments batch ships the `CREDIT_NOTE`/`DEBIT_NOTE` templates (SDD-PAY-001 §2.13), and before any environment issues confirmed invoices at volume. Until then, mitigate operationally: monitor `InvoicePostedEvent` dead letters (SDD-OBS-001) as the detection signal for an orphaned entry, and instruct operators not to cancel a confirmed invoice.

## 14. Open questions

- Should cancelling a `Confirmed` invoice be replaced by an explicitly-permissioned **void** operation (`finance.invoice:void`) that atomically posts the invoice, issues an auto-generated fully-offsetting Credit Note, and marks the original `Reversed` — a single operator action with correct accounting underneath? It would preserve today's one-click UX and needs accountant sign-off on auto-generated notes.
- Should the Journal service gain a reverse-by-source-document lookup so that an orphaned entry from a *historical* cancellation can be reversed with the invoice's identity attached to the audit trail? (Remediation of data already created by this defect is not addressed by §7, which only prevents new occurrences.)
