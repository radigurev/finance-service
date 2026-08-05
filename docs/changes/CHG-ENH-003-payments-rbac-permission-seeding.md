# CHG-ENH-003 — Seed the eight new Payments RBAC permissions in auth-service

> Created: 2026-08-05
> Author: spec-writer (Batch 17 — Phase 5 Payments; discharges the SDD-PAY-003 §2.9 "MUST be recorded in the change spec, not only in code" obligation)
> Status: Proposed
> Related specs: SDD-INT-AUTH-001 (Shared JWT Authentication — owns `[RequirePermission]` and §2.4's deferred auto-registration), SDD-PAY-001 §2.17 (six `finance.payment:*` permissions), SDD-PAY-002 §2.13 (`finance.payment:allocate`), SDD-PAY-003 §2.9 (`finance.aging:read`), SDD-INFRA-002 (the gateway routes that expose these endpoints), SDD-AUDIT-001 (the grant/revoke of a finance permission is itself audit-worthy)
> Originating ticket: Batch-17 Phase-4 validation — "missing deployment record" (the only unrecorded obligation of the batch)

---

## 1. Summary

Batch 17 shipped fifteen new Payments endpoints guarded by **eight new permission strings** that exist nowhere but in the shipped `[RequirePermission(...)]` attributes. SDD-INT-AUTH-001 §2.4 (`:34-35`) states that permission auto-registration is **DEFERRED** — "permissions are seeded MANUALLY in auth-service. No Finance service performs startup permission registration today." Nothing in this repository can create them. Until an operator inserts all eight into auth-service (a separate repository) and grants them to roles, **every one of the fifteen endpoints returns `403 INSUFFICIENT_PERMISSIONS` (SDD-INT-AUTH-001 §4) for every caller, including the read-only aging and counterparty-balance reports.** This change spec is the deployment record that obligation requires; the seeding itself is an operator action outside this repo.

## 2. Motivation

The three PAY specs each state the manual-seeding obligation for their own slice (SDD-PAY-001 §2.17 and §7, SDD-PAY-002 §2.13 and §7, SDD-PAY-003 §2.9 and §7), and SDD-PAY-003 §2.9 escalates it: *"This obligation MUST be recorded in the change spec, not only in code."* No such record existed, so the batch shipped with its single hard external prerequisite documented only as three scattered in-spec asides — none of them enumerating the full set, and none of them the artefact a deploy actually reads.

The failure mode is silent and total, and it is easy to misdiagnose. A missing permission produces a `403`, not a `500`; the service is healthy, `/health/ready` is green, the migrations applied, the gateway routes resolve, and the logs show authenticated users being refused. On the frontend there is not even a specific message: no `errors.FORBIDDEN`/`403` key exists in either locale (verified — no match for `403`/`forbidden`/`FORBIDDEN` anywhere under `frontend/src/shared`), so any future payments UI would surface `getApiErrorMessage`'s `errors.GENERIC_ERROR` fallback (`frontend/src/shared/utils/getApiErrorMessage.ts:31`).

The other reason to write it down: the aging surface was deliberately given its **own** permission so a reporting/collections role can read the roll-ups without being granted individual payment records (SDD-PAY-003 §2.9, restated in `AgingController.cs:16-20`). That intent is only realized if whoever seeds the catalogue knows to keep the two permissions unaliased. A well-meaning "just grant `finance.payment:*`" shortcut destroys the separation the design paid for.

## 3. Scope

### In scope

- The authoritative enumeration of the eight permission strings and the endpoint each one gates, verified attribute-by-attribute against shipped code (§4.1).
- The role-grant recommendation that preserves the aging/record-level split (§4.2).
- The companion deployment prerequisites this batch carries — migrations and their ordering, and the posting-rule seeding gate (§6, §11).

### Out of scope (explicit)

- **Implementing auto-registration.** That is SDD-INT-AUTH-001 §2.4, still deferred pending a published auth-service registration-endpoint contract. This change spec does not promote it and does not design it.
- **Any change in this repository.** No code, no test, no migration, no spec MUST is added, changed, or removed. The `[RequirePermission]` attributes are already correct as shipped.
- Role definitions themselves, user-to-role assignment, and the auth-service seeding mechanism (SQL, admin UI, or seeder) — all owned by the auth-service repository.
- The pre-existing Finance permissions (`finance.account:*`, `finance.nomenclature:*`, `finance.period:*`, `finance.journal:*`, `finance.posting:apply`, `finance.invoice:*`, `finance.event:read`). They are subject to the same manual-seeding rule but predate this batch and are assumed already seeded.
- Widening the SDD-INT-AUTH-001 §2.2 action vocabulary — see §13.

## 4. Proposed Behavior

### 4.1 The eight permissions (MUST)

All eight **MUST** exist in the auth-service permission catalogue, spelled exactly as below (ordinal, case-sensitive — the attribute value is compared as a plain string), **before** the Payments service is reachable through `Finance.Gateway`. Every row was read off the shipped attribute; the file:line is the attribute site.

| Permission | HTTP + gateway route | Controller action | Attribute site |
|---|---|---|---|
| `finance.payment:read` | `GET /api/v1/payments` | `PaymentsController.List` | `PaymentsController.cs:39-40` |
| `finance.payment:read` | `GET /api/v1/payments/{id}` | `PaymentsController.Get` | `PaymentsController.cs:56-57` |
| `finance.payment:read` | `GET /api/v1/payments/{paymentId}/allocations` | `PaymentAllocationsController.List` | `PaymentAllocationsController.cs:52-53` |
| `finance.payment:read` | `GET /api/v1/open-items` | `OpenItemsController.List` | `OpenItemsController.cs:56-57` |
| `finance.payment:create` | `POST /api/v1/payments` | `PaymentsController.Create` | `PaymentsController.cs:70-71` |
| `finance.payment:create` | `PUT /api/v1/payments/{id}` | `PaymentsController.Update` | `PaymentsController.cs:96-97` |
| `finance.payment:create` | `DELETE /api/v1/payments/{id}` | `PaymentsController.Delete` | `PaymentsController.cs:116-117` |
| `finance.payment:confirm` | `POST /api/v1/payments/{id}/confirm` | `PaymentsController.Confirm` | `PaymentsController.cs:132-133` |
| `finance.payment:post` | `POST /api/v1/payments/{id}/post` | `PaymentsController.Post` | `PaymentsController.cs:157-158` |
| `finance.payment:cancel` | `POST /api/v1/payments/{id}/cancel` | `PaymentsController.Cancel` | `PaymentsController.cs:179-180` |
| `finance.payment:reverse` | `POST /api/v1/payments/{id}/reverse` | `PaymentsController.Reverse` | `PaymentsController.cs:203-204` |
| `finance.payment:allocate` | `POST /api/v1/payments/{paymentId}/allocations` | `PaymentAllocationsController.Allocate` | `PaymentAllocationsController.cs:80-81` |
| `finance.payment:allocate` | `DELETE /api/v1/payments/{paymentId}/allocations/{allocationId}` | `PaymentAllocationsController.Deallocate` | `PaymentAllocationsController.cs:114-115` |
| `finance.aging:read` | `GET /api/v1/aging` | `AgingController.Get` | `AgingController.cs:60-61` |
| `finance.aging:read` | `GET /api/v1/counterparty-balances` | `CounterpartyBalancesController.List` | `CounterpartyBalancesController.cs:56-57` |

All controllers live in `src/Interfaces/Payments/Finance.Payments.API/Controllers/`. **Fifteen endpoints, eight distinct permissions, zero endpoints unguarded** — no action in the five controllers is missing a `[RequirePermission]` attribute, and no attribute names a permission outside this table (verified by an exhaustive `RequirePermission\("finance\.[a-z]+:[a-z]+"\)` sweep of `src/**`, which also confirms these eight strings appear **nowhere else in the solution** — they are genuinely new, not reuses of an already-seeded permission).

Per-spec ownership, reconciling the counts each spec states: SDD-PAY-001 §2.17 owns the six on `PaymentsController` (`read`, `create`, `confirm`, `post`, `cancel`, `reverse`); SDD-PAY-002 §2.13 adds `finance.payment:allocate`; SDD-PAY-003 §2.9 adds `finance.aging:read`. Six + one + one = eight.

Further rules:

1. `finance.aging:read` **MUST NOT** be aliased to, implied by, or auto-granted with `finance.payment:read`. The split is the whole point of the second permission (SDD-PAY-003 §2.9): the roll-ups are grantable without the individual payment records.
2. `finance.payment:read` **MUST** gate `/open-items` — an open item is a projection the Payments service already owns (SDD-PAY-003 §2.5, §2.9), so it is a payment-record read, not a report read. It **MUST NOT** be moved under `finance.aging:read`.
3. Seeding **MUST** be idempotent — re-running it after a partial failure **MUST NOT** create duplicate catalogue entries or duplicate grants.
4. Descriptors **SHOULD** carry `domain = "finance"` and a human-readable description, matching the descriptor shape SDD-INT-AUTH-001 §2.4 specifies for the future auto-registration path, so the manual rows are forward-compatible with it.
5. Granting or revoking any of these eight **SHOULD** be treated as an audit-worthy security event on the auth-service side (SDD-AUDIT-001 lists permission revocation among the operations that require a `Reason`). Finance cannot enforce this — the grant happens outside this repo.

### 4.2 Suggested role grants (SHOULD)

| Role | Grants | Rationale |
|---|---|---|
| **finance clerk** (payment data entry + matching) | `finance.payment:read`, `finance.payment:create`, `finance.payment:confirm`, `finance.payment:allocate` | Records and confirms payments and matches them to invoices. Confirm is the legally significant step (it consumes a gapless НАП document number, SDD-PAY-001 §2.4), so it belongs to the person doing the entry, but nothing beyond it does. |
| **finance-reporting** (read-only, incl. collections) | `finance.aging:read` | The roll-ups only. Deliberately **no** `finance.payment:read`, so this role sees bucketed AP/AR exposure per counterparty without seeing individual payment records — the separation SDD-PAY-003 §2.9 introduced the permission for. |

Additionally **SHOULD**, and deliberately kept out of both roles above: `finance.payment:post`, `finance.payment:cancel`, and `finance.payment:reverse` **SHOULD** be granted to an accountant/controller role rather than to the clerk. `post` re-drives the posting handshake and can enqueue a fresh `PaymentConfirmedEvent` (SDD-PAY-001 §2.5 re-enqueue recovery path), `cancel` voids a draft, and `reverse` is the only way to correct a `Posted` payment and writes a sign-flipped journal entry — all three are corrective/GL-affecting rather than data-entry actions. A deployment that has no controller role yet **SHOULD** grant them to a named administrator rather than folding them into the clerk role.

A read-only auditor variant **MAY** be given `finance.payment:read` + `finance.aging:read` with no write permission at all.

## 5. Affected Specs

| Spec ID | Section | Change |
|---|---|---|
| SDD-PAY-003 | §2.9 | None to the rule. Its "MUST be recorded in the change spec" obligation is **discharged by this document** — SDD-PAY-003 §2.9 gains a pointer to `CHG-ENH-003` (applied in the same batch by the FILE 3 edit). |
| SDD-PAY-001 | §2.17, §7 | None. Its manual-seeding statements stay as written; this spec is the record they presuppose. |
| SDD-PAY-002 | §2.13, §7 | None. Same. |
| SDD-INT-AUTH-001 | §2.4 | None **by this change**. Its deferral is the reason this document exists; when auto-registration lands, this change spec becomes `Archived` (§11). See §13 for the separate §2.2 action-vocabulary drift. |
| `docs/cross-reference-map.md` | — | A record for `CHG-ENH-003` alongside the three PAY rows. |

## 6. Database Changes

**None in this repository by this change.** The auth-service catalogue/grant rows are created in the auth-service database, which Finance does not own.

Recorded here because this document is the batch's deployment record: Batch 17 ships **four** new EF Core migrations, each applied automatically by its owning service at startup (`await db.Database.MigrateAsync()` — `Finance.Payments.API/Program.cs:215`, `Finance.Journal.API/Program.cs:204`, `Finance.Invoices.API/Program.cs:159`), so redeploying a service applies its own migration:

| Database | Migration | Owner |
|---|---|---|
| `finance_payments` | `20260805171727_InitialCreate` | `Finance.Payments.DBModel` |
| `finance_payments` | `20260805175922_AddPaymentAllocations` | `Finance.Payments.DBModel` |
| `finance_journal` | `20260805174405_AddJournalEntrySourceDocument` | `Finance.Journal.DBModel` |
| `finance_invoices` | `20260805182742_AddInvoiceSettlement` | `Finance.Invoices.DBModel` |

Both Payments migrations MUST be applied — they are two migrations, not one, and they apply in timestamp order to the same end state (SDD-PAY-001 §2.16 explicitly permits the split: the SDD-PAY-002 tables "MAY ride in this same initial migration … otherwise they MUST be a NEW migration"). `20260805174405_AddJournalEntrySourceDocument` is the **only** new Journal-side migration; the two earlier Journal migrations (`20260603101540_InitialCreate`, `20260604094913_AddPostingRules`) are already applied and MUST NOT be edited (SDD-PAY-001 §2.5, §2.16). No backfill: `AddJournalEntrySourceDocument` is a pure `AddColumn` + `CreateIndex` with both columns nullable and no default, so every pre-existing entry keeps them NULL and is exempt from the filtered unique index (verified in the migration body, `:23-44`). Rollback is the generated `Down` of each migration; rolling back `AddJournalEntrySourceDocument` while the new Journal build is running would break the duplicate-post lookup (§11), so it is a redeploy-together operation.

## 7. API Changes

No endpoint, contract, error code, or i18n key is added, removed, or changed by this document. The fifteen endpoints of §4.1 already shipped, already carry their attributes, and already declare `403` where the batch's specs require it (`AgingController.cs:64`, and per SDD-PAY-003 §2.9 for all three read endpoints).

`403 INSUFFICIENT_PERMISSIONS` is produced by the RBAC layer (SDD-INT-AUTH-001 §4), not by a Finance domain error code, so it needs no entry in `Finance.Common/ErrorCodes/PaymentErrorCodes.cs` and no `errors.*` locale key. See §13 on whether a locale key is nonetheless wanted.

## 8. Event Contract Changes

None. Permissions are enforced on the HTTP request path only. MassTransit consumers (`PaymentPostedEventConsumer`, `InvoiceOpenItemProjectionConsumer`, `PaymentAllocatedEventConsumer`, `PaymentDeallocatedEventConsumer`, `PaymentConfirmedEventConsumer`) carry no user identity and are unaffected by seeding — so the confirm → journal → post handshake and the settlement projection keep working even while every HTTP endpoint is returning `403`. That asymmetry is worth knowing during triage: events flow, requests do not.

## 9. Frontend Impact

None today. There is no payments UI: SDD-UI-FIN-002 is not authored, and no `frontend/src/features/payments*` exists. The shipped invoices UI (SDD-UI-FIN-001) calls no Payments endpoint.

Recorded for whoever authors SDD-UI-FIN-002: all 43 payment `errors.*` keys — including the fourteen allocation codes and the five aging codes — plus `INVOICE_HAS_SETTLEMENTS` already shipped in this batch in both `en.ts` and `bg.ts`, pinned at exact EN/BG parity with non-empty Cyrillic BG text by `frontend/src/shared/i18n/paymentErrorCodes.test.ts` (`:23-74`, `:116-129`). There is **no** key for a `403`, and the only fallback is `errors.GENERIC_ERROR` — *"Something went wrong. Please try again."* (`en.ts:384`) — so an unseeded permission would render as an unexplained generic failure (§2). The UI **SHOULD** distinguish "you lack the permission" from "something went wrong" before the payments views ship.

## 10. Testing

This change is not test-bearing inside this repository, and cannot be: the catalogue lives in another system. What **is** already covered offline, and what remains owed:

| Coverage | Where | Status |
|---|---|---|
| Every action's DECLARED permission string, per controller | `Finance.Payments.API.Tests/Unit/Controllers/PaymentControllerContractTests.cs` — four `[Test]` methods: `PaymentsController_EveryAction_DeclaresItsRequiredPermission` (asserts all nine actions and their exact strings, `:21-50`), `PaymentAllocationsController_AllocateAndDeallocate_RequireTheAllocatePermission` (`:54-71`), `AgingControllers_DeclareTheReportLevelAgingPermission_ButOpenItemsReadsAsAPayment` (`:75-92`), `EveryAction_DeclaresProducesResponseType_AndTakesCancellationTokenLast` (`:96-127`) | **Green.** Reflection over `RequirePermissionAttribute`, no host, no auth-service. This is what makes §4.1's table a verified contract rather than prose — a typo in an attribute breaks a test. |
| The `403` behavior itself | Four distinct `[Integration]` test names across five spec rows: `Endpoint_Returns403_WhenPermissionMissing` (SDD-PAY-001 §6 **and** SDD-PAY-002 §6 — same name, two specs), plus `OpenItems_Endpoint_Returns403_WhenPermissionMissing`, `Aging_Endpoint_Returns403_WhenPermissionMissing`, `CounterpartyBalances_Endpoint_Returns403_WhenPermissionMissing` (SDD-PAY-003 §6) | **DEFERRED — not written.** No `Finance.Payments.API.Tests/Integration/` directory exists. Same deferral as the rest of the Batch-17 integration suite; recorded as a gap, not as coverage. Note that even when written they assert the *service's* enforcement against a granted/withheld test permission — they can never prove the production catalogue was seeded. |
| That auth-service actually holds the eight rows | — | **Not automatable from this repo.** The only verification is the §11 smoke check. |

## 11. Rollout

No feature flag: RBAC enforcement is not switchable, and it should not be.

**Ordering (each step gates the next):**

1. **Deploy the Journal service first.** Its startup applies `20260805174405_AddJournalEntrySourceDocument` to `finance_journal`. This MUST happen **before** the Payments service starts publishing `PaymentConfirmedEvent`: the Journal-side `PaymentConfirmedEventConsumer` calls `IJournalEntryService.FindPostedBySourceDocumentAsync` before it posts anything (`PaymentConfirmedEventConsumer.cs:73-76`), and that query filters on `SourceDocumentType`/`SourceDocumentId` (`JournalEntryService.cs:105-113`). Against an unmigrated `finance_journal` those columns do not exist, so the very first `PaymentConfirmedEvent` faults the consume, retries, and dead-letters to `finance-journal_error` (loudly, and recoverably, only because CHG-FIX-006 shipped in this batch) — and until it is fixed the duplicate-post backstop that SDD-PAY-001 §2.5 depends on is absent. Deploying the new Journal build applies the migration automatically; the risk is a build/migration skew, not a forgotten script.
2. **Deploy Invoices** (applies `20260805182742_AddInvoiceSettlement`) so `PaymentAllocatedEventConsumer`/`PaymentDeallocatedEventConsumer` have the settlement columns to write before any allocation event lands.
3. **Deploy Payments** (applies `20260805171727_InitialCreate` then `20260805175922_AddPaymentAllocations` to `finance_payments`).
4. **Seed the eight permissions in auth-service and grant the §4.2 roles.** Everything above is green and still 100 % unusable over HTTP until this is done.
5. **Fill the gateway placeholder.** `payments-route`, `open-items-route`, `aging-route`, and `counterparty-balances-route` plus `payments-cluster` and the top-level `"PaymentsApi"` entry already ship in `src/Infrastructure/Gateway/Finance.Gateway/appsettings.json.template` (`:18`, `:137-160`, `:212`); the real `appsettings.json` is gitignored, so `<PAYMENTS_API_URL>` MUST be substituted per environment.
6. **Smoke check, one call per permission** — eight authenticated requests, one per distinct permission, expecting anything **other** than `403`. A `200`/`400`/`404` all prove the grant landed; a `403` proves it did not. Run it with a clerk token and with a finance-reporting token, and confirm the reporting token is refused on `GET /api/v1/payments` — that negative check is what proves the aging/record-level split survived seeding (§4.1 rule 1).

**Companion deployment step, not RBAC but the other thing that silently breaks the batch:** the two new Bulgarian payment posting-rule templates `PAYMENT_CUSTOMER_RECEIPT` and `PAYMENT_SUPPLIER_PAYMENT` (`BulgariaStrategy.cs:178`, `:190`; `BuildDefaultRules()` now returns seven templates, `:101-110`) reach `journal.PostingRules` only through the Journal seeder, which is gated by the `EnablePostingRuleSeeding` feature flag — **`false` in `Finance.Journal.API/appsettings.json.template:31`**, checked at `Finance.Journal.API/Program.cs:212`. Until the flag is enabled (after accountant sign-off on the account codes), a confirmed payment's journal post fails with `POSTING_RULE_NOT_FOUND` and retries/dead-letters, leaving the payment `Confirmed` and unlinked (SDD-PAY-001 §2.18). Re-driving `POST /{id}/post` after the rules are seeded completes the handshake, so this is recoverable — but only if the operator knows which of the two prerequisites they missed, which is why both are recorded in one place.

**Downstream coordination:** auth-service repository (catalogue + roles) and the accountant sign-off above. No Warehouse-side change.

**Retirement:** when SDD-INT-AUTH-001 §2.4 auto-registration ships, the eight strings become self-registering and this change spec moves to `Archived` with a pointer to the spec that superseded it. The role grants remain manual regardless — auto-registration registers permissions, not assignments.

## 12. Risks

- **A blanket `finance.payment:*` wildcard grant.** The likeliest shortcut, and it silently destroys two deliberate boundaries: the aging/record-level split (§4.1 rule 1) and the clerk/controller split on `post`/`cancel`/`reverse` (§4.2). It also hands a data-entry clerk the ability to reverse a posted payment, which writes a sign-flipped journal entry into the GL. Mitigation: the negative half of the §11 step-6 smoke check.
- **Seeding only seven of the eight.** `finance.aging:read` is the odd one out — a different resource, introduced by a different spec, and the only one whose endpoints are pure reads. It is the one most likely to be dropped from a hand-written list, and its symptom (two report endpoints refusing everyone while payments work fine) reads like a routing bug. Mitigation: §4.1's table is the checklist, and `AgingControllers_DeclareTheReportLevelAgingPermission_ButOpenItemsReadsAsAPayment` keeps the code side honest.
- **A typo in a permission string.** Fails closed (`403`), so it cannot grant more than intended — but the offline contract tests only pin the *attribute* side. A misspelling in the auth-service row is invisible to this repo and is caught only by the smoke check.
- **Nothing in CI can fail on an unseeded environment.** This document, and the release checklist that reads it, are the only enforcement. That is exactly the residual risk the deferral in SDD-INT-AUTH-001 §2.4 buys, and the reason this record was demanded by SDD-PAY-003 §2.9 in the first place.
- **Migration/service skew on `finance_journal`** (§11 step 1). A new Journal build whose migration did not apply dead-letters the first `PaymentConfirmedEvent` instead of posting it. Recoverable by applying the migration and replaying from `finance-journal_error`, and no longer silent — but only because CHG-FIX-006 made the DLQ reachable.

## 13. Open Questions

- **SDD-INT-AUTH-001 §2.2 restricts actions to `read`, `write`, `delete`, `post`, `approve`.** Five of this batch's eight permissions use verbs outside that list — `finance.payment:create`, `:confirm`, `:cancel`, `:reverse`, `:allocate` (only `finance.payment:read`, `finance.payment:post`, and `finance.aging:read` comply) — and so do many already-shipped Finance permissions (`finance.period:close`, `finance.period:reopen`, `finance.period:create`, `finance.invoice:confirm`, `finance.journal:reverse`, `finance.posting:apply`, …). The vocabulary rule is stale platform-wide and predates Batch 17 by many batches; nothing enforces it, and no shipped code is wrong. It should be widened (or explicitly relaxed to "a lowercase verb matching the operation") in SDD-INT-AUTH-001 §2.2 — **not** here, and not by renaming shipped permissions. Raising it as a separate `CHG-` against that spec is the clean route; noted here because a seeder validating against §2.2's list would reject five of these eight rows.
- Should the platform own a single consolidated permission catalogue document? Finance now declares **28 distinct permissions across nine resources** (`account`, `nomenclature`, `period`, `journal`, `posting`, `invoice`, `event`, `payment`, `aging` — counted from an exhaustive `[RequirePermission]` sweep of `src/**`), of which Batch 17 contributed eight. Nothing like a catalogue exists today: SDD-INT-AUTH-001 §2.2 names only the two Phase-0 `finance.account:*` permissions, so every batch rediscovers the set by grepping. The next batch will face the same archaeology.
- Should a `403` get its own `errors.*` locale key (EN + BG) so the UI can say "you do not have permission" rather than the generic fallback (§9)? Cheap, and it would make an unseeded environment self-diagnosing for the operator. Deferred to SDD-UI-FIN-002.
- Should `finance.payment:create` be split into `create`/`update`/`delete`? It currently gates all three write shapes on `PaymentsController` (`:70-71`, `:96-97`, `:116-117`), which is intentional — all three only ever touch a `Draft` (SDD-PAY-001 §2.3/§2.6/§2.7) — but it does mean "may record a payment" implies "may delete a draft". Left as shipped; flagged so the choice is on the record.
