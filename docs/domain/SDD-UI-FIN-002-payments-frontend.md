# SDD-UI-FIN-002 — Payments Frontend Feature (Customer Receipts + Supplier Payments, Allocation, AP/AR Aging)

> Status: **Implemented** (2026-08-10 — the Payments SPA surface shipped and verified per the CLAUDE.md §0 lifecycle. `features/payments/` (`types.ts`, `api.ts`, `schema.ts`, `usePaymentMutations.ts`, `useAllocationMutations.ts`), the `PaymentsListPage` / `OpenItemsListPage` / `AgingReportPage` routes (the last hosting both the ageing report and counterparty balances behind one control bar per §2.15), the `PaymentFormDialog` / `CancelPaymentDialog` / `ReversePaymentDialog` / `PaymentAllocationsDialog` / `AllocatePaymentDialog` organisms, the `ForbiddenState` molecule, and the `payments.*` / `allocations.*` / `openItems.*` / `aging.*` / `balances.*` / `nav.*` key groups at exact EN↔BG parity — reusing the 43 shipped `errors.*` payment codes rather than re-declaring them. Dialog mode only; no `SDD-UI-002` page-mode machinery. **Test evidence:** 365 Vitest tests green across 34 files (from 219/21), `tsc -b` clean. **Phase 7 `ui-validate`** ran the §7.2(a) offline set in Chrome: 14 of 16 passed first time, and all nine defects it raised are fixed — the gating one being a `403` on an ACTION rendering raw developer English because neither locale defined `errors.FORBIDDEN` (§2.17), which would have hit EVERY action button while the `CHG-ENH-003` permissions stay unseeded. Also fixed: unthemed Material-blue notistack toasts (§1.1), an empty-state action rendered fully invisible inside the DataGrid's `overflow: hidden` virtual scroller, a clipped unapplied badge, untranslated grid chrome under BG, a `<label for>` pointing at a non-labelable combobox, and an unbounded `/aging` request per keystroke on the one uncapped endpoint. **Deferred:** the eleven §7.2(b) `PaymentsUi_Live_*` checks are NOT-RUN — no Docker daemon, so no gateway, API, SQL, Redis or RabbitMQ, and the eight RBAC permissions are unseeded; the confirm→document-number transition, the posting handshake, allocation arithmetic and a genuine RBAC 403 were only ever exercised against injected XHR stubs and MUST be re-verified live. Also deferred: a standalone payment detail route (§1.2 forbids inventing one, so six `payments.detail*` keys are defined but unrendered), and counterparty NAMES everywhere — every DTO carries only a `Guid` until `SDD-INT-WH-002` is authored, so the AP/AR ageing report ships GUID-keyed (§1.6 gap 1, the largest usability gap in this surface).)
> Owner: Frontend
> Last updated: 2026-08-10
> Category: Domain (feature UI surface). The companion frontend **shell** spec `SDD-UI-001` (theme, density, i18n, navigation, Atomic Design, axios/correlation, error helper) lives in `docs/infrastructure/`; this spec depends on it and MUST NOT re-define shell behavior.
> Authored AFTER the backend shipped, FROM the shipped endpoints — this is the `SDD-UI-FIN-001` precedent. Every endpoint, verb, DTO field, enum member, error code, permission, and status transition named below was read out of the source files cited in §1.3 / §1.4, not inferred from the backend specs' prose. Where the shipped code and a backend spec's narrative could be read differently, the **code wins** and the divergence is called out inline.
> Related: **SDD-PAY-001** (Payment Recording & Lifecycle — the 9 `/api/v1/payments/*` endpoints, the `Draft → Confirmed → Posted` lifecycle with `Cancelled` from **`Draft` ONLY** and `Reversed` from `Posted`, gapless `RCT`/`PAY` numbering at confirm, the `PAYMENT_POSTING_PENDING` handshake + its confirm-event re-enqueue recovery, posted immutability, the settlement account, amount/FX freeze), **SDD-PAY-002** (Payment Allocation & Settlement — the 3 `/allocations` endpoints, the ten-rule invariant chain, derived `SettlementStatus`, the informational `RealizedFxDifference`), **SDD-PAY-003** (AP/AR Aging & Counterparty Balances — `/open-items`, `/aging`, `/counterparty-balances`, the configurable buckets, the separate `finance.aging:read` permission), SDD-UI-001 (frontend shell — ledger theme/density/i18n/navigation/axios/error helper this feature builds on), SDD-UI-FIN-001 (the Invoices frontend feature — the STRUCTURAL and behavioral precedent this spec mirrors; the invoice is the document a payment settles), SDD-INV-001 (Invoice Lifecycle — the counterpart document; its `SettlementStatus` is the same shared enum), SDD-INFRA-001 (ProblemDetails error model + `X-Correlation-ID` on every request), SDD-INFRA-005 (Generic Filtering — the `FilterRequest`/`PagedResult` contract, `PageSize ≤ 200`), SDD-INT-AUTH-001 (shared JWT + RBAC), **CHG-ENH-003** (the EIGHT Payments permissions that MUST be seeded manually in auth-service, without which every screen in this spec answers `403`), SDD-NOM-001 (currency dropdown via `useNomenclature()`), SDD-ACCT-001 (the Chart of Accounts the settlement-account picker reads), SDD-CTRY-001 (document-number format, base currency and monetary rounding are server-side; the UI DISPLAYS them, never recomputes them authoritatively), SDD-INT-WH-002 (counterparty name enrichment — DEFERRED, which is why every counterparty renders as a raw GUID in v1, §1.6), SDD-FIN-005 (decimal arithmetic + FX rate resolution — not yet authored; realized-FX posting and automatic rate lookup wait on it). The dialog-vs-page dual-mode spec `SDD-UI-002` is `Planned` and NOT built; this app is **dialog-mode** (§1.2), so this spec describes the established dialog pattern and MUST NOT invent page-mode machinery.
> ISA-95: Level 4 (Business Planning & Logistics) — UI surface over financial Documents (cash settlement) and the sub-ledger roll-ups derived from them.

---

## 1. Context & Scope

This spec defines the **Payments frontend feature surface** of the Finance SPA: the React + TypeScript + MUI views, hooks, schemas, and dialogs that let an operator record and settle cash — the two payment documents **Customer Receipt** (money in, `AR`) and **Supplier Payment** (money out, `AP`) (one backend aggregate discriminated by `DocumentType` with a derived, frozen `Direction`, SDD-PAY-001 §1) — across their `Draft → Confirmed → Posted` lifecycle (plus `Cancelled` from **`Draft` only** and `Reversed` from `Posted`), plus the **allocation** sub-collection that matches a payment against open invoices, plus the three read-only **AP/AR roll-ups** (open items, bucketed aging, counterparty balances).

It is a **pure consumer** of the shipped `Finance.Payments.API` REST contract — **15 endpoints across 5 controllers**, proxied by `Finance.Gateway`. It introduces NO new backend behavior, endpoints, events, error codes, tables, permissions, or business rules. The server is **authoritative** for the base amount, the document number, state transitions, immutability, allocation invariants, settlement derivation, bucket assignment, and every validation; this UI MAY mirror those rules client-side for immediate feedback but MUST re-display the server's persisted values after every write and MUST surface every server error through the shared error helper.

### 1.1 Relationship to the shell (SDD-UI-001) and the established feature pattern

This feature MUST be built exactly like the already-shipped `invoices` feature surface (the canonical pattern to mirror — `SDD-UI-FIN-001`), and MUST reuse the shell primitives from SDD-UI-001:

- Feature folder `frontend/src/features/payments/` with `api.ts` (typed axios calls), `types.ts` (wire contracts mirroring the .NET DTOs field-for-field), `schema.ts` (zod form schemas), and `usePaymentMutations.ts` (TanStack Query mutations) — mirroring `features/invoices/{types,api,schema,useInvoiceMutations}.ts`. The allocation and aging surfaces MAY live in the same feature folder (`features/payments/allocations.ts`, `features/payments/aging.ts`) or in sibling folders `features/allocations/` and `features/aging/`; the implementator MUST pick ONE layout and keep the four-file shape.
- List pages `frontend/src/components/pages/PaymentsListPage.tsx`, `OpenItemsListPage.tsx`, and `AgingReportPage.tsx` mirroring `InvoicesListPage.tsx` — `ListPageTemplate` + `DataTable` (the MUI DataGrid wrapper, with `ledgerMonoColumn`) + `FilterBar`, server-side `FilterRequest` paging/sort/search, status-gated row actions, `notification.error(getApiErrorMessage(...))` on every failure.
- Organisms `PaymentFormDialog.tsx` (create/edit draft, mirroring `InvoiceFormDialog.tsx`), `CancelPaymentDialog.tsx` and `ReversePaymentDialog.tsx` (reason-prompt, mirroring `CancelInvoiceDialog.tsx` / `ReverseJournalEntryDialog.tsx` / the shared `ReasonPromptDialog`), `PaymentAllocationsDialog.tsx` (the per-payment allocation panel: existing rows + the open-item picker), and destructive/confirm flows through the shared `ConfirmDialog`.
- The shared axios instance (`@/shared/api/axios` — attaches `Authorization` + a fresh `X-Correlation-ID` per request, SDD-UI-001 §2.2), `toFilterParams` / `MAX_PAGE_SIZE` / `PagedResult` / `FilterRequest` from `@/shared/api/paging`, `getApiErrorMessage` (SDD-UI-001 §2.5), the `notification` helper, the `useLayoutStore` density store (SDD-UI-001 §2.4), the ledger theme (SDD-UI-001 §2.8 — serif Fraunces headings, hairline rules, paper/ink palette, deep-green accent, mono **tabular figures** for money/codes, `MoneyText` / `CodeText` / `StatusDot` atoms), and EN+BG i18n (SDD-UI-001 §2.3).
- **Styling is the MUI theme + `sx` + the density store.** There is NO Tailwind in this repository. The `mb-2 p-3` / `mb-4 p-4` examples in `CLAUDE.md` §0.3.B are leftovers from a Warehouse Vue port and MUST be ignored; density is expressed through MUI `sx` spacing and the `DataTable` density prop, exactly as `InvoicesListPage` does.
- **The aesthetic MUST NOT look AI-generated.** No glows, no gradient heroes, no gradient stat-cards, no glassmorphism, no pill buttons, no emoji, and no default Material blue (`#1976d2` / `#1565c0`). The aging report is an *editorial ledger table*, not a dashboard: hairline-ruled columns, mono tabular figures, uppercase letter-spaced headers, a single deep-green accent. The one permitted colored surface is the sidebar (SDD-UI-001 §2.8) — an aging report MUST NOT introduce a second one.

### 1.2 Confirmed frontend conventions (read out of the shipped code)

- **Display mode: dialog-mode only.** The app ships dialog-based create/edit (`*FormDialog` organisms); `frontend/src/app/App.tsx` declares NO `*CreatePage`/`*EditPage` routes and there is no `isPageMode` flag anywhere. `SDD-UI-002` is `Planned`/not built. This feature MUST therefore use dialogs and MUST NOT introduce page-mode routes, an `isPageMode` read, or a `mode: 'dialog' | 'page'` prop (no overengineering).
- **`useGoBack` exists** (`frontend/src/shared/hooks/useGoBack.ts`) but is NOT required here, because every form closes in place. It MAY be used only if a future standalone read-only detail route is added (out of scope for v1).
- **No client-side permission hook.** There is no `usePermission`/`hasPermission` in the codebase. RBAC is enforced by the backend `[RequirePermission(...)]`; a `403` surfaces through `getApiErrorMessage`. Action availability in the UI is gated by **entity status**. Hiding a control by the caller's permission set is a MAY, not a current pattern (§2.4).
- **Enum serialization is NUMERIC.** `Finance.Payments.API` registers NO `JsonStringEnumConverter` (verified: the only `JsonStringEnumConverter` in `src/` is in `Finance.EventLog.API/Mapping/EventLogJsonOptions.cs`), so `System.Text.Json` emits the `Finance.Common.Enums.Payment*` enums and `SettlementStatus` as their integer values — matching the shipped `features/invoices/types.ts`.
- **The wire is MIXED numeric and string enums, and this is the single easiest thing to get wrong (§1.4 trap 1).** On the SAME feature surface:
  - **Numeric** (real C# enums): `PaymentDto.documentType` (1–2), `.direction` (`AP = 1`, `AR = 2`), `.method` (1–3), `.status` (`Draft = 1` … `Reversed = 5`); `PaymentAllocationDto.invoiceSettlementStatus` (`SettlementStatus?`, 1–3); `AllocatedInvoiceSettlementDto.settlementStatus`; `OpenItemDto.settlementStatus`.
  - **Strings** (declared `string` on the DTO): `OpenItemDto.documentType`, `.direction`, `.invoiceStatus`, `.agingBucket`; `PaymentAllocationDto.invoiceStatus`; `AgingReportDto.direction`, `.bucketLabels[]`; `AgingRowDto` / `CounterpartyBalanceDto` `.direction`; `AgingBucketAmountDto.label`; `AgingBucketTotalDto.label`.
  - **Request narrowings** `direction` are strings (`"AR"` / `"AP"`) on `OpenItemQueryRequest`, `AgingReportQueryRequest`, `CounterpartyBalanceQueryRequest` — never the numeric `PaymentDirection`.
- **`PaymentDirection` is `AP = 1`, `AR = 2`** — the numbering is NOT alphabetical-by-intuition and mirrors `InvoiceDirection` value-for-value on purpose (`src/Finance.Common/Enums/PaymentDirection.cs`). A TypeScript enum that guesses `AR = 1` silently mislabels every row.
- **`CreatedBy`, `ConfirmedBy`, and `CorrelationId` are deliberately NOT on `PaymentDto`**, and `AllocatedBy`/`CorrelationId` are not on `PaymentAllocationDto`. The UI therefore cannot show "who recorded / confirmed this" in v1 (§1.6).

### 1.3 Backend-Linkage matrix (the complete consumed surface)

Every capability this feature exposes, the shipped endpoint it consumes, the backend spec section that owns the behavior, the RBAC permission the shipped controller declares, and the error codes the UI MUST be able to surface for it. Sources: `src/Interfaces/Payments/Finance.Payments.API/Controllers/{PaymentsController,PaymentAllocationsController,OpenItemsController,AgingController,CounterpartyBalancesController}.cs`; `src/Finance.ServiceModel/Payments/**`; `src/Finance.Common/ErrorCodes/PaymentErrorCodes.cs`; `src/Interfaces/Payments/Finance.Payments.API/ErrorMapping/PaymentErrorCodeToStatusMap.cs`.

| # | Capability (UI) | Endpoint + verb | Owning spec § | RBAC permission | Error codes the UI MUST surface |
|---|---|---|---|---|---|
| 1 | List payments (paged/filtered/sorted/searched) | `GET /api/v1/payments` | SDD-PAY-001 §2.11 | `finance.payment:read` | `PAGE_SIZE_TOO_LARGE` |
| 2 | Read one payment | `GET /api/v1/payments/{id:guid}` | SDD-PAY-001 §2.11 | `finance.payment:read` | `PAYMENT_NOT_FOUND` |
| 3 | Create draft (**201 `CreatedAtAction`**) | `POST /api/v1/payments` | SDD-PAY-001 §2.3, §2.8 | `finance.payment:create` | `INVALID_PAYMENT_DOCUMENT_TYPE`, `INVALID_PAYMENT_METHOD`, `PAYMENT_COUNTERPARTY_REQUIRED`, `INVALID_PAYMENT_CURRENCY`, `INVALID_PAYMENT_AMOUNT`, `INVALID_PAYMENT_EXCHANGE_RATE`, `INVALID_PAYMENT_DATE`, `PAYMENT_SETTLEMENT_ACCOUNT_REQUIRED`, `INVALID_PAYMENT_BANK_REFERENCE`, `PAYMENT_SETTLEMENT_ACCOUNT_NOT_FOUND`, `PAYMENT_SETTLEMENT_ACCOUNT_INACTIVE`, `PAYMENT_BASE_AMOUNT_MISMATCH` |
| 4 | Edit draft | `PUT /api/v1/payments/{id:guid}` | SDD-PAY-001 §2.6, §2.10 | `finance.payment:create` | all of #3, plus `PAYMENT_NOT_FOUND`, `PAYMENT_POSTED_IMMUTABLE`, `INVALID_PAYMENT_DOCUMENT_TYPE` (type changed), `CONCURRENT_MODIFICATION` |
| 5 | Delete draft | `DELETE /api/v1/payments/{id:guid}` | SDD-PAY-001 §2.6 | `finance.payment:create` | `PAYMENT_NOT_FOUND`, `PAYMENT_POSTED_IMMUTABLE` |
| 6 | Confirm (assigns the gapless `RCT`/`PAY` number) | `POST /api/v1/payments/{id:guid}/confirm` | SDD-PAY-001 §2.2, §2.4, §2.9 | `finance.payment:confirm` | `PAYMENT_NOT_FOUND`, `PAYMENT_NOT_DRAFT`, `PAYMENT_DUPLICATE_DOCUMENT_NUMBER`, `PAYMENT_DATE_YEAR_MISMATCH`, `PAYMENT_PERIOD_CLOSED`, `PAYMENT_SETTLEMENT_ACCOUNT_NOT_FOUND`, `PAYMENT_SETTLEMENT_ACCOUNT_INACTIVE`, `PAYMENT_BASE_AMOUNT_MISMATCH`, `CONCURRENT_MODIFICATION` |
| 7 | Post — complete the handshake **/ re-enqueue the confirm event** | `POST /api/v1/payments/{id:guid}/post` | SDD-PAY-001 §2.5 | `finance.payment:post` | `PAYMENT_NOT_FOUND`, `PAYMENT_NOT_CONFIRMED`, **`PAYMENT_POSTING_PENDING`** (normal transient state, §1.4 trap 3), `PAYMENT_PERIOD_CLOSED`, `CONCURRENT_MODIFICATION` |
| 8 | Cancel — **`Draft` ONLY** | `POST /api/v1/payments/{id:guid}/cancel` | SDD-PAY-001 §2.1, §2.6 | `finance.payment:cancel` | `PAYMENT_NOT_FOUND`, `PAYMENT_CANCEL_REASON_REQUIRED`, `INVALID_PAYMENT_STATE_TRANSITION`, `PAYMENT_HAS_ALLOCATIONS`, `CONCURRENT_MODIFICATION` |
| 9 | Reverse — **`Posted` only**, reason required | `POST /api/v1/payments/{id:guid}/reverse` | SDD-PAY-001 §2.7 | `finance.payment:reverse` | `PAYMENT_NOT_FOUND`, `PAYMENT_REVERSE_REASON_REQUIRED`, `INVALID_PAYMENT_STATE_TRANSITION`, `PAYMENT_HAS_ALLOCATIONS`, `PAYMENT_PERIOD_CLOSED`, `CONCURRENT_MODIFICATION` |
| 10 | List a payment's allocations (paged, invoice-enriched) | `GET /api/v1/payments/{paymentId:guid}/allocations` | SDD-PAY-002 §2.7 | `finance.payment:read` | `PAYMENT_NOT_FOUND`, `PAGE_SIZE_TOO_LARGE` |
| 11 | Allocate against open items (**200, not 201; no `Location`**) | `POST /api/v1/payments/{paymentId:guid}/allocations` | SDD-PAY-002 §2.4, §2.5 | `finance.payment:allocate` | `PAYMENT_NOT_FOUND`, `PAYMENT_ALLOCATION_ITEMS_REQUIRED`, `PAYMENT_ALLOCATION_INVOICE_REQUIRED`, `INVALID_PAYMENT_ALLOCATION_AMOUNT`, `PAYMENT_NOT_ALLOCATABLE`, `PAYMENT_ALLOCATION_INVOICE_NOT_FOUND`, `PAYMENT_ALLOCATION_INVOICE_NOT_ELIGIBLE`, `PAYMENT_ALLOCATION_DIRECTION_MISMATCH`, `PAYMENT_ALLOCATION_COUNTERPARTY_MISMATCH`, `PAYMENT_ALLOCATION_CURRENCY_MISMATCH`, `PAYMENT_ALLOCATION_DUPLICATE`, `PAYMENT_ALLOCATION_EXCEEDS_PAYMENT`, `PAYMENT_ALLOCATION_EXCEEDS_OUTSTANDING`, `PAYMENT_ALLOCATION_CONTROL_ACCOUNT_MISMATCH`, `CONCURRENT_MODIFICATION` |
| 12 | Deallocate one row (**`rowVersion` + `reason` are QUERY params**) | `DELETE /api/v1/payments/{paymentId:guid}/allocations/{allocationId:int}?rowVersion=&reason=` | SDD-PAY-002 §2.6 | `finance.payment:allocate` | `PAYMENT_NOT_FOUND`, `PAYMENT_ALLOCATION_NOT_FOUND`, `PAYMENT_NOT_ALLOCATABLE`, `CONCURRENT_MODIFICATION` |
| 13 | Open items worklist (oldest-due-first drill-down) | `GET /api/v1/open-items` | SDD-PAY-003 §2.5 | `finance.payment:read` | `INVALID_AGING_AS_OF_DATE`, `INVALID_AGING_DIRECTION`, `INVALID_COUNTERPARTY_ID`, `INVALID_AGING_CURRENCY`, `PAGE_SIZE_TOO_LARGE` |
| 14 | Bucketed AP/AR aging report (configurable buckets) | `GET /api/v1/aging` | SDD-PAY-003 §2.4, §2.6 | **`finance.aging:read`** | `INVALID_AGING_AS_OF_DATE`, `INVALID_AGING_DIRECTION`, **`INVALID_AGING_BUCKETS`**, `INVALID_COUNTERPARTY_ID`, `INVALID_AGING_CURRENCY` |
| 15 | Counterparty balances (paged roll-up) | `GET /api/v1/counterparty-balances` | SDD-PAY-003 §2.7 | **`finance.aging:read`** | `INVALID_AGING_AS_OF_DATE`, `INVALID_AGING_DIRECTION`, `INVALID_AGING_CURRENCY`, `PAGE_SIZE_TOO_LARGE` |

`finance.aging:read` is a **SEPARATE permission** from `finance.payment:read` (`AgingController.cs:61`, `CounterpartyBalancesController.cs:57` vs `OpenItemsController.cs:57`), deliberately, so a collections / finance-reporting role can be granted the roll-ups without the individual payment records (SDD-PAY-003 §2.9). The UI MUST NOT assume that a caller who can read payments can read aging, and MUST NOT assume the converse.

### 1.4 Traps the implementator MUST NOT walk into

1. **Mixed numeric/string enums on one surface (§1.2).** `PaymentDto.direction` is the number `2` for `AR`; `OpenItemDto.direction` is the string `"AR"`; `AgingReportQueryRequest.direction` must be SENT as the string `"AR"`. The TypeScript types MUST mirror each DTO field-for-field and MUST NOT normalize one representation into the other silently. `PaymentDirection` is `AP = 1`, `AR = 2`.
2. **Which actions are legal in which status — gate in the UI, do NOT rely on a 403/409 (§2.4).** The legal set is narrower than the invoice one:
   - `Draft` → Edit, Confirm, Cancel, Delete. (Allocation is **NOT** available: `PaymentAllocatableValidator` requires `Confirmed`/`Posted`.)
   - `Confirmed` → Post (or "posting…"), Allocate/Deallocate. **NO Cancel, NO Edit, NO Delete, NO Reverse.**
   - `Posted` → Reverse (reason required), Allocate/Deallocate. **NO Cancel, NO Edit, NO Delete.**
   - `Cancelled` / `Reversed` → nothing (terminal). Allocation rows on a reversed payment are read-only history.
3. **`Confirmed → Cancelled` was DELIBERATELY REMOVED and the UI MUST NOT offer it.** `PaymentStatus.Confirmed`'s `AllowedNextStates` is `{ Posted }` only (SDD-PAY-001 §2.1). Cancelling a `Confirmed`/`Posted`/`Cancelled`/`Reversed` payment answers `INVALID_PAYMENT_STATE_TRANSITION` (409). This is the single biggest behavioral difference from `SDD-UI-FIN-001`, where Cancel IS offered on a `Confirmed` invoice — copying that row action across is a defect. A confirmed payment is completed to `Posted` and then **reversed**.
4. **A document number appears only after confirm.** `Payment.DocumentNumber` is NULL while `Draft` and is assigned inside the confirm transaction from `ISequenceGenerator` + `ICountryStrategy.GenerateDocumentNumber` (`RCT-{yyyy}-{nnnnnn}` / `PAY-{yyyy}-{nnnnnn}`). A `Draft` row MUST render `—`.
5. **A `Cancelled` payment ALSO shows `—` forever.** Because cancel is `Draft`-only and a `Draft` never held a number, `DocumentNumber` on a `Cancelled` row is always NULL in v1 (SDD-PAY-001 §2.6). This is the OPPOSITE of `SDD-UI-FIN-001` §2.7, where a cancelled *Confirmed* invoice keeps its number. Do not port that rule.
6. **`PAYMENT_POSTING_PENDING` is a NORMAL transient state, not an error to alarm the user with.** It is returned as HTTP **409** by `PaymentErrorCodeToStatusMap`, but semantically it means "the Journal handshake has not landed yet, and this call just re-enqueued `PaymentConfirmedEvent` for you". The UI MUST present it as an informational / progress affordance ("posting…", "retry queued"), NOT as a red destructive failure, and MUST NOT block or hide the payment. Re-driving Post is the documented, bounded, safe recovery path (SDD-PAY-001 §2.5) — the operator MAY press it again.
7. **The aging `buckets` parameter binds as REPEATED query values, not a comma-separated string.** `AgingReportQueryRequest.Buckets` is `int[]?` and its XML doc pins the wire form: `?buckets=30&buckets=60&buckets=90`. Axios' default array serialization emits `buckets[]=30&buckets[]=60`, which ASP.NET Core will NOT bind to `int[] Buckets`. The implementator MUST serialize with repeat semantics (e.g. `paramsSerializer: { indexes: null }` on that request, or build the query string explicitly). A comma-separated `?buckets=30,60,90` would require a **custom model binder that does not exist** — do NOT send it and do NOT add one (that would be a backend change this spec does not own).
8. **`toFilterParams` does NOT carry the query narrowings.** `frontend/src/shared/api/paging.ts` emits only `Page`, `PageSize`, `Search`, `Filters[i].*`, `Sort[i].*`. `GET /open-items` and `GET /counterparty-balances` bind **both** a `FilterRequest` **and** a narrowing record (`OpenItemQueryRequest` / `CounterpartyBalanceQueryRequest`) from the SAME query string, so the caller MUST merge `toFilterParams(request)` with the narrowing params. `GET /aging` binds NO `FilterRequest` at all.
9. **Deallocate is a `DELETE` whose `rowVersion` and `reason` are QUERY parameters, not a body** (`PaymentAllocationsController.Deallocate`, `[FromQuery] string? rowVersion, [FromQuery] string? reason`). Both are optional; when `rowVersion` is omitted the server still guards with the token it loads inside the transaction.
10. **Allocate answers `200`, not `201`, and emits no `Location` header** (SDD-PAY-002 §2.13) — the rows are a sub-collection of the payment aggregate. The response body (`AllocatePaymentResultDto`) already carries the new `rowVersion`, the new `allocatedAmount`/`unallocatedAmount`, and every affected invoice's post-change settlement state, so the UI MUST consume it and MUST NOT issue a follow-up read to learn the new state.
11. **`rowVersion` MUST be re-seeded from the write response after every allocate/deallocate.** Allocation increments the payment's `RowVersion`, so a cached token from the list query goes stale immediately. `AllocatePaymentResultDto.rowVersion` / `DeallocatePaymentResultDto.rowVersion` exist precisely so the caller can chain writes.
12. **Reverse is blocked while `allocatedAmount > 0`** (`PAYMENT_HAS_ALLOCATIONS`, 409). Allocations are NEVER auto-released. The UI MUST tell the operator to deallocate first, and SHOULD pre-empt this by disabling Reverse (with an explanatory tooltip) when `allocatedAmount > 0` rather than letting the 409 be the first signal. The same code exists on Cancel but is unreachable there (a `Draft` can never be allocated) — the UI MUST still map it.
13. **Eight RBAC permissions MUST be seeded manually in auth-service (`CHG-ENH-003`) or EVERY screen in this spec answers `403`.** The seven `finance.payment:read|create|confirm|post|cancel|reverse|allocate` plus `finance.aging:read`. SDD-INT-AUTH-001 permission auto-registration is still deferred and no artifact in this repository can discharge the seeding action. The UI therefore MUST have a sane forbidden/empty state (§2.17) — a 403 on the aging report MUST NOT render as a blank white page or a raw status.
14. **No Finance backend runs in-browser today.** There is no service daemon and `docker-compose.finance.yml` was only just made composable, so a live end-to-end golden path is not verifiable during Phase 7 as things stand. The `[UI]` checks in §7.2 are therefore split into (a) verifiable OFFLINE against the SPA alone and (b) requiring a LIVE stack — the latter MUST be recorded as deferred rather than reported as passing.
15. **The aging report has NO paging and NO `FilterRequest`.** `GET /api/v1/aging` returns one `AgingReportDto` carrying **all** rows. This is the only list-like surface in the app where client-side rendering of the full set is correct; the §2.1 "never page client-side" rule does NOT apply to it, and the UI MUST NOT invent a `page`/`pageSize` for it. It MUST, however, be prepared for an unbounded row count (§1.6 gap 8).
16. **Bucket labels are server-generated DATA, not i18n keys.** `bucketLabels` / `AgingBucketAmountDto.label` come back as `"Current"`, `"1-30"`, `"31-60"`, `"61-90"`, `"90+"` — and the boundaries are configurable, so the column set is DYNAMIC. The UI MUST render columns from `bucketLabels` and MUST NOT hard-code five columns. Only the `"Current"` label SHOULD be translated (via `aging.bucket_Current`); the numeric range labels render verbatim.

### 1.5 Scope — covered (v1)

- **Payments list**: filter / sort / paginate / free-text search via the SDD-INFRA-005 `FilterRequest` → `PagedResult<PaymentDto>` contract (`PageSize ≤ 200`), default order `PaymentDate` descending.
- **View detail** of one payment (header, amounts, FX, settlement account, allocation figures, lifecycle timestamps).
- **Create draft**: `DocumentType`, `Method`, counterparty, currency, amount, exchange rate, settlement account, payment date, optional bank reference.
- **Edit draft** (`Draft` only, with `rowVersion` optimistic concurrency).
- **Delete draft** (`Draft` only, destructive confirm).
- **Confirm** a draft (server assigns the gapless `RCT`/`PAY` number).
- **Post** a confirmed payment, including the **posting-pending** presentation and the fact that re-driving Post **re-enqueues the confirm event** as the documented recovery path.
- **Cancel** — **`Draft` ONLY**, reason required.
- **Reverse** — **`Posted` only**, reason required, blocked while allocated.
- **Allocation**: list a payment's allocation rows (paged, invoice-enriched, with derived `SettlementStatus` and the informational `RealizedFxDifference`); allocate against open items with an EXPLICIT invoice + amount list (all-or-nothing); deallocate one row. All ten invariant codes surfaced meaningfully (§4).
- **Open items** worklist: oldest-due-first, with `asOfDate` / `direction` / `counterparty` / `currency` / `overdueOnly` narrowings, outstanding + base outstanding, days past due, and bucket label.
- **Aging report**: bucketed per (counterparty, currency) with configurable boundaries, per-bucket report totals in base currency, grand total, open-item count.
- **Counterparty balances**: paged per (counterparty, currency) roll-up with outstanding, overdue subset, open-item count, oldest due date.
- Cross-cutting: ledger theme + density (SDD-UI-001), EN+BG i18n parity, `X-Correlation-ID` on every request (SDD-INFRA-001), currency dropdown via `useNomenclature()` (SDD-NOM-001), ProblemDetails error mapping via `getApiErrorMessage`, a sane forbidden/empty state for the un-seeded-permission case (§2.17).

### 1.6 Scope — excluded / deferred, and what the shipped backend does NOT support

The following are things a payments UI would normally want and **cannot** have in v1. They are reported here, not designed around silently. None of them may be worked around by adding backend behavior in Phase 6.

1. **Counterparty NAMES.** Every counterparty is an opaque `Guid` on `PaymentDto`, `OpenItemDto`, `AgingRowDto`, and `CounterpartyBalanceDto`; there is no enrichment endpoint (`SDD-INT-WH-002` deferred). The UI MUST render the raw GUID in the mono face. This materially degrades the aging report and counterparty balances, which are fundamentally "who owes me" reports — v1 ships them GUID-keyed. **This is the largest usability gap in this spec.**
2. **A cash/bank-account picker.** `SettlementAccountId` is an `int` reference into `Finance.Accounts.API`. There is no "list settlement/cash accounts" endpoint and no `IsPostable`/`IsCash`/`IsHeader` flag on the Chart of Accounts (`CHG-ENH-002` dormant, SDD-PAY-001 §2.8), so the picker can only offer the WHOLE chart of accounts from `GET /api/v1/accounts` and cannot pre-filter to cash/bank accounts. The operator will see non-cash accounts and learn of the mistake only from `PAYMENT_SETTLEMENT_ACCOUNT_NOT_FOUND` / `PAYMENT_SETTLEMENT_ACCOUNT_INACTIVE` at create/confirm.
3. **The base currency is not readable before a payment exists.** `ExchangeRate` MUST equal exactly `1.000000` when `CurrencyCode == BaseCurrencyCode` (SDD-PAY-001 §2.8), but `BaseCurrencyCode` is only exposed *on responses* (`PaymentDto.baseCurrencyCode`, `AgingReportDto.baseCurrencyCode`) — there is no `GET` that returns the country strategy's base currency for a blank form. The client therefore **cannot** enforce the rate-equals-one rule up front; it MUST rely on the server's `INVALID_PAYMENT_EXCHANGE_RATE`. The UI MAY infer the base currency from any already-loaded response and, when it has it, MAY apply the rule as a soft hint — never as the authority.
4. **No automatic FX rate resolution.** v1 takes the rate on the request (`SDD-FIN-005` unauthored). The UI MAY prefill from the already-shipped read-only `GET /api/v1/exchange-rates/latest?currency=&date=` (SDD-UI-001 §2.10), but nothing server-side validates the submitted rate against a published BNB rate.
5. **No automatic / FIFO / oldest-due-first allocation.** `AllocatePaymentRequest.Items` is required and an empty list is rejected with `PAYMENT_ALLOCATION_ITEMS_REQUIRED` — it is explicitly never read as "apply the whole payment" (SDD-PAY-002 §2.4). An "apply to oldest first" button would have to compose the item list purely client-side; v1 does NOT ship one.
6. **No in-place amendment of an allocation amount.** A wrong amount is corrected by deallocating and re-allocating (SDD-PAY-002 §2.6). The allocations table MUST NOT offer an inline amount edit.
7. **The open-item picker cannot exclude invoices already allocated to THIS payment.** `GET /api/v1/open-items` has no "exclude allocated by payment X" narrowing, and an already-matched invoice keeps a positive `outstanding` for partial matches. The UI MUST cross-reference the payment's own allocations list client-side to avoid a guaranteed `PAYMENT_ALLOCATION_DUPLICATE`; that costs a second request.
8. **The aging report is unbounded.** `GET /api/v1/aging` accepts no `FilterRequest` and returns every in-scope (counterparty, currency) row in one payload. A counterparty-heavy tenant produces a large response with no server-side cap. The UI MUST render it as one table (§1.4 trap 15) and SHOULD narrow by counterparty/currency to keep it tractable.
9. **`GET /api/v1/counterparty-balances` has no counterparty narrowing** (deliberately, SDD-PAY-003 §2.7) — one counterparty's detail is read through `/open-items?counterpartyId=`. The balances page cannot answer "show me just this customer".
10. **The balances page cannot offer column sorting.** Its rows are GROUPED, so no `[Sortable]` entity surface applies; the server orders by `BaseOutstanding` desc then `(CounterpartyId, CurrencyCode)`. The grid MUST NOT expose user sorting on that table.
11. **The open-items list has no free-text search.** `InvoiceOpenItem` declares NO `[Searchable]` property (only `Payment.DocumentNumber` is `[Searchable]` in this service), so `search` has nothing to match. The open-items page MUST NOT show a `FilterBar` search box.
12. **No payment status-history / audit timeline.** `payments.PaymentStatusHistory` rows exist in the database but have no read endpoint, and the audit read endpoint (`GET /api/v1/audit/export`) is deferred in SDD-AUDIT-001. The UI cannot show a lifecycle timeline or "who confirmed/cancelled/reversed and why". The stored `CancellationReason` IS exposed on `PaymentDto` and MAY be shown; the reverse reason is NOT.
13. **A `Reversed` payment cannot be linked to its offsetting entry.** `ReversalJournalEntryId` was deliberately not added (SDD-PAY-001 §2.7); only the ORIGINAL `journalEntryId` is on the DTO. There is also no journal-entry DETAIL route in the SPA (`/journal-entries` is a dialog-mode list), so even `journalEntryId` can only be displayed as a mono code, not deep-linked.
14. **No refunds and no compensation/offset settlement.** `PaymentDocumentType` is two-valued and `PaymentMethod` three-valued; both would be breaking enum changes (SDD-PAY-001 §5). A credit note therefore cannot be settled at all (`SettlementPairing` admits `CustomerReceipt → {SaleInvoice, DebitNote}` and `SupplierPayment → {PurchaseInvoice}` only), which is also why a confirmed credit note NEVER appears in open items or aging — by design, not lag (SDD-PAY-003 §2.10).
15. **No bank statement import / reconciliation and no payment-file (SEPA / ISO 20022) generation.** A later phase.
16. **No aging export** (CSV / XLSX / PDF) and no printable receipt/payment advice. No endpoint exists.
17. **No bulk operations** (multi-select confirm/post/allocate) and **no approval / maker-checker** state between `Draft` and `Confirmed`.
18. **The GL is currency-naive and the realized FX difference is not posted.** `RealizedFxDifference` is computed and stored per allocation row but `IRealizedFxHandler` is wired to the inert `NoOpRealizedFxHandler`; the posting waits on `SDD-FIN-005` (SDD-PAY-002 §2.9). The UI MAY display the figure but MUST label it informational and MUST NOT present it as a posted ledger amount.
19. **`OpenItemDto.baseOutstanding` is at the invoice's FROZEN booking rate**, not a current revaluation (SDD-PAY-003 §2.2). The UI MUST label base-currency columns as booking-rate figures and MUST NOT imply a live conversion.
20. **Page-mode forms** (`SDD-UI-002`) and the per-aggregate **audit-log panel** remain out of scope (SDD-UI-001 §7).

## 2. Behavior

> All rules build on SDD-UI-001 (shell). Each rule below is independently testable via Vitest (component/unit) and/or `ui-validate` (live SPA). Every backend call MUST go through the shared axios instance; every failure MUST be surfaced via `notification.error(getApiErrorMessage(err, t))` — never `err.message`, `err.response.status`, or raw `data.detail`.

### 2.1 Payments list (MUST) — `GET /api/v1/payments`

- The Payments list MUST consume `GET /api/v1/payments` as a `PagedResult<PaymentDto>` envelope `{ items, totalCount, page, pageSize }` and MUST send `FilterRequest` query params (filters / sort / page / pageSize / search) through `toFilterParams`. Server-side paging is authoritative; the grid MUST NOT page client-side (SDD-INFRA-005).
- The grid MUST request `pageSize ≤ 200` (`MAX_PAGE_SIZE`); a `PAGE_SIZE_TOO_LARGE` from the server MUST still surface via the error helper.
- Columns MUST include: document number (mono tabular, `—` while `Draft` or `Cancelled` — §1.4 traps 4/5), document type, direction (`AP`/`AR`), method, counterparty (mono GUID), currency (mono), amount (mono tabular, right-aligned, with currency), payment date, status (a `StatusDot` tone), and allocation state. Money / rate / code / date cells MUST render in the mono **tabular** face and be right-aligned where numeric (SDD-UI-001 §2.8).
- **Allocation state MUST be visible on the row.** The list MUST show `allocatedAmount` and/or `unallocatedAmount` (both are on `PaymentDto`) so an operator can see unapplied cash without opening the payment. A payment with `unallocatedAmount > 0` SHOULD carry a quiet "unapplied" affordance.
- The default sort MUST be `PaymentDate` descending (mirroring SDD-PAY-001 §2.11); the grid MUST send the corresponding `sort` term.
- Filter/sort MUST only target the backend opt-in surface, which is CLOSED (SDD-PAY-001 §2.11, pinned by `PaymentFilterSurfaceTests`): sortable + filterable = `DocumentNumber`, `DocumentType`, `Direction`, `Method`, `Status`, `CurrencyCode`, `Amount`, `SettlementAccountId`, `PaymentDate`, `CreatedAt`; **`CounterpartyId` is `[Filterable]` only** (sorting a page by an opaque GUID has no user meaning) so its column MUST be `sortable: false`; **`DocumentNumber` is the sole `[Searchable]` property**, so the `FilterBar` search box drives `FilterRequest.search` against the document number only and its placeholder MUST say so. The UI MUST NOT offer a sort or filter on any property outside this list.
- The list MUST respect the density store and MUST NOT hard-code `size="small"` or fixed padding (SDD-UI-001 §2.4).
- An empty result MUST render the editorial empty state with a single quiet "new payment" action (SDD-UI-001 §2.8), NOT an error.
- `Confirmed`-but-unlinked payments MUST be listed like any other row (SDD-PAY-001 §2.11) — the pending post is observable, never hidden or filtered out.

### 2.2 View detail (MUST) — `GET /api/v1/payments/{id}`

- Opening a payment MUST read `GET /api/v1/payments/{id}` and render: document number (once assigned), document type, direction, method, status, counterparty, currency + amount, exchange rate (mono, six-decimal semantics), base currency + base amount, settlement account id, payment date, bank reference, `allocatedAmount` / `unallocatedAmount`, `journalEntryId` (mono, display-only — §1.6 gap 13), `cancellationReason` when present, and the `createdAt` / `confirmedAt` / `postedAt` / `reversedAt` timestamps.
- The detail MUST display the **server's** `baseAmount` and `documentNumber`; it MUST NOT recompute or override them for display (the rounding and the number format are owned by `ICountryStrategy`, SDD-CTRY-001).
- A `404` (`PAYMENT_NOT_FOUND`) MUST surface a translated message via the error helper, not a raw status.
- The detail MUST NOT attempt to show a status history or an audit trail — neither is exposed (§1.6 gap 12).

### 2.3 Create draft (MUST) — `POST /api/v1/payments` (`finance.payment:create`)

- The "New payment" action MUST open `PaymentFormDialog` in create mode and submit `POST /api/v1/payments` with `DocumentType`, `Method`, `CounterpartyId`, `CurrencyCode`, `Amount`, `ExchangeRate`, `SettlementAccountId`, `PaymentDate`, and the optional `BankReference`.
- The form MUST NOT send `Direction`, `BaseCurrencyCode`, or `BaseAmount` — all three are server-derived and a client value is ignored (`CreatePaymentRequest` does not declare them). The dialog MUST DISPLAY the derived direction as read-only feedback (`CustomerReceipt → AR`, `SupplierPayment → AP`) so the operator sees the consequence of the type choice.
- The currency selector MUST load options via `useNomenclature()` (SDD-NOM-001) — options MUST NOT be hard-coded. The settlement-account selector MUST load the Chart of Accounts from `GET /api/v1/accounts`; it MUST NOT hard-code account ids, and the spec acknowledges it cannot pre-filter to cash/bank accounts (§1.6 gap 2).
- The dialog MAY show a client-side **preview** of `baseAmount = amount × exchangeRate` rounded to two decimals for immediate feedback, but MUST treat the server's persisted `baseAmount` as authoritative and MUST re-display it after save. The UI MUST NOT claim its preview is the legal base amount.
- On success (**`201 CreatedAtAction`**) the dialog MUST close, the payments list cache MUST invalidate, and a success toast MUST show. On failure the dialog MUST stay open and the mapped error toast MUST show (mirroring the invoice mutation pattern, which resolves to `null` on failure).
- The created payment MUST appear in the list as `Draft` with NO document number (§1.4 trap 4) and `allocatedAmount = 0.00`.

### 2.4 Status gating of actions (MUST)

- Row/detail actions MUST be gated by the payment **status** so the UI never offers an illegal transition. The exact sets are §1.4 trap 2 and are restated as the normative rule here:
  - **`Draft`** → expose Edit, Confirm, Cancel, Delete. MUST NOT expose Post, Reverse, or Allocate.
  - **`Confirmed`** → expose Post (or the "posting…" affordance, §2.7) and Allocate/Deallocate. **MUST NOT expose Cancel**, Edit, Delete, or Reverse.
  - **`Posted`** → expose Reverse and Allocate/Deallocate. MUST NOT expose Cancel, Edit, Delete, Post.
  - **`Cancelled`** / **`Reversed`** → expose no mutating action (terminal). Existing allocation rows MUST render read-only.
- **Reverse MUST additionally be disabled when `allocatedAmount > 0`**, with a tooltip pointing at deallocation (§1.4 trap 12); the server's `PAYMENT_HAS_ALLOCATIONS` (409) MUST still be mapped as a defensive path.
- The backend `[RequirePermission("finance.payment:<action>")]` / `[RequirePermission("finance.aging:read")]` is the authoritative gate (SDD-INT-AUTH-001). A `403` on any action MUST surface the translated forbidden message via `getApiErrorMessage`, never a raw status.
- Because no client-side permission hook exists today, hiding a control by the caller's permission set is OPTIONAL: the UI SHOULD remain functional and rely on the backend 403 + error toast; it MAY hide actions if a permission hook is later introduced (no such hook is in scope for v1).

### 2.5 Edit draft (MUST) — `PUT /api/v1/payments/{id}` (`finance.payment:create`)

- Edit MUST be offered ONLY for `Draft` payments (§2.4). The dialog MUST submit `PUT /api/v1/payments/{id}` carrying every editable field plus the `PaymentDto.rowVersion` (base64) for optimistic concurrency.
- **`DocumentType` MUST be sent unchanged and MUST be rendered read-only in edit mode.** `UpdatePaymentRequest` carries it precisely so the server can reject a change (it drives `Direction`, the sequence key, and the posting rule); a differing value yields `INVALID_PAYMENT_DOCUMENT_TYPE` (400).
- A stale or malformed `rowVersion` MUST surface `CONCURRENT_MODIFICATION` (409) via the error helper.
- Attempting to edit a non-`Draft` payment MUST NOT be reachable from the UI; if the backend rejects with `PAYMENT_POSTED_IMMUTABLE` (409) because the status changed underneath, that code MUST surface via the error helper.
- After save the dialog MUST re-display the server-recomputed `baseAmount` (not the client preview).

### 2.6 Delete draft (MUST) — `DELETE /api/v1/payments/{id}` (`finance.payment:create`)

- Delete MUST be offered for `Draft` only, behind a destructive `ConfirmDialog`. The copy MUST make clear this is a hard delete of a document that never held a number (as opposed to Cancel, which keeps the row for audit).
- Attempting to delete a non-`Draft` payment MUST surface `PAYMENT_POSTED_IMMUTABLE` via the error helper.

### 2.7 Confirm, Post, and posting-pending (MUST)

- **Confirm** — `POST /api/v1/payments/{id}/confirm` (`finance.payment:confirm`), body `{ rowVersion }`: offered for `Draft` only. On success the payment moves to `Confirmed` and the server-assigned gapless **document number** MUST now be displayed (it was `—`). The format (`RCT-{yyyy}-{nnnnnn}` / `PAY-{yyyy}-{nnnnnn}`) is owned by `ICountryStrategy` server-side; the UI MUST only display it, in the mono face.
- Confirm failures MUST all surface via the error helper: `PAYMENT_NOT_DRAFT` (409), `PAYMENT_DUPLICATE_DOCUMENT_NUMBER` (409), `PAYMENT_PERIOD_CLOSED` (409), **`PAYMENT_DATE_YEAR_MISMATCH`** (409 — the payment date's year differs from the confirm-clock year, so no number may be drawn), `PAYMENT_SETTLEMENT_ACCOUNT_NOT_FOUND` (404), `PAYMENT_SETTLEMENT_ACCOUNT_INACTIVE` (409), `PAYMENT_BASE_AMOUNT_MISMATCH` (400), `CONCURRENT_MODIFICATION` (409).
  - `PAYMENT_DATE_YEAR_MISMATCH` deserves an explanatory message, not a bare code: the operator's fix is to re-date the payment into the current year or to have the prior year's series handled by accounting, and the message SHOULD say so.
- **Post** — `POST /api/v1/payments/{id}/post` (`finance.payment:post`), body `{ rowVersion }`: offered for `Confirmed`. It NEVER posts a journal entry itself; it is the operator-driven completion/visibility seam for the asynchronous Confirm→Post handshake (SDD-PAY-001 §2.5).
- **Posting-pending UX (MUST).** A `Confirmed` payment whose `journalEntryId` is `null` (the Journal-service back-event has not arrived) MUST be presented as **"posting…"** — a pending/in-progress affordance derived for display only, exactly as `features/invoices/types.ts` derives `POSTING_PENDING` via `displayStatusKey`. `Posting` MUST NOT be added as a backend status value.
- **Pressing Post on a `Confirmed`-and-unlinked payment answers `PAYMENT_POSTING_PENDING` (409) AND re-enqueues `PaymentConfirmedEvent`** as the documented recovery path. The UI MUST therefore:
  - present the outcome as **informational / progress**, not as a destructive failure (§1.4 trap 6);
  - state that a retry has been queued;
  - keep the payment visible and the Post action available (repeated retries are bounded in effect — each adds one outbox message and at most one back-event, never a second journal entry);
  - refetch (TanStack Query invalidation and/or a bounded poll) so the transition to `Posted` is observed when the back-event lands.
- Once `journalEntryId` is linked the payment MUST present as `Posted`. The UI MUST NOT assume immediate synchronous posting and MUST NOT treat a long-lived `Confirmed` state as corruption.
- `PAYMENT_NOT_CONFIRMED` (409) MUST be surfaced distinctly from `PAYMENT_POSTING_PENDING`: the former is a wrong-state post, the latter is a pending one. The two codes exist precisely so they are distinguishable, and the UI MUST NOT collapse them into one message.

### 2.8 Cancel — `Draft` ONLY (MUST) — `POST /api/v1/payments/{id}/cancel` (`finance.payment:cancel`)

- Cancel MUST be offered for **`Draft` only**. The UI MUST NOT render a Cancel action on a `Confirmed`, `Posted`, `Cancelled`, or `Reversed` payment (§1.4 trap 3).
- It MUST open a **reason-prompt** dialog (mirroring `CancelInvoiceDialog` / the shared `ReasonPromptDialog`) requiring a **non-empty, non-whitespace** `Reason`; submit MUST be disabled until a reason is entered. The body MUST be `{ reason, rowVersion }`.
- An empty reason that reaches the server MUST surface `PAYMENT_CANCEL_REASON_REQUIRED` (400). Cancelling a non-`Draft` payment (not reachable from the UI, but defensively) MUST surface `INVALID_PAYMENT_STATE_TRANSITION` (409) via the error helper, and the message SHOULD point the operator at **reversal** as the correct correction for a confirmed-or-later payment.
- A `Cancelled` payment MUST continue to render `—` for its document number (it never held one — §1.4 trap 5). The UI MUST NOT display a placeholder number, and MUST NOT port SDD-UI-FIN-001 §2.7's "keep the number on display" rule.
- `PAYMENT_HAS_ALLOCATIONS` (409) MUST be mapped on this path as defense-in-depth even though it is unreachable (a `Draft` can never be allocated).

### 2.9 Reverse — `Posted` only (MUST) — `POST /api/v1/payments/{id}/reverse` (`finance.payment:reverse`)

- Reverse MUST be offered for **`Posted` only**, and MUST be disabled when `allocatedAmount > 0` (§2.4).
- It MUST open a reason-prompt dialog requiring a non-empty `Reason`; the body MUST be `{ reason, rowVersion }`. An empty reason reaching the server MUST surface `PAYMENT_REVERSE_REASON_REQUIRED` (400).
- The dialog copy MUST explain that reversal produces a **sign-flipped journal entry** and that nothing on the payment header, amount, or document number changes — the payment is flagged `Reversed` and keeps its number. The UI MUST NOT present reversal as an edit or a deletion.
- `PAYMENT_PERIOD_CLOSED` (409) on reverse MUST be surfaced with an explanatory message: the reversing entry keeps the ORIGINAL entry date, so a closed original period is a hard block until it is reopened (SDD-PAY-001 §2.7). "Try again later" would be wrong copy.
- `INVALID_PAYMENT_STATE_TRANSITION` (409) and `PAYMENT_HAS_ALLOCATIONS` (409) MUST both be mapped.
- After a successful reverse the row MUST render as `Reversed` (terminal, no actions). The UI MUST NOT attempt to link to the offsetting entry (§1.6 gap 13).

### 2.10 List allocations (MUST) — `GET /api/v1/payments/{paymentId}/allocations` (`finance.payment:read`)

- The allocation panel MUST consume `GET /api/v1/payments/{paymentId}/allocations` as `PagedResult<PaymentAllocationDto>` with a `FilterRequest` (`pageSize ≤ 200`), default order `AllocatedAt` descending.
- Filter/sort MUST target only the opt-in surface on `PaymentAllocation`: `InvoiceId` (`[Filterable]` only → column `sortable: false`), `AllocatedAmount` (filterable + sortable), `AllocatedAt` (filterable + sortable). No `[Searchable]` property exists, so the panel MUST NOT offer a free-text search box.
- Columns MUST include: invoice document number (mono, joined from the local projection — MAY be `null`), invoice due date, allocated amount (mono tabular, in the payment currency), base allocated amount (mono, booking-rate figure), the invoice's mirrored `invoiceStatus` (a **string** on the wire), the derived `invoiceSettlementStatus` (a **numeric** `SettlementStatus`, 1–3), `realizedFxDifference`, and `allocatedAt`.
- `realizedFxDifference` MUST be labelled **informational** and MUST NOT be presented as a posted GL amount (§1.6 gap 18). A zero value MUST render as `0.00`, not blank.
- `invoiceSettlementStatus` MUST be rendered from the numeric enum via an i18n label (`allocations.settlement_Unsettled` / `_PartiallySettled` / `_Settled`) and MUST NOT be re-derived client-side from `settledAmount` vs `grossTotal` — the server owns the single `SettlementStatusCalculator` (SDD-PAY-002 §2.8).
- An unknown payment MUST surface `PAYMENT_NOT_FOUND` (404). **A payment with no allocations MUST render an empty state with a quiet "allocate" action — never an error** (an unallocated payment is a normal business state).

### 2.11 Allocate (MUST) — `POST /api/v1/payments/{paymentId}/allocations` (`finance.payment:allocate`)

- The allocate flow MUST be offered only for `Confirmed`/`Posted` payments (§2.4) and MUST present an **open-item picker** followed by an explicit per-invoice amount entry, submitting `{ items: [{ invoiceId, allocatedAmount }], rowVersion }`.
- The picker MUST source candidates from `GET /api/v1/open-items` **pre-narrowed to the payment's own `counterpartyId`, `currencyCode`, and `direction`** (the string form `"AR"`/`"AP"` derived from the numeric `PaymentDto.direction`). Pre-narrowing is what turns four of the ten invariant codes from user-facing errors into unreachable defenses.
- The picker MUST exclude invoices already allocated to THIS payment by cross-referencing the payment's own allocations list (§1.6 gap 7), so `PAYMENT_ALLOCATION_DUPLICATE` is not a routine outcome.
- The picker MUST default each amount to `min(item.outstanding, payment.unallocatedAmount)` and MUST show the running total against `unallocatedAmount` so the operator can see an over-allocation before submitting.
- **Client-side pre-checks (MUST, all mirroring server invariants, none authoritative):** each amount `> 0`, at most **two decimal places** (`decimal.Round(amount, 2) == amount` server-side, so a fraction of a cent MUST be blocked client-side), `Σ items ≤ unallocatedAmount`, and per invoice `amount ≤ outstanding`. Submit MUST be disabled while any of these fails.
- **The call is ALL-OR-NOTHING.** On failure the UI MUST NOT report partial success, MUST NOT optimistically decrement any figure, and MUST leave the dialog open with the mapped error toast.
- On success (**`200`, no `Location` — §1.4 trap 10**) the UI MUST consume `AllocatePaymentResultDto` directly: render the created rows, update `allocatedAmount` / `unallocatedAmount`, **re-seed the payment `rowVersion` from the response** (§1.4 trap 11), and apply each `affectedInvoices[].settlementStatus`. It MUST invalidate the payments list, the allocations list, and the open-items / aging caches, and MUST NOT need a follow-up read to learn the new state.
- **All ten invariant codes MUST be surfaced meaningfully**, i.e. with a message that tells the operator what to change — not a generic "allocation failed". §4 fixes the required treatment per code. In particular:
  - `PAYMENT_ALLOCATION_EXCEEDS_PAYMENT` and `PAYMENT_ALLOCATION_EXCEEDS_OUTSTANDING` MUST name which bound was breached (the payment's unallocated amount vs the invoice's outstanding) — they are different operator fixes.
  - `PAYMENT_ALLOCATION_INVOICE_NOT_FOUND` (404) MUST be phrased as "not available for matching yet" and SHOULD suggest a retry, because the projection is eventually consistent — but MUST NOT claim it will definitely appear, since a credit note is absent permanently by design.
  - `PAYMENT_ALLOCATION_INVOICE_NOT_ELIGIBLE`, `_DIRECTION_MISMATCH`, `_COUNTERPARTY_MISMATCH`, `_CURRENCY_MISMATCH` MUST each get their own message; pre-narrowing (above) should make them rare, but they are reachable when the projection changes under the operator.
  - `PAYMENT_ALLOCATION_CONTROL_ACCOUNT_MISMATCH` is unreachable through the v1 paths and retained as defense-in-depth; it MUST still be mapped so it can never render as a raw code.
  - `PAYMENT_NOT_ALLOCATABLE` (409) MUST be gated away by §2.4 and mapped defensively.

### 2.12 Deallocate (MUST) — `DELETE /api/v1/payments/{paymentId}/allocations/{allocationId}` (`finance.payment:allocate`)

- Deallocate MUST sit behind a `ConfirmDialog` on the allocation row and MUST send `rowVersion` and the optional `reason` as **QUERY parameters** (§1.4 trap 9) — never a request body.
- The copy MUST state that releasing a match posts nothing, reverses nothing, and leaves the payment's status untouched; and that v1 has no in-place amount amendment, so a wrong amount is corrected by releasing and re-allocating (§1.6 gap 6).
- On success the UI MUST consume `DeallocatePaymentResultDto`: apply `releasedAmount`, the new `allocatedAmount` / `unallocatedAmount`, the `affectedInvoice` settlement state, and **re-seed the payment `rowVersion`**. It MUST invalidate the same caches as §2.11.
- `PAYMENT_ALLOCATION_NOT_FOUND` (404) MUST be surfaced when the row is unknown *or belongs to another payment* (the lookup is scoped by `(paymentId, allocationId)`); the message MUST NOT imply the row was deleted by someone else. `PAYMENT_NOT_ALLOCATABLE` (409) and `CONCURRENT_MODIFICATION` (409) MUST be mapped.

### 2.13 Open items worklist (MUST) — `GET /api/v1/open-items` (`finance.payment:read`)

- The open-items page MUST consume `GET /api/v1/open-items` as `PagedResult<OpenItemDto>` and MUST merge `toFilterParams(request)` with the narrowing params `asOfDate`, `direction`, `counterpartyId`, `currencyCode`, `overdueOnly` (§1.4 trap 8).
- Every narrowing is OPTIONAL: `asOfDate` defaults to the current date server-side, and an omitted direction / counterparty / currency widens the list. `direction` MUST be sent as the string `"AR"` / `"AP"`.
- The default order MUST be oldest-due-first (`DueDate` ascending) so the page reads as a collection worklist; `pageSize ≤ 200`.
- Filter/sort MUST target only the `InvoiceOpenItem` opt-in surface: `DocumentNumber`, `DocumentType`, `Direction`, `CounterpartyId` (filterable only), `CurrencyCode`, `IssueDate`, `DueDate`, `InvoiceStatus`. **No `[Searchable]` property exists, so the page MUST NOT show a search box** (§1.6 gap 11).
- Columns MUST include: invoice document number (mono), document type (**string**), direction (**string**), counterparty (mono GUID), currency (mono), gross total, settled amount, `outstanding`, `baseOutstanding` (labelled a booking-rate figure), issue date, due date, `daysPastDue`, `agingBucket` (a server label), `settlementStatus` (**numeric**), `invoiceStatus` (**string**, always `Confirmed` or `Posted`).
- `daysPastDue` of `0` or less MUST render as "not yet due" rather than a negative number, and MUST correspond to the `Current` bucket.
- The page MUST document — in visible, translated help text — that the projection is **eventually consistent**: a very recently confirmed invoice may be missing and a very recently cancelled/reversed one may still appear, and a confirmed **credit note is absent permanently by design** (SDD-PAY-003 §2.8/§2.10). This MUST NOT be presented as an error state.
- An empty window MUST render the editorial empty state with `200` semantics — never an error, and never a `404` message.
- A future `asOfDate` MUST be blocked client-side (§3.3) and, if it reaches the server, MUST surface `INVALID_AGING_AS_OF_DATE` (400).

### 2.14 Aging report (MUST) — `GET /api/v1/aging` (`finance.aging:read`)

- The aging page MUST consume `GET /api/v1/aging` and render the single `AgingReportDto`. It accepts **no `FilterRequest`** and returns **all** rows in one payload; the UI MUST render one table and MUST NOT invent paging for it (§1.4 trap 15).
- `asOfDate` and `direction` are **REQUIRED**; `counterpartyId`, `currencyCode`, and `buckets` are optional. `direction` MUST be sent as `"AR"` / `"AP"`.
- **`buckets` MUST be serialized as REPEATED query values** — `?buckets=30&buckets=60&buckets=90` (§1.4 trap 7). The default axios array form `buckets[]=30` will NOT bind, and a comma-separated form is unsupported by the shipped binder. The UI MUST NOT send `buckets` at all when the operator has not customized them (the server then applies the documented default `30, 60, 90`).
- The bucket columns MUST be built dynamically from the response's `bucketLabels` (with `bucketDayBoundaries` available for tooltips, and each `AgingBucketAmountDto` carrying its own `fromDaysPastDue` / `toDaysPastDue`). The UI MUST NOT hard-code five columns and MUST NOT re-derive boundaries or labels (§1.4 trap 16).
- Each row MUST render its (counterparty, currency) key, `openItemCount`, the per-bucket `outstanding` (and `baseOutstanding`), `totalOutstanding`, and `totalBaseOutstanding`, all mono tabular and right-aligned.
- **Only base-currency figures may be summed across rows.** The report-level `totals` are base-currency only, by design. The UI MUST NOT compute or display a cross-currency transactional total, and MUST label `baseCurrencyCode` on the totals row.
- A counterparty with `0.00` in-scope outstanding is omitted by the server; the UI MUST NOT synthesize zero rows. An empty report MUST render empty rows + zero totals with an editorial empty state — never a `404` message.
- The report is **period-status-agnostic** (a closed period's invoices are still aged) and **invoice-only** (unallocated payment cash is NOT netted in, so no balance is ever negative). Both MUST be stated in visible translated help text so the numbers are not misread.
- A row MUST offer a drill-down into `/open-items` pre-narrowed to that row's `counterpartyId` + `currencyCode` + the report's `direction` and `asOfDate` (the endpoint supports exactly these narrowings).
- Bucket customization MUST be validated client-side before the request (§3.3): at most **six** boundaries, strictly ascending, strictly positive. A violation reaching the server MUST surface `INVALID_AGING_BUCKETS` (400).

### 2.15 Counterparty balances (MUST) — `GET /api/v1/counterparty-balances` (`finance.aging:read`)

- The balances table MUST consume `GET /api/v1/counterparty-balances` as `PagedResult<CounterpartyBalanceDto>`, merging `toFilterParams(request)` with the required `asOfDate` + `direction` and the optional `currencyCode`. `pageSize ≤ 200`.
- Because both this and the aging report require `finance.aging:read` and share the same as-of / direction / currency inputs, they SHOULD live on ONE `/aging` route driven by one shared control bar — mirroring the shipped `ExchangeRatesPage`, which hosts a latest view and a range view on one page. This is a SHOULD; two routes are acceptable if the shared control bar is preserved.
- Columns MUST include: counterparty (mono GUID), currency (mono), `openItemCount`, `outstanding`, `baseOutstanding`, `overdueOutstanding`, `baseOverdueOutstanding`, and `oldestDueDate` (which MAY be `null`).
- **The grid MUST NOT expose user sorting** on this table: the rows are grouped and the server orders by `baseOutstanding` descending then the composite grouping key (§1.6 gap 10). There is **no counterparty narrowing** (§1.6 gap 9) — single-counterparty detail is reached through the `/open-items` drill-down.
- The UI MUST state that `overdueOutstanding` is exactly the sum of the non-`Current` buckets and that `outstanding` equals the aging report's total for the same pair / date / direction (both endpoints share one aggregation path). It MUST NOT recompute either figure.
- A counterparty with zero outstanding is omitted and is not counted in `totalCount`; an unknown counterparty simply yields an empty page with `200`. Neither is an error.

### 2.16 Cross-cutting: correlation, density, i18n (MUST)

- Every outbound request from this feature MUST carry a fresh `X-Correlation-ID` via the shared axios interceptor (SDD-INFRA-001 / SDD-UI-001 §2.2); the feature MUST NOT instantiate raw `axios` or call `fetch`.
- Every visible string MUST come from `t('payments.*')` / `t('allocations.*')` / `t('openItems.*')` / `t('aging.*')` / `t('balances.*')` / `t('errors.*')` / shared keys, present in BOTH `en.ts` and `bg.ts` (§5).
- Every grid / dialog / table / field MUST read density from `useLayoutStore` (SDD-UI-001 §2.4). No hard-coded `size="small"` and no fixed padding that ignores the store.
- Routes MUST be registered in `frontend/src/app/App.tsx` (`payments`, `open-items`, `aging`) and sidebar entries in `frontend/src/components/templates/AppShell.tsx` (`nav.payments`, `nav.openItems`, `nav.aging`), following the shipped `navItems` array shape.
- Nothing on this surface MAY be cached beyond TanStack Query's in-flight/short-lived client cache: payments, allocations, open items, aging, and balances are transactional data (SDD-INFRA-004 / SDD-PAY-001 §2.12 / SDD-PAY-003 §2.8). The UI MUST invalidate after every write and MUST NOT persist any of it to `localStorage`.

### 2.17 Forbidden / un-seeded-permission state (MUST)

- Because the eight Payments permissions MUST be seeded manually in auth-service (`CHG-ENH-003`) and that action cannot be discharged from this repository, **every screen in this spec MAY answer `403` for every caller on day one** (§1.4 trap 13).
- A `403` on a LIST/READ request MUST render an **editorial forbidden state** — a translated "you do not have permission to view this" panel with no retry loop — and MUST NOT render a blank page, an infinite spinner, a raw status, or a red crash toast on every route change.
- A `403` on an ACTION MUST surface the translated forbidden message through `getApiErrorMessage` and MUST leave the dialog open.
- The forbidden state MUST distinguish the aging surfaces: because `finance.aging:read` is separate from `finance.payment:read`, a caller MAY legitimately see payments and open items while the aging report and balances are forbidden. Each surface MUST reach its own conclusion from its own response — the UI MUST NOT infer one surface's permission from another's.

### 2.18 Edge cases (MUST)

- **Document number only after confirm.** A `Draft` row MUST show `—`; the number MUST appear only once the payment is `Confirmed` or later.
- **A `Cancelled` payment shows `—` forever.** Cancel is `Draft`-only, so no number was ever issued. The UI MUST NOT fabricate one.
- **Posting-pending is not a failure.** A `Confirmed` payment with `journalEntryId == null` MUST show "posting…", and pressing Post MUST surface `PAYMENT_POSTING_PENDING` as an informational retry-queued outcome, not a destructive error.
- **Cancel is never offered on a `Confirmed` payment.** The UI MUST NOT render the action; a `INVALID_PAYMENT_STATE_TRANSITION` from the server MUST point the operator at reversal.
- **Reverse while allocated is blocked.** With `allocatedAmount > 0` the Reverse action MUST be disabled with an explanatory tooltip; `PAYMENT_HAS_ALLOCATIONS` MUST still be mapped.
- **Allocation is all-or-nothing.** A multi-item allocate that fails on one item MUST leave every figure unchanged in the UI; no partial optimistic update.
- **One cent over a bound fails.** The server compares exact `DECIMAL(18,2)` values with no tolerance. A client pre-check MUST use the same exactness (two decimal places, no epsilon) so the UI and the server agree, and `grossTotal − 0.01` settled MUST read `PartiallySettled`, never `Settled`.
- **Stale `rowVersion` after an allocation.** A second write using the token captured before an allocate MUST surface `CONCURRENT_MODIFICATION`; the UI MUST re-seed the token from every allocate/deallocate response instead.
- **`asOfDate` in the future is rejected.** Blocked client-side; `INVALID_AGING_AS_OF_DATE` mapped if it reaches the server.
- **Due exactly on `asOfDate` is `Current`.** `daysPastDue == 0` lands in `Current`, never in `1-30`; the UI MUST NOT relabel it "overdue".
- **Configurable buckets change the column set.** Passing four boundaries yields six columns; the table MUST adapt from `bucketLabels`.
- **A confirmed credit note never appears** in open items, in any aging bucket, or in any balances row — permanently and by design. The UI MUST NOT present its absence as projection lag or as a defect.
- **A fully settled invoice disappears from open items** while staying visible in the allocation views; deallocating makes it reappear. The UI MUST NOT treat the disappearance as data loss.
- **Multi-currency counterparty produces two rows** in aging and in balances. The UI MUST NOT merge them and MUST NOT sum their transactional amounts.
- **Empty is not an error** on any of the three read surfaces: empty rows, zero totals, `200`.
- **EN/BG parity.** Switching locale MUST re-render every payments/allocations/open-items/aging/balances string with no raw key path visible in either locale.

## 3. Validation Rules (client-side zod / form — mirrors the shipped FluentValidation; server authoritative)

> The forms MUST mirror the backend shape so the operator gets immediate feedback, but the backend remains authoritative; every server validation error MUST still surface via `getApiErrorMessage` (§4). Validation messages MUST be i18n keys, mirroring `features/invoices/schema.ts`.

### 3.1 Payment form — field-level (zod), mirroring `CreatePaymentRequestValidator` / `UpdatePaymentRequestValidator`

| Field | Client rule | Mirrors backend code |
|---|---|---|
| `documentType` | Required; one of `CustomerReceipt` (1) / `SupplierPayment` (2). Read-only in edit mode. | `INVALID_PAYMENT_DOCUMENT_TYPE` |
| `method` | Required; one of `Cash` (1) / `BankTransfer` (2) / `Card` (3) | `INVALID_PAYMENT_METHOD` |
| `counterpartyId` | Required; non-empty GUID (not `Guid.Empty`) | `PAYMENT_COUNTERPARTY_REQUIRED` |
| `currencyCode` | Required; exactly 3 uppercase letters (`^[A-Z]{3}$`) | `INVALID_PAYMENT_CURRENCY` |
| `amount` | Required; `> 0`; at most two decimal places | `INVALID_PAYMENT_AMOUNT` |
| `exchangeRate` | Required; `> 0`; at most six decimal places | `INVALID_PAYMENT_EXCHANGE_RATE` |
| `paymentDate` | Required; NOT in the future (whole-day granularity, mirroring `PaymentDateRule`) | `INVALID_PAYMENT_DATE` |
| `settlementAccountId` | Required; integer `> 0` | `PAYMENT_SETTLEMENT_ACCOUNT_REQUIRED` |
| `bankReference` | Optional; at most **64** characters | `INVALID_PAYMENT_BANK_REFERENCE` |
| `rowVersion` (edit/confirm/post/cancel/reverse) | Non-empty base64 string | `CONCURRENT_MODIFICATION` |
| Cancel `reason` | Required; non-empty / non-whitespace | `PAYMENT_CANCEL_REASON_REQUIRED` |
| Reverse `reason` | Required; non-empty / non-whitespace | `PAYMENT_REVERSE_REASON_REQUIRED` |

### 3.2 Allocation form — field-level (zod), mirroring `AllocatePaymentRequestValidator` / `AllocatePaymentItemValidator`

| Field | Client rule | Mirrors backend code |
|---|---|---|
| `items` | Required; **at least one** item. An empty list is never an implicit "apply the whole payment". | `PAYMENT_ALLOCATION_ITEMS_REQUIRED` |
| `items[].invoiceId` | Required; non-empty GUID | `PAYMENT_ALLOCATION_INVOICE_REQUIRED` |
| `items[].allocatedAmount` | Required; `> 0`; **at most two decimal places** (`round(x, 2) === x`) | `INVALID_PAYMENT_ALLOCATION_AMOUNT` |
| `rowVersion` | Required; non-empty base64 | `CONCURRENT_MODIFICATION` |

### 3.3 Read-surface query forms — field-level (zod), mirroring the three aging validators

| Field | Surface | Client rule | Mirrors backend code |
|---|---|---|---|
| `asOfDate` | open-items | Optional; if supplied, NOT in the future (date-part comparison) | `INVALID_AGING_AS_OF_DATE` |
| `asOfDate` | aging, balances | **Required**; NOT in the future | `INVALID_AGING_AS_OF_DATE` |
| `direction` | open-items | Optional; if supplied, exactly `"AR"` or `"AP"` | `INVALID_AGING_DIRECTION` |
| `direction` | aging, balances | **Required**; exactly `"AR"` or `"AP"` | `INVALID_AGING_DIRECTION` |
| `counterpartyId` | open-items, aging | Optional; if supplied, a non-empty GUID | `INVALID_COUNTERPARTY_ID` |
| `currencyCode` | all three | Optional; if supplied, `^[A-Z]{3}$` | `INVALID_AGING_CURRENCY` |
| `buckets` | aging | Optional; if supplied: at most **6** values, each a strictly positive integer, strictly ascending | `INVALID_AGING_BUCKETS` |
| `overdueOnly` | open-items | Optional boolean; defaults `false` | — |
| `pageSize` | payments, allocations, open-items, balances | `≤ 200` (`MAX_PAGE_SIZE`) | `PAGE_SIZE_TOO_LARGE` |

### 3.4 Cross-field (zod `superRefine`)

- **Allocation totals.** `Σ items[].allocatedAmount ≤ payment.unallocatedAmount` MUST be enforced cross-field (message `allocations.validation.exceedsUnallocated`), and per item `allocatedAmount ≤ openItem.outstanding` (message `allocations.validation.exceedsOutstanding`). Both mirror server rules 8 and 9 and MUST use exact two-decimal comparison — no epsilon.
- **Duplicate invoice within one request.** The same `invoiceId` MUST NOT appear twice in `items` (message `allocations.validation.duplicateInvoice`), mirroring server rule 7's within-request clause.
- **Bucket boundaries.** Strict ascent MUST be enforced cross-field with a single message (`aging.validation.bucketsAscending`), mirroring `AgingBucketCalculator.Validate`.
- **Base-amount preview.** The client MAY preview `baseAmount = round(amount × exchangeRate, 2)` for feedback but MUST NOT block submission on its own computation (the country strategy owns the rounding). `PAYMENT_BASE_AMOUNT_MISMATCH` is a defensive server code and MUST be surfaced if returned, but the client MUST NOT attempt to reproduce it as a blocking rule.
- **Rate-equals-one.** `exchangeRate === 1.000000` when `currencyCode === baseCurrencyCode` MUST NOT be a blocking client rule in v1, because the base currency is not readable before a payment exists (§1.6 gap 3). It MAY be applied as a soft hint when a base currency is already known from a loaded response; `INVALID_PAYMENT_EXCHANGE_RATE` from the server is the authority.

### 3.5 State-based (UI gating — §2.4)

- Edit / Delete / Confirm / Cancel MUST be offered for `Draft` only. Post MUST be offered for `Confirmed` only. Reverse MUST be offered for `Posted` only, and MUST be disabled while `allocatedAmount > 0`. Allocate / Deallocate MUST be offered for `Confirmed` and `Posted` only. `Cancelled` and `Reversed` MUST expose no mutating action.
- The UI MUST NOT offer an action whose backend transition would be rejected. In particular it MUST NOT offer Cancel on a `Confirmed` payment and MUST NOT offer Allocate on a `Draft` payment.

## 4. Error Rules

UI errors are i18n keys under `errors.*`. Mapping is per SDD-UI-001 §2.5 / SDD-INFRA-001: `getApiErrorMessage(err, t)` looks up `errors.<title>` (the ProblemDetails `title` = the SCREAMING_SNAKE_CASE code); if absent, falls back to ProblemDetails `detail`; if absent, `errors.GENERIC_ERROR`. Components MUST NOT render `err.message`, `err.response.status`, or raw `data.detail`.

**All 43 `PaymentErrorCodes` ALREADY have matching `errors.<CODE>` entries in BOTH `frontend/src/shared/i18n/locales/en.ts` and `bg.ts`.** This obligation is **already discharged** — it shipped with the Batch 17 backend and is pinned by `frontend/src/shared/i18n/paymentErrorCodes.test.ts`, which asserts, for all 43 codes plus `INVOICE_HAS_SETTLEMENTS`: presence in EN, presence in BG, a non-empty message in both, that no message is its own raw key path, exact EN/BG parity across the whole `errors.*` group, and that the BG messages contain Cyrillic. The frontend phase MUST keep that test green and MUST NOT remove or rename any of the entries. `CONCURRENT_MODIFICATION`, `PAGE_SIZE_TOO_LARGE`, and `GENERIC_ERROR` are likewise already present (`en.ts:384`, `:386`, `:387`).

HTTP statuses below are the shipped mapping: `PaymentErrorCodeToStatusMap` names sixteen 409 conflicts explicitly and delegates the rest to `DefaultErrorCodeToStatusMap` (`*_NOT_FOUND` → 404; `*_INACTIVE` / `*DUPLICATE*` / `*_CONFLICT` / `CONCURRENT_*` → 409; else 400).

| Code | HTTP | Trigger | UI treatment |
|---|---|---|---|
| `PAYMENT_NOT_FOUND` | 404 | Unknown payment id on any read or action | Toast |
| `PAYMENT_SETTLEMENT_ACCOUNT_NOT_FOUND` | 404 | Settlement account unknown, or the Accounts read seam is unreachable (fails closed) | Inline (settlement-account field) + toast |
| `PAYMENT_SETTLEMENT_ACCOUNT_INACTIVE` | 409 | Settlement account exists but is not active | Inline (settlement-account field) + toast |
| `PAYMENT_SETTLEMENT_ACCOUNT_REQUIRED` | 400 | Settlement account id missing / non-positive | Inline (settlement-account field) |
| `INVALID_PAYMENT_DOCUMENT_TYPE` | 400 | Missing/unknown document type, or an update tried to change it | Inline (type field) |
| `INVALID_PAYMENT_METHOD` | 400 | Missing/unknown method | Inline (method field) |
| `PAYMENT_COUNTERPARTY_REQUIRED` | 400 | Counterparty missing / empty GUID | Inline (counterparty field) |
| `INVALID_PAYMENT_CURRENCY` | 400 | Currency missing / not ISO 4217 3-letter | Inline (currency field) |
| `INVALID_PAYMENT_AMOUNT` | 400 | Amount ≤ 0 | Inline (amount field) |
| `INVALID_PAYMENT_EXCHANGE_RATE` | 400 | Rate ≤ 0, or ≠ `1.000000` on a base-currency payment | Inline (rate field) — the authority for the rate-equals-one rule (§3.4) |
| `INVALID_PAYMENT_DATE` | 400 | Payment date missing / in the future | Inline (date field) |
| `INVALID_PAYMENT_BANK_REFERENCE` | 400 | Bank reference over 64 characters | Inline (bank-reference field) |
| `PAYMENT_BASE_AMOUNT_MISMATCH` | 400 | Stored base amount ≠ rounded `amount × rate` (defensive) | Toast |
| `PAYMENT_CANCEL_REASON_REQUIRED` | 400 | Cancel without a non-empty reason | Inline (reason field) + toast |
| `PAYMENT_REVERSE_REASON_REQUIRED` | 400 | Reverse without a non-empty reason | Inline (reason field) + toast |
| `PAYMENT_NOT_DRAFT` | 409 | Confirm on a non-`Draft` payment | Toast |
| `PAYMENT_NOT_CONFIRMED` | 409 | Post / back-event link on a payment that is neither `Confirmed` nor already `Posted` | Toast — MUST read differently from `PAYMENT_POSTING_PENDING` |
| **`PAYMENT_POSTING_PENDING`** | 409 | Post while the Journal handshake has not linked an entry; the same call re-enqueues the confirm event | **Informational / progress affordance, NOT a destructive error** (§2.7). Retry-queued copy; Post stays available |
| `PAYMENT_POSTED_IMMUTABLE` | 409 | Update / delete on a `Confirmed`/`Posted`/`Cancelled`/`Reversed` payment | Toast |
| `INVALID_PAYMENT_STATE_TRANSITION` | 409 | Transition not in `AllowedNextStates` — notably cancelling a `Confirmed` payment | Toast; the message SHOULD point at reversal |
| `PAYMENT_PERIOD_CLOSED` | 409 | Payment date (confirm) or the linked entry's date (reverse) is in a closed period / has no period / the Periods service is unreachable (fails closed) | Toast; on reverse the copy MUST say the period must be reopened, not "try later" |
| `PAYMENT_DUPLICATE_DOCUMENT_NUMBER` | 409 | Confirm / replay would assign a second gapless number | Toast |
| `PAYMENT_DATE_YEAR_MISMATCH` | 409 | `paymentDate.year` ≠ the confirm-clock year | Toast with an explanatory message (§2.7) |
| `PAYMENT_HAS_ALLOCATIONS` | 409 | Cancel or reverse while `allocatedAmount > 0` | Toast; MUST tell the operator to deallocate first. Pre-empted by §2.4 gating on reverse; unreachable on cancel |
| `PAYMENT_ALLOCATION_NOT_FOUND` | 404 | Allocation id unknown **for the route payment** (scoped by `(paymentId, id)`) | Toast; MUST NOT imply someone else deleted it |
| `PAYMENT_ALLOCATION_INVOICE_NOT_FOUND` | 404 | Invoice unknown to the LOCAL open-item projection (transient lag OR permanently non-projected, e.g. a credit note) | Toast; "not available for matching yet", MAY suggest retry, MUST NOT promise it will appear |
| `PAYMENT_ALLOCATION_ITEMS_REQUIRED` | 400 | Allocate with a missing/empty item list | Inline (items list) |
| `PAYMENT_ALLOCATION_INVOICE_REQUIRED` | 400 | An item omits its invoice id | Inline (item row) |
| `INVALID_PAYMENT_ALLOCATION_AMOUNT` | 400 | Item amount ≤ 0 or more than two decimal places | Inline (item amount) |
| `PAYMENT_NOT_ALLOCATABLE` | 409 | Allocate/deallocate on a payment that is not `Confirmed`/`Posted` | Toast; gated away by §2.4 |
| `PAYMENT_ALLOCATION_INVOICE_NOT_ELIGIBLE` | 409 | The mirrored invoice status is terminal (`Cancelled`/`Reversed`) | Toast, own message |
| `PAYMENT_ALLOCATION_EXCEEDS_PAYMENT` | 409 | `Σ existing + Σ requested > payment.amount` (exact `DECIMAL(18,2)`) | Inline (running total) + toast naming the **payment** bound |
| `PAYMENT_ALLOCATION_EXCEEDS_OUTSTANDING` | 409 | Per invoice, `settled + requested > grossTotal` | Inline (item row) + toast naming the **invoice** bound |
| `PAYMENT_ALLOCATION_DIRECTION_MISMATCH` | 409 | Payment direction ≠ invoice direction | Toast, own message; pre-narrowing (§2.11) should prevent it |
| `PAYMENT_ALLOCATION_COUNTERPARTY_MISMATCH` | 409 | Payment counterparty ≠ invoice counterparty | Toast, own message |
| `PAYMENT_ALLOCATION_CURRENCY_MISMATCH` | 409 | Payment currency ≠ invoice currency (cross-currency deferred to SDD-FIN-005) | Toast, own message |
| `PAYMENT_ALLOCATION_CONTROL_ACCOUNT_MISMATCH` | 409 | The document-type pair is not a documented `SettlementPairing` (defensive; unreachable in v1) | Toast |
| `PAYMENT_ALLOCATION_DUPLICATE` | 409 | A row already exists — or is requested twice — for `(paymentId, invoiceId)` | Inline (item row) + toast; pre-empted by §2.11's client-side exclusion |
| `INVALID_AGING_AS_OF_DATE` | 400 | As-of date missing (aging/balances) or in the future (any aging surface) | Inline (as-of field) |
| `INVALID_AGING_DIRECTION` | 400 | Direction missing (aging/balances) or not `AR`/`AP` | Inline (direction field) |
| `INVALID_AGING_BUCKETS` | 400 | Boundaries not strictly ascending positive integers, or more than six | Inline (buckets field) |
| `INVALID_COUNTERPARTY_ID` | 400 | Counterparty narrowing supplied as an EMPTY GUID (an unknown-but-well-formed GUID is a valid empty `200`) | Inline (counterparty field) |
| `INVALID_AGING_CURRENCY` | 400 | Currency narrowing supplied but not a 3-letter ISO code | Inline (currency field) |
| `CONCURRENT_MODIFICATION` | 409 | Stale/malformed base64 `rowVersion` on any write | Toast; the UI MUST re-read or re-seed the token (§1.4 trap 11) |
| `PAGE_SIZE_TOO_LARGE` | 400 | `pageSize` above the 200 cap | Toast (should be unreachable — the client clamps with `MAX_PAGE_SIZE`) |

- Inline-treated codes SHOULD map to the offending form field where the field is known; every code MUST also be safe to surface as a toast via the error helper (the helper is the unconditional fallback).
- A forced server 500 / an unmapped code MUST fall back to `errors.GENERIC_ERROR` — never a raw key path, never a raw status.
- **There is deliberately no `COUNTERPARTY_NOT_FOUND` and no `OPEN_ITEM_NOT_FOUND`.** An unknown counterparty and an empty window are valid business states answered with an empty `200`. The UI MUST NOT synthesize a not-found message for either.

## 5. i18n

All new strings MUST be keys present in BOTH `en.ts` and `bg.ts` in the SAME PR (SDD-UI-001 §2.3). Key groups:

- **Nav:** `nav.payments`, `nav.openItems`, `nav.aging`.
- **`payments.*` — titles / nav / empty:** `payments.title`, `.newPayment`, `.detailTitle`, `.createTitle`, `.editTitle`, `.searchPlaceholder` (MUST say the search matches the document number only), `.empty`, `.emptyHint`, `.forbidden`, `.forbiddenHint`.
- **`payments.*` — columns / fields:** `.documentNumber`, `.documentType`, `.direction`, `.method`, `.status`, `.counterparty`, `.counterpartyPlaceholder`, `.currency`, `.amount`, `.exchangeRate`, `.baseCurrency`, `.baseAmount`, `.baseAmountPreview`, `.settlementAccount`, `.paymentDate`, `.bankReference`, `.allocatedAmount`, `.unallocatedAmount`, `.unapplied`, `.journalEntry`, `.cancellationReason`, `.createdAt`, `.confirmedAt`, `.postedAt`, `.reversedAt`.
- **`payments.*` — document types:** `.type_CustomerReceipt`, `.type_SupplierPayment`.
- **`payments.*` — directions:** `.direction_AP`, `.direction_AR`.
- **`payments.*` — methods:** `.method_Cash`, `.method_BankTransfer`, `.method_Card`.
- **`payments.*` — statuses:** `.status_Draft`, `.status_Confirmed`, `.status_Posting` (the derived posting-pending affordance — NOT a backend value), `.status_Posted`, `.status_Cancelled`, `.status_Reversed`.
- **`payments.*` — actions:** `.edit`, `.delete`, `.confirm`, `.post`, `.cancel`, `.reverse`, `.allocate`, `.viewAllocations`.
- **`payments.*` — dialogs / hints / toasts:** `.confirmTitle`/`.confirmMessage`, `.postTitle`/`.postMessage`, `.postingPendingHint`, `.postingPendingQueued`, `.cancelTitle`/`.cancelMessage`/`.cancelReasonLabel`, `.cancelNotAvailableHint` (why a confirmed payment cannot be cancelled), `.reverseTitle`/`.reverseMessage`/`.reverseReasonLabel`, `.reverseBlockedByAllocations`, `.deleteTitle`/`.deleteMessage`, `.created`, `.updated`, `.deleted`, `.confirmed`, `.posted`, `.cancelled`, `.reversed`.
- **`payments.validation.*`:** `.documentTypeRequired`, `.methodRequired`, `.counterpartyRequired`, `.currencyRequired`, `.amountPositive`, `.amountScale`, `.exchangeRatePositive`, `.exchangeRateScale`, `.paymentDateRequired`, `.paymentDateFuture`, `.settlementAccountRequired`, `.bankReferenceTooLong`, `.cancelReasonRequired`, `.reverseReasonRequired`.
- **`allocations.*` — panel / columns:** `allocations.title`, `.allocate`, `.deallocate`, `.empty`, `.emptyHint`, `.invoice`, `.invoiceDueDate`, `.invoiceStatus`, `.invoiceGrossTotal`, `.allocatedAmount`, `.baseAllocatedAmount`, `.realizedFxDifference`, `.realizedFxInformational`, `.allocatedAt`, `.runningTotal`, `.remainingUnallocated`, `.pickerTitle`, `.pickerHint`, `.applyMax`.
- **`allocations.*` — settlement statuses (numeric enum):** `.settlement_Unsettled`, `.settlement_PartiallySettled`, `.settlement_Settled`.
- **`allocations.*` — dialogs / toasts:** `.allocateTitle`, `.allocateMessage`, `.deallocateTitle`, `.deallocateMessage`, `.deallocateReasonLabel`, `.noAmendHint` (a wrong amount is corrected by release + re-allocate), `.allocated`, `.deallocated`.
- **`allocations.validation.*`:** `.itemsRequired`, `.invoiceRequired`, `.amountPositive`, `.amountScale`, `.exceedsUnallocated`, `.exceedsOutstanding`, `.duplicateInvoice`.
- **`openItems.*`:** `openItems.title`, `.empty`, `.emptyHint`, `.forbidden`, `.asOfDate`, `.direction`, `.counterparty`, `.currency`, `.overdueOnly`, `.documentNumber`, `.documentType`, `.grossTotal`, `.settledAmount`, `.outstanding`, `.baseOutstanding`, `.bookingRateHint`, `.issueDate`, `.dueDate`, `.daysPastDue`, `.notYetDue`, `.agingBucket`, `.invoiceStatus`, `.settlementStatus`, `.eventualConsistencyHint`, `.creditNoteExcludedHint`.
- **`aging.*`:** `aging.title`, `.asOfDate`, `.direction`, `.counterparty`, `.currency`, `.buckets`, `.bucketsHint` (up to six strictly ascending positive boundaries), `.bucketsDefaultHint`, `.bucket_Current` (the ONLY bucket label that is translated — the rest are server data, §1.4 trap 16), `.openItemCount`, `.totalOutstanding`, `.totalBaseOutstanding`, `.reportTotals`, `.grandTotalBaseOutstanding`, `.baseCurrencyOnlyHint`, `.periodAgnosticHint`, `.invoiceOnlyHint`, `.drillDown`, `.empty`, `.emptyHint`, `.forbidden`, `.forbiddenHint`.
- **`aging.validation.*`:** `.asOfDateRequired`, `.asOfDateFuture`, `.directionRequired`, `.bucketsAscending`, `.bucketsPositive`, `.bucketsTooMany`, `.currencyInvalid`, `.counterpartyInvalid`.
- **`balances.*`:** `balances.title`, `.counterparty`, `.currency`, `.openItemCount`, `.outstanding`, `.baseOutstanding`, `.overdueOutstanding`, `.baseOverdueOutstanding`, `.oldestDueDate`, `.noOpenItems`, `.overdueDefinitionHint`, `.matchesAgingHint`, `.noSortingHint`, `.empty`, `.emptyHint`, `.forbidden`.
- **`errors.*`:** the 43 `errors.<PAYMENT_CODE>` entries plus `errors.CONCURRENT_MODIFICATION`, `errors.PAGE_SIZE_TOO_LARGE`, and `errors.GENERIC_ERROR` — **all already present and pinned by `paymentErrorCodes.test.ts`** (§4). This feature MUST keep them, MUST NOT rename them, and MUST NOT add a second copy.
- EN and BG MUST stay key-for-key in sync; a parity check MUST cover every new key group above (§7.1).

## 6. Versioning Notes

- **v1 — Initial specification (Drafted).** The Payments SPA feature surface consuming all **15** shipped `Finance.Payments.API` endpoints in dialog-mode: the payments list (paged `FilterRequest`, default `PaymentDate` desc, `pageSize ≤ 200`, `DocumentNumber`-only search), detail, create/edit/delete draft, confirm (gapless `RCT`/`PAY` number), post with the posting-pending affordance and the re-enqueue recovery path, cancel (**`Draft` ONLY**, reason required), reverse (`Posted` only, reason required, blocked while allocated), the allocation sub-surface (list / allocate with an explicit all-or-nothing item list / deallocate with query-param `rowVersion` + `reason`) surfacing all ten invariant codes meaningfully, and the three read roll-ups (open items, configurable-bucket aging under the separate `finance.aging:read`, counterparty balances). Built on the SDD-UI-001 ledger shell (theme, density, EN+BG i18n, axios + `X-Correlation-ID`, error helper), with a nomenclature-backed currency dropdown (SDD-NOM-001) and the 43 `PaymentErrorCodes` **already** mapped to EN+BG `errors.*` keys (pinned by `paymentErrorCodes.test.ts`). RBAC is enforced by the backend's eight permissions (SDD-INT-AUTH-001 / `CHG-ENH-003`), with action availability gated by entity status in the UI and an editorial forbidden state for the un-seeded case.
- **Deferred (future versions / specs) — see §1.6 for the full gap list with reasons:** counterparty name enrichment (`SDD-INT-WH-002`), a cash/bank-filtered settlement-account picker (`CHG-ENH-002`), a base-currency read endpoint, automatic FX rate resolution and realized-FX posting (`SDD-FIN-005`), auto/FIFO allocation and in-place allocation amendment (`SDD-PAY-002` §5/§7), an "exclude already-allocated" open-item narrowing, server-side paging for the aging report, a counterparty narrowing on balances, sorting on the grouped balances table, free-text search on open items, a payment status-history / audit panel (`SDD-AUDIT-001` read endpoint deferred), a `ReversalJournalEntryId` link and a journal-entry detail route, refunds / compensation settlement (breaking enum change), bank statement import and payment-file generation, aging export and printable receipts, bulk operations, approval / maker-checker, and page-mode forms (`SDD-UI-002`).
- Changing a consumed endpoint contract, the status set, the permission strings, the enum member values, or the error-code set is a **breaking** change that originates in the owning backend spec (`SDD-PAY-001` / `-002` / `-003`) and requires a coordinated update here (new `errors.*` keys in both locales, new wire types). Adding a column, a narrowing, or a display affordance is **non-breaking/additive**.

## 7. Test Plan

> Vitest component/unit tests live under `frontend/src/components/pages/{PaymentsListPage,OpenItemsListPage,AgingReportPage}.test.tsx`, `frontend/src/components/organisms/{PaymentFormDialog,PaymentAllocationsDialog}.test.tsx`, and `frontend/src/features/payments/{schema,types,i18n}.test.ts`, rendered via `src/test/renderWithProviders.tsx` (mirroring the shipped invoices test files). `ui-validate` checks drive the SPA via Chrome DevTools MCP (Phase 7). Business tests SHOULD reference the spec id (`SDD-UI-FIN-002`) in their `describe` title.

### 7.1 Vitest component / unit

| Test name | Kind |
|---|---|
| `PaymentsList_Authenticated_RendersPagedResultEnvelope` — list reads `{ items, totalCount, page, pageSize }`, not a bare array | [Unit] |
| `PaymentsList_FilterSortPage_SendsFilterRequestParams_ServerSidePaging_PageSizeCapped` — paging/sort issues `FilterRequest`; no client-side paging; `pageSize ≤ 200` | [Unit] |
| `PaymentsList_DefaultSort_IsPaymentDateDescending` — the initial `sort` term is `paymentDate` desc | [Unit] |
| `PaymentsList_SearchBox_TargetsDocumentNumberOnly` — `search` is sent and the placeholder names the document number (the sole `[Searchable]` property) | [Unit] |
| `PaymentsList_CounterpartyColumn_IsNotSortable` — `CounterpartyId` is `[Filterable]`-only, so the column exposes no sort | [Unit] |
| `PaymentsList_DraftRow_ShowsDashForDocumentNumber` — `Draft` shows `—`; `Confirmed` shows the assigned number | [Unit] |
| `PaymentsList_CancelledRow_StillShowsDashForDocumentNumber` — a `Cancelled` payment never held a number (the SDD-UI-FIN-001 divergence) | [Unit] |
| `PaymentsList_ActionsGatedByStatus_DraftEditConfirmCancelDelete_ConfirmedPostAllocate_PostedReverseAllocate` — the full status→action matrix (§2.4) | [Unit] |
| `PaymentsList_ConfirmedRow_DoesNotOfferCancel` — `Confirmed → Cancelled` was removed; the action MUST be absent | [Unit] |
| `PaymentsList_DraftRow_DoesNotOfferAllocate` — allocation requires `Confirmed`/`Posted` | [Unit] |
| `PaymentsList_PostingPending_ShowsPostingAffordance` — `Confirmed` with `journalEntryId == null` renders `payments.status_Posting` | [Unit] |
| `PaymentsList_UnallocatedAmount_IsVisibleOnRow` — unapplied cash is visible without opening the payment | [Unit] |
| `PaymentTypes_EnumsMirrorDotNetNumericValues_ApAr` — `PaymentDirection.AP === 1`, `.AR === 2`; `PaymentStatus.Draft === 1 … Reversed === 5`; `PaymentDocumentType` 1–2; `PaymentMethod` 1–3; `SettlementStatus` 1–3 | [Unit] |
| `PaymentTypes_MixedWireShapes_NumericOnPaymentDto_StringOnOpenItemDto` — the §1.2 mixed-enum contract is pinned in the types | [Unit] |
| `PaymentForm_MissingCounterparty_ShowsCounterpartyRequired` — zod field validation | [Unit] |
| `PaymentForm_NonPositiveAmount_ShowsAmountPositive` — `amount > 0` | [Unit] |
| `PaymentForm_AmountWithThreeDecimals_ShowsAmountScale` — two-decimal money scale | [Unit] |
| `PaymentForm_FuturePaymentDate_ShowsPaymentDateFuture` — mirrors `PaymentDateRule` | [Unit] |
| `PaymentForm_BankReferenceOver64Chars_ShowsBankReferenceTooLong` — the 64-char bound | [Unit] |
| `PaymentForm_DocumentTypeReadOnlyInEditMode_AndSentUnchanged` — the type drives direction/sequence/posting rule and MUST NOT be editable | [Unit] |
| `PaymentForm_DirectionDerivedFromDocumentType_ShownReadOnly` — `CustomerReceipt → AR`, `SupplierPayment → AP`; never sent | [Unit] |
| `PaymentForm_BaseAmountPreview_RecomputesOnAmountAndRateChange_ServerOverridesAfterSave` — preview via `useWatch` (the SDD-UI-FIN-001 regression), server value re-displayed | [Unit] |
| `PaymentForm_RateEqualsOneRule_IsSoftHintOnly_NotBlocking` — the base currency is unknown pre-save (§1.6 gap 3) | [Unit] |
| `PaymentMutations_Create_InvalidatesListAndShowsSuccess` — create success path | [Unit] |
| `PaymentMutations_Confirm_NotDraft_ShowsPaymentNotDraftToast` — `PAYMENT_NOT_DRAFT` mapped | [Unit] |
| `PaymentMutations_Confirm_YearMismatch_ShowsPaymentDateYearMismatchToast` — `PAYMENT_DATE_YEAR_MISMATCH` mapped with its explanatory message | [Unit] |
| `PaymentMutations_Post_PostingPending_ShowsInformationalRetryQueued_NotDestructiveError` — `PAYMENT_POSTING_PENDING` is progress, not alarm; Post stays available | [Unit] |
| `PaymentMutations_Post_NotConfirmed_ShowsDistinctMessageFromPostingPending` — the two 409s MUST NOT collapse into one message | [Unit] |
| `PaymentMutations_Edit_StaleRowVersion_ShowsConcurrentModificationToast` — `CONCURRENT_MODIFICATION` mapped | [Unit] |
| `PaymentCancel_EmptyReason_SubmitDisabled_AndServerCodeMapped` — reason required + `PAYMENT_CANCEL_REASON_REQUIRED` | [Unit] |
| `PaymentCancel_ConfirmedPayment_MapsInvalidStateTransitionAndSuggestsReversal` — defensive mapping with the right copy | [Unit] |
| `PaymentReverse_EmptyReason_SubmitDisabled_AndServerCodeMapped` — `PAYMENT_REVERSE_REASON_REQUIRED` | [Unit] |
| `PaymentReverse_AllocatedPayment_ActionDisabled_AndHasAllocationsMapped` — gated by `allocatedAmount > 0`; code still mapped | [Unit] |
| `PaymentReverse_ClosedPeriod_ShowsReopenPeriodMessage_NotTryAgainLater` — `PAYMENT_PERIOD_CLOSED` copy on reverse | [Unit] |
| `PaymentImmutability_ConfirmedRow_NoEditOrDelete_ImmutableCodeMapped` — `PAYMENT_POSTED_IMMUTABLE` mapped | [Unit] |
| `Allocations_EmptyList_RendersEmptyStateNotError` — an unallocated payment is a normal state | [Unit] |
| `Allocations_ListSortSurface_AllocatedAtAndAmountOnly_InvoiceIdNotSortable` — the opt-in surface; no search box | [Unit] |
| `Allocations_SettlementStatus_RenderedFromNumericEnum_NotRederivedClientSide` — the server owns the derivation | [Unit] |
| `Allocations_RealizedFxDifference_LabelledInformational` — never presented as a posted GL amount | [Unit] |
| `AllocatePicker_PreNarrowsOpenItemsByCounterpartyCurrencyDirection` — direction sent as `"AR"`/`"AP"` derived from the numeric DTO value | [Unit] |
| `AllocatePicker_ExcludesInvoicesAlreadyAllocatedToThisPayment` — avoids a guaranteed `PAYMENT_ALLOCATION_DUPLICATE` | [Unit] |
| `AllocateForm_EmptyItems_ShowsItemsRequired` — an empty list is never "apply everything" | [Unit] |
| `AllocateForm_AmountWithThreeDecimals_ShowsAmountScale` — two-decimal exactness, no epsilon | [Unit] |
| `AllocateForm_SumOverUnallocated_ShowsExceedsUnallocated` — cross-field rule 8 mirror | [Unit] |
| `AllocateForm_ItemOverOutstanding_ShowsExceedsOutstanding` — cross-field rule 9 mirror | [Unit] |
| `AllocateForm_DuplicateInvoiceWithinRequest_ShowsDuplicateInvoice` — rule 7's within-request clause | [Unit] |
| `AllocateForm_OneCentOverBound_Fails_NoToleranceBand` — exact `DECIMAL(18,2)` comparison | [Unit] |
| `Allocate_Success_ConsumesResultDto_ReseedsRowVersion_UpdatesSettlementState` — 200 (not 201), no follow-up read, token re-seeded | [Unit] |
| `Allocate_Failure_IsAllOrNothing_NoPartialOptimisticUpdate` — no figure moves on failure | [Unit] |
| `Allocate_TenInvariantCodes_EachMapsToItsOwnMessage` — all ten allocation 404/409 codes resolve to distinct, non-generic messages | [Unit] |
| `Deallocate_SendsRowVersionAndReasonAsQueryParams_NotBody` — the shipped `[FromQuery]` contract | [Unit] |
| `Deallocate_Success_ConsumesResultDto_ReseedsRowVersion` — release path | [Unit] |
| `Deallocate_ForeignAllocationId_MapsAllocationNotFound` — scoped `(paymentId, allocationId)` lookup | [Unit] |
| `OpenItems_MergesFilterRequestWithQueryNarrowings` — `toFilterParams` + `asOfDate`/`direction`/`counterpartyId`/`currencyCode`/`overdueOnly` | [Unit] |
| `OpenItems_DefaultOrder_IsOldestDueFirst` — `dueDate` ascending worklist order | [Unit] |
| `OpenItems_NoSearchBoxRendered` — `InvoiceOpenItem` declares no `[Searchable]` property | [Unit] |
| `OpenItems_NotYetDue_RendersNotYetDueNotNegativeDays` — `daysPastDue ≤ 0` presentation | [Unit] |
| `OpenItems_EventualConsistencyAndCreditNoteHints_AreRendered` — the two translated help texts | [Unit] |
| `OpenItems_FutureAsOfDate_BlockedClientSide_AndServerCodeMapped` — `INVALID_AGING_AS_OF_DATE` | [Unit] |
| `Aging_BucketsSerializedAsRepeatedQueryValues_NotBracketedNorCommaSeparated` — `?buckets=30&buckets=60&buckets=90` | [Unit] |
| `Aging_OmitsBucketsParamWhenNotCustomized` — the server applies its documented default | [Unit] |
| `Aging_ColumnsBuiltFromResponseBucketLabels_NotHardCodedFive` — dynamic column set | [Unit] |
| `Aging_FourBoundaries_RendersSixBucketColumns` — configurability is real | [Unit] |
| `Aging_SendsNoFilterRequest_AndDoesNotPageClientSideAsIfServerPaged` — the endpoint has no paging contract | [Unit] |
| `Aging_MoreThanSixBoundaries_ShowsBucketsTooMany` — the six-boundary cap | [Unit] |
| `Aging_NonAscendingBoundaries_ShowsBucketsAscending` — strict ascent | [Unit] |
| `Aging_NonPositiveBoundary_ShowsBucketsPositive` — strictly positive | [Unit] |
| `Aging_NoCrossCurrencyTransactionalTotalRendered` — only base-currency totals are summed | [Unit] |
| `Aging_EmptyReport_RendersEmptyStateWithZeroTotals_Not404Copy` — empty is a `200` | [Unit] |
| `Aging_RowDrillDown_NavigatesToOpenItemsWithNarrowings` — counterparty + currency + direction + as-of carried through | [Unit] |
| `Aging_PeriodAgnosticAndInvoiceOnlyHints_AreRendered` — the numbers are not misread | [Unit] |
| `Balances_RequiresAsOfDateAndDirection_ShowsValidationWhenMissing` — both are required here (unlike open items) | [Unit] |
| `Balances_GridExposesNoUserSorting` — grouped rows, server-fixed order | [Unit] |
| `Balances_NullOldestDueDate_RendersNoOpenItemsPlaceholder` — nullable field | [Unit] |
| `Permissions_ForbiddenListResponse_RendersEditorialForbiddenState_NoRawStatus` — the `CHG-ENH-003` day-one reality | [Unit] |
| `Permissions_AgingForbiddenButPaymentsAllowed_SurfacesIndependently` — `finance.aging:read` is a separate permission | [Unit] |
| `PaymentError_UnmappedCode_FallsBackToGenericError` — never a raw key path | [Unit] |
| `Payments_I18n_AllKeysExistInEnAndBg` — every `payments.*`, `allocations.*`, `openItems.*`, `aging.*`, `balances.*`, and `nav.{payments,openItems,aging}` key present in both locales | [Unit] |
| `Payments_I18n_ReusesShippedPaymentErrorCodeEntries_WithoutDuplication` — the 43 `errors.<CODE>` entries are reused, not re-declared; `paymentErrorCodes.test.ts` stays green | [Unit] |

### 7.2 `ui-validate` (Chrome DevTools MCP)

**(a) Verifiable OFFLINE — SPA only, no Finance backend required.** These drive the built SPA with the API either unreachable or stubbed; they assert client-side behavior, and a `403`/network failure is itself one of the expected inputs.

| Check | Kind |
|---|---|
| `PaymentsUi_Boot_NavToPayments_OpenItems_Aging_RoutesRender` — app boots and all three routes render their shells | [UI] |
| `PaymentsUi_I18n_NoRawKeys_EnAndBg` — switch EN↔BG on all three routes; no raw `payments.*`/`allocations.*`/`openItems.*`/`aging.*`/`balances.*`/`errors.*` key path renders in either locale | [UI] |
| `PaymentsUi_Density_CompactTightensGridsAndDialogs` — density flows from `useLayoutStore` to every grid and dialog | [UI] |
| `PaymentsUi_LedgerAesthetic_NoMaterialBlueNoGradientNoGlow_MoneyIsMonoTabular` — the §1.1 aesthetic constraints hold on the aging table | [UI] |
| `PaymentsUi_CreateDialog_ClientValidation_BlocksInvalidSubmit` — zod validation surfaces before any request | [UI] |
| `PaymentsUi_EditDialog_DocumentTypeIsReadOnly` — the type cannot be changed | [UI] |
| `PaymentsUi_CancelDialog_RequiresNonEmptyReason` — submitting with an empty reason MUST issue no request, keep the dialog open, and show the translated `common.reasonRequired` message. The shared `ReasonPromptDialog` validates on submit rather than disabling the button: a disabled control explains nothing, whereas the zod message names the requirement. The invariant to assert is that **no request escapes**, not the button's `disabled` attribute. | [UI] |
| `PaymentsUi_ReverseDialog_RequiresNonEmptyReason` — same for reverse | [UI] |
| `PaymentsUi_AllocateDialog_ClientBoundsBlockOverAllocation` — running total vs unallocated blocks submit | [UI] |
| `PaymentsUi_AgingBucketsQuery_SerializesAsRepeatedValues` — inspect the outbound request: `buckets=30&buckets=60&buckets=90` | [UI] |
| `PaymentsUi_OpenItemsQuery_MergesFilterRequestAndNarrowings` — inspect the outbound query string | [UI] |
| `PaymentsUi_Deallocate_SendsQueryParamsNotBody` — inspect the outbound `DELETE` | [UI] |
| `PaymentsUi_Forbidden403_RendersEditorialForbiddenState_NoRawStatusNoSpinnerLoop` — the un-seeded-permission day-one path | [UI] |
| `PaymentsUi_ErrorToast_MappedNotRaw` — a forced server error surfaces the mapped `errors.*` message, never `err.message`/raw `detail`/status | [UI] |
| `PaymentsUi_CorrelationId_EveryRequestHasUniqueHeader` — every outbound payments/allocations/aging request carries a unique `X-Correlation-ID` | [UI] |
| `PaymentsUi_ConsoleClean_NoErrorsOrUnhandledRejections` — no console errors across the three routes and every dialog | [UI] |

**(b) Requires a LIVE stack (gateway + `Finance.Payments.API` + SQL Server + Redis + RabbitMQ + the Journal service, and the eight `CHG-ENH-003` permissions seeded).** No Finance backend runs in-browser today — `docker-compose.finance.yml` was only just made composable and there is no service daemon — so if the stack is unavailable at Phase 7 these MUST be recorded as **deferred**, not reported as passing. The backend behavior they would cover is already guarded by the 286 `[Unit]` tests in `Finance.Payments.API.Tests`; what is NOT guarded anywhere is the end-to-end wiring, which is exactly why these must stay named.

| Check | Kind | Needs |
|---|---|---|
| `PaymentsUi_Live_ListRendersPagedEnvelopeWithRows` — a populated grid from the real API | [UI] | live stack |
| `PaymentsUi_Live_CreateThenConfirm_AssignsAndDisplaysDocumentNumber` — `—` becomes `RCT-…`/`PAY-…` | [UI] | live stack |
| `PaymentsUi_Live_PostingPending_ShowsPostingThenPostedOnRefetch` — the Confirm→Post handshake observed in the UI | [UI] | live stack + Journal service |
| `PaymentsUi_Live_RedrivePost_ReEnqueuesAndEventuallyPosts` — the documented recovery path, bounded retries | [UI] | live stack + Journal service |
| `PaymentsUi_Live_CancelDraft_KeepsDashDocumentNumber` — a cancelled draft never gains a number | [UI] | live stack |
| `PaymentsUi_Live_AllocateAgainstOpenItem_UpdatesSettlementAndUnallocated` — the golden allocation path | [UI] | live stack + a confirmed invoice + the projection consumers |
| `PaymentsUi_Live_DeallocateRestoresOutstanding` — release path | [UI] | live stack |
| `PaymentsUi_Live_ReverseBlockedWhileAllocated_ThenSucceedsAfterDeallocate` — the `PAYMENT_HAS_ALLOCATIONS` precondition end to end | [UI] | live stack |
| `PaymentsUi_Live_AgingBucketsPopulate_AndDrillDownFiltersOpenItems` — real buckets and the drill-down | [UI] | live stack + seeded invoices |
| `PaymentsUi_Live_BalancesMatchAgingTotalsForSamePair` — the shared-aggregation-path guarantee, observed | [UI] | live stack |
| `PaymentsUi_Live_Rbac403_WhenPermissionNotGranted` — a genuinely un-granted permission, not a stub | [UI] | live stack + auth-service |
