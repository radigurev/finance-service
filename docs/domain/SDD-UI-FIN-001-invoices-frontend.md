# SDD-UI-FIN-001 — Invoices Frontend Feature (Purchase/Sale Invoices + Credit/Debit Notes)

> Status: **Drafted** (freshly authored by the spec-writer Phase 1; not yet committed/built). Per the repo status lifecycle (`docs/README.md` → "Status lifecycle" and `CLAUDE.md` §0): this spec becomes **`Active`** when the Phase-6 (`implement-frontend`) UI work begins, and **`Implemented`** only after the frontend ships AND the Vitest suite + the `ui-validate` (Chrome DevTools MCP) golden-path checks pass (Phase 7). It documents the **planned** Invoices SPA surface; no TypeScript/React code is authored by this spec.
> Owner: Frontend
> Last updated: 2026-06-10
> Category: Domain (feature UI surface). The companion frontend **shell** spec `SDD-UI-001` (theme, density, i18n, navigation, Atomic Design, axios/correlation, error helper) lives in `docs/infrastructure/`; this spec depends on it and MUST NOT re-define shell behavior.
> Related: SDD-INV-001 (Invoice Lifecycle — THE backend contract this UI consumes: the 8 endpoints, the `Draft → Confirmed → Posted` (+ `Cancelled`/`Reversed`) lifecycle, immutability, the 17 `InvoiceErrorCodes`, the `finance.invoice:*` permissions, server-computed tax/totals, gapless number at confirm, posting-pending, Credit/Debit-Note correction via `CorrectsInvoiceId`), SDD-INT-WH-001 (Warehouse inbound events → system-created **draft** invoices the UI must surface for operator review/completion before confirm), SDD-UI-001 (frontend shell — theme/density/i18n/navigation/axios/error helper this feature builds on), SDD-NOM-001 (counterparty/currency dropdowns via `useNomenclature()` — no hard-coded options), SDD-CTRY-001 (document-number format + tax come from the country strategy server-side; the UI DISPLAYS them, never recomputes authoritatively), SDD-INFRA-001 (ProblemDetails error model + `X-Correlation-ID` on every request), SDD-INFRA-005 (Generic Filtering — the `FilterRequest`/`PagedResult` contract the list grid binds to, `PageSize ≤ 200`), SDD-INT-AUTH-001 (shared JWT + RBAC gating of actions). The dialog-vs-page dual-mode spec `SDD-UI-002` is `Planned` and NOT yet built; this app is **dialog-mode** (see §1.2), so this spec describes the established dialog pattern and does NOT invent page-mode machinery.
> ISA-95: Level 4 (Business Planning & Logistics) — UI surface over financial Documents (SDD-INV-001).

---

## 1. Context & Scope

This spec defines the **Invoices frontend feature surface** of the Finance SPA: the React + TypeScript + MUI views, hooks, schema, and dialogs that let an operator manage the four invoice document types — **Purchase Invoice**, **Sale Invoice**, **Credit Note**, **Debit Note** (one backend aggregate discriminated by `DocumentType` + `AP`/`AR` `Direction`, SDD-INV-001 §1) — across their `Draft → Confirmed → Posted` (+ `Cancelled`/`Reversed`) lifecycle.

It is a **pure consumer** of the `Finance.Invoices.API` REST contract defined in SDD-INV-001 (8 endpoints under `/api/v1/invoices`, proxied by `Finance.Gateway`). It introduces NO new backend behavior, endpoints, events, error codes, tables, or business rules. The server is **authoritative** for tax, totals, document numbering, state transitions, immutability, and validation; this UI MAY preview/mirror those rules client-side for UX but MUST re-display the server's persisted values after every write and MUST surface every server error through the shared error helper.

### 1.1 Relationship to the shell (SDD-UI-001) and the established feature pattern

This feature MUST be built exactly like the already-shipped `journal` and `periods` feature surfaces (the canonical pattern to mirror), and MUST reuse the shell primitives from SDD-UI-001:

- Feature folder `frontend/src/features/invoices/` with `api.ts` (typed axios calls), `types.ts` (wire contracts mirroring the .NET DTOs field-for-field), `schema.ts` (zod form schema), and `useInvoiceMutations.ts` (TanStack Query mutations) — mirroring `features/journal/{api,types,schema,useJournalMutations}.ts`.
- A list page `frontend/src/components/pages/InvoicesListPage.tsx` mirroring `JournalEntriesListPage.tsx` — `ListPageTemplate` + `DataTable` (MUI DataGrid wrapper) + `FilterBar`, server-side `FilterRequest` paging/sort/search, status-gated row actions, `notification.error(getApiErrorMessage(...))` on every failure.
- A create/edit organism `frontend/src/components/organisms/InvoiceFormDialog.tsx` (mirroring `JournalEntryFormDialog.tsx`), and reason-prompt / confirm action dialogs mirroring `ReverseJournalEntryDialog.tsx` / `ConfirmDialog`.
- The shared axios instance (`@/shared/api/axios` — attaches `Authorization` + a fresh `X-Correlation-ID` per request, SDD-UI-001 §2.2), `getApiErrorMessage` (SDD-UI-001 §2.5), the `notification` helper, the `useLayoutStore` density store (SDD-UI-001 §2.4), the ledger theme (§2.8 — serif Fraunces headings, hairline rules, paper/ink palette, deep-green accent, mono **tabular figures** for money/codes), and EN+BG i18n (SDD-UI-001 §2.3).

### 1.2 Confirmed frontend conventions (from the actual code)

- **Display mode: dialog-mode only.** The app ships dialog-based create/edit (the `*FormDialog` organisms); there are NO `*CreatePage`/`*EditPage` routes. `SDD-UI-002` (dialog-vs-page dual mode + `isPageMode`) is `Planned`/not built. This feature MUST therefore use dialogs and MUST NOT introduce page-mode routes or an `isPageMode` flag (no overengineering).
- **`useGoBack` exists** (`frontend/src/shared/hooks/useGoBack.ts`) and is used for future page-mode reuse. Because this feature is dialog-mode (forms close in place), `useGoBack` is NOT required here; it MAY be used only if a future read-only detail route is added (out of scope for v1).
- **No client-side permission hook.** There is no `usePermission`/`hasPermission` in the codebase. RBAC is enforced by the backend `[RequirePermission(...)]`; a `403` surfaces through `getApiErrorMessage`. Action availability in the UI is gated by **entity status** (e.g., only `Draft` rows expose edit/delete/confirm). Hiding a control by client-side permission is therefore a SHOULD/MAY, not a current pattern (§2.3).
- **Status enum on the wire is numeric** for the journal feature (no string-enum converter). The invoices `types.ts` MUST mirror however `Finance.Common.Enums.Invoice*` is actually serialized by `Finance.Invoices.API`; the implementator MUST confirm the on-wire shape (numeric vs string) against the API and the `InvoiceDto`, and the zod/types MUST match it field-for-field.

### 1.3 Scope — covered (v1)

- **List** invoices: filter / sort / paginate via the SDD-INFRA-005 `FilterRequest` → `PagedResult<InvoiceDto>` contract (`PageSize ≤ 200`), default order `IssueDate` desc then `Id`.
- **View detail** of one invoice with its lines and computed totals.
- **Create draft** (manual): `DocumentType`, counterparty, currency, issue/due dates, ≥ 1 line.
- **Edit draft** (Draft only — header + lines).
- **Confirm** a draft (assigns the gapless document number server-side).
- **Post** a confirmed invoice (and **posting-pending** handling — confirmed but `JournalEntryId` not yet linked).
- **Cancel** a Draft/Confirmed invoice (**reason required**).
- **Delete** a draft (Draft only).
- **Create a Credit/Debit Note** linked to a Posted invoice (`CorrectsInvoiceId`).
- **Surface Warehouse-created drafts** (SDD-INT-WH-001) in the same list for operator review/completion before confirm, including their source-document origin.
- Cross-cutting: ledger theme + density (SDD-UI-001), EN+BG i18n parity, `X-Correlation-ID` on every request (SDD-INFRA-001), nomenclature dropdowns (SDD-NOM-001), ProblemDetails error mapping via `getApiErrorMessage`.

### 1.4 Scope — excluded / deferred

- **Payments / settlement / allocation UI** (outstanding balance, AP/AR aging) — `SDD-PAY-001` / `SDD-PAY-002`.
- **Reporting / VAT-journals UI** over posted invoices — `SDD-RPT-003`.
- **The automatic `Posted → Reversed` full-offset behavior** — deferred backend (SDD-INV-001 §5; the UI MUST NOT assume a full-offset note auto-reverses the original — the original stays `Posted`).
- **Bulk operations** (multi-select confirm/post/cancel) — future `CHG-FEAT-*`.
- **Approval / maker-checker** between Draft and Confirmed — future `CHG-FEAT-*` (no `Approved` state exists).
- **Page-mode forms** (`SDD-UI-002` `isPageMode`) and the **audit-log panel** (blocked on the deferred `GET /api/v1/audit/export`, SDD-UI-001 §7).
- **Counterparty name enrichment** for display (depends on the deferred `SDD-INT-WH-002`); v1 MAY show the raw counterparty id / a name only if the DTO already carries one.
- **FX rate entry UI** — v1 assumes a base-currency context (SDD-INV-001 §1).

## 2. Behavior

> All rules build on SDD-UI-001 (shell). Each rule below is independently testable via Vitest (component/unit) and/or `ui-validate` (live SPA). Every backend call MUST go through the shared axios instance; every failure MUST be surfaced via `notification.error(getApiErrorMessage(err, t))` — never `err.message`, `err.response.status`, or raw `data.detail`.

### 2.1 List (MUST) — `GET /api/v1/invoices`
- The Invoices list MUST consume `GET /api/v1/invoices` as a `PagedResult<InvoiceDto>` envelope `{ items, totalCount, page, pageSize }` and MUST send `FilterRequest` query params (filters / sort / page / pageSize / search) through `toFilterParams`. Server-side paging is authoritative; the grid MUST NOT page client-side (SDD-INFRA-005).
- The grid MUST request `pageSize ≤ 200`; it MUST NOT issue a `pageSize` above the backend cap (a `PAGE_SIZE_TOO_LARGE` from the server MUST still surface via the error helper).
- Columns MUST include: document number (mono tabular, `—` while Draft since it is unassigned), document type, direction (AP/AR), counterparty, currency (mono), issue date, due date, status (a `StatusDot` tone), and gross total (mono tabular, right-aligned, with currency). Money/number/code cells MUST render in the mono **tabular** face and be right-aligned (SDD-UI-001 §2.8).
- The default sort MUST be `IssueDate` descending (mirroring SDD-INV-001 §2.10); the grid MUST send the corresponding `sort` term.
- Filter/sort MUST only target the backend opt-in surface (`DocumentNumber`, `DocumentType`, `Direction`, `Status`, `CounterpartyId`, `CurrencyCode`, `IssueDate`, `DueDate`); a free-text `search` box MUST drive the `FilterRequest.search` field.
- The list MUST respect the density store (row height / spacing) and MUST NOT hard-code `size="small"` or fixed padding (SDD-UI-001 §2.4).
- An empty result MUST render the editorial empty state with a single quiet "new invoice" action (SDD-UI-001 §2.8), NOT an error.

### 2.2 View detail (MUST) — `GET /api/v1/invoices/{id}`
- Opening an invoice MUST read `GET /api/v1/invoices/{id}` and render the header (type, direction, counterparty, currency, dates, status, document number once assigned) plus the line table (quantity, unit price, tax rate, line net/tax/gross) and the header totals (net, tax, gross), all money in the mono tabular face.
- The detail MUST display the **server's** computed totals and document number; it MUST NOT recompute or override them for display purposes.
- A `404` (`INVOICE_NOT_FOUND`) MUST surface a translated message via the error helper, not a raw status.

### 2.3 Permission / status gating of actions (MUST)
- Row/detail actions MUST be gated by the invoice **status** so the UI never offers an illegal transition:
  - **Draft** → expose Edit, Confirm, Cancel, Delete.
  - **Confirmed** → expose Post (or "posting…" — §2.6), Cancel. MUST NOT expose Edit/Delete.
  - **Posted** → expose "Create Credit Note" / "Create Debit Note" (correction). MUST NOT expose Edit/Delete/Cancel.
  - **Cancelled** / **Reversed** → expose no mutating action (terminal).
- The backend `[RequirePermission("finance.invoice:<action>")]` (SDD-INT-AUTH-001) is the authoritative gate. A `403` on any action MUST surface the translated forbidden message via `getApiErrorMessage`, never a raw status.
- Because no client-side permission hook exists today, hiding a control by the caller's permission set is OPTIONAL: the UI SHOULD remain functional and rely on the backend 403 + error toast; it MAY hide actions if a permission hook is later introduced (no such hook is in scope for v1).

### 2.4 Create draft (MUST) — `POST /api/v1/invoices` (`finance.invoice:create`)
- The "New invoice" action MUST open `InvoiceFormDialog` in create mode and submit `POST /api/v1/invoices` with `DocumentType`, `CounterpartyId`, `CurrencyCode`, `IssueDate`, `DueDate`, and ≥ 1 line.
- The counterparty and currency selectors MUST load options via `useNomenclature()` (SDD-NOM-001) — options MUST NOT be hard-coded.
- The line editor MUST compute a **client-side PREVIEW** of line net/tax/gross and the header net/tax/gross from `Quantity`, `UnitPrice`, `TaxRate` (decimal, two-dp money, mirroring SDD-INV-001 §2.8) for immediate feedback, but MUST treat the value returned by the server after save as authoritative and MUST re-display the server totals on the persisted invoice. The UI MUST NOT claim its preview is the legal total (the country strategy rounding is server-side, SDD-CTRY-001).
- On success the dialog MUST close, the list cache MUST invalidate, and a success toast MUST show. On failure the dialog MUST stay open and the mapped error toast MUST show (mirroring the journal mutation pattern, which resolves to `null` on failure).
- The created invoice MUST appear in the list as `Draft` with NO document number (the number is assigned only at confirm — §2.5).

### 2.5 Edit draft (MUST) — `PUT /api/v1/invoices/{id}` (`finance.invoice:create`)
- Edit MUST be offered ONLY for `Draft` invoices (§2.3). The dialog MUST submit `PUT /api/v1/invoices/{id}` carrying the line edits and the `InvoiceDto.rowVersion` (base64) for optimistic concurrency.
- A stale `rowVersion` MUST surface `CONCURRENT_MODIFICATION` via the error helper.
- Attempting to edit a non-Draft invoice MUST NOT be reachable from the UI; if the backend rejects with `INVOICE_POSTED_IMMUTABLE` (e.g. status changed underneath), that code MUST surface via the error helper.
- After save the dialog MUST re-display the server-recomputed totals (not the client preview).

### 2.6 Confirm + Post + posting-pending (MUST)
- **Confirm** — `POST /api/v1/invoices/{id}/confirm` (`finance.invoice:confirm`): offered for `Draft` only. On success the invoice moves to `Confirmed` and the server-assigned gapless **document number** MUST now be displayed (it was `—` while Draft). The document-number format is owned by `ICountryStrategy` server-side (SDD-CTRY-001); the UI MUST only display it.
- Confirm of a draft with no lines / mismatched totals MUST surface `INVOICE_LINES_REQUIRED` / `INVOICE_TOTALS_MISMATCH`; confirm of a non-Draft invoice MUST surface `INVOICE_NOT_DRAFT`; confirm into a closed period MUST surface `INVOICE_PERIOD_CLOSED` — all via the error helper.
- **Post** — `POST /api/v1/invoices/{id}/post` (`finance.invoice:post`): offered for `Confirmed`. Posting is the operator-driven completion of the asynchronous Confirm→Post handshake (SDD-INV-001 §2.5).
- **Posting-pending UX (MUST):** A `Confirmed` invoice whose `JournalEntryId` is not yet linked (the Journal-service back-event has not arrived) MUST be presented as **"posting…"** (a pending/in-progress status affordance), and an explicit Post action MUST surface `INVOICE_NOT_CONFIRMED` when the server reports posting is not yet linked, prompting a retry. Once `JournalEntryId` is linked the invoice MUST present as `Posted` (the UI MAY poll/refetch via TanStack Query to observe the transition). The UI MUST NOT assume immediate synchronous posting.
- Money figures throughout MUST render in the mono tabular face with their currency; the document number MUST render in mono.

### 2.7 Cancel (MUST) — `POST /api/v1/invoices/{id}/cancel` (`finance.invoice:cancel`)
- Cancel MUST be offered for `Draft`/`Confirmed` only. It MUST open a **reason-prompt** dialog (mirroring `ReverseJournalEntryDialog`/`ReasonPromptDialog`) requiring a **non-empty** `Reason`; the submit MUST be disabled until a reason is entered.
- An empty reason that reaches the server MUST surface `INVOICE_CANCEL_REASON_REQUIRED`; cancelling a `Posted`/`Cancelled`/`Reversed` invoice (not reachable from the UI, but defensively) MUST surface `INVALID_INVOICE_STATE_TRANSITION` via the error helper.
- A cancelled Confirmed invoice MUST keep its document number on display (numbers are gapless and never recycled, SDD-INV-001 §2.6); the UI MUST NOT blank it.

### 2.8 Delete draft (MUST) — `DELETE /api/v1/invoices/{id}` (`finance.invoice:create`)
- Delete MUST be offered for `Draft` only, behind a destructive `ConfirmDialog`. Attempting to delete a non-Draft invoice MUST surface `INVOICE_POSTED_IMMUTABLE` via the error helper.

### 2.9 Credit/Debit-Note correction (MUST)
- For a `Posted` invoice the UI MUST offer "Create Credit Note" / "Create Debit Note". This MUST open `InvoiceFormDialog` pre-set with the corresponding `DocumentType` (CreditNote/DebitNote) and `CorrectsInvoiceId` = the original's id, then follow the normal create→confirm→post flow for the note (SDD-INV-001 §2.7).
- The UI MUST NOT mutate the original posted invoice's lines, totals, or number when issuing a note (the original stays `Posted` — the automatic full-offset `Reversed` transition is DEFERRED backend, §1.4). The note appears as a separate document in the list, linked by `CorrectsInvoiceId`.

### 2.10 Warehouse-created drafts (MUST) — SDD-INT-WH-001
- System-created **draft** invoices that arrive from Warehouse events (GoodsReceiptCompleted → draft Purchase Invoice, ShipmentCompleted → draft Sale Invoice, CustomerReturnCompleted → draft Credit Note, SupplierReturnShipped → draft Debit Note) MUST appear in the SAME list as manual drafts (they are created via the same create path, SDD-INT-WH-001) and MUST be reviewable/completable by the operator (edit + confirm) before they are confirmed.
- The UI SHOULD visually distinguish a Warehouse-originated draft and SHOULD display its source-document origin (`SourceDocumentType` / `SourceDocumentId`) when the `InvoiceDto` carries those fields, so the operator knows the draft was system-created. If the DTO does not expose source fields, the UI MUST still list the draft (origin display is best-effort, not blocking).

### 2.11 Correlation, density, i18n cross-cutting (MUST)
- Every outbound request from this feature MUST carry a fresh `X-Correlation-ID` via the shared axios interceptor (SDD-INFRA-001 / SDD-UI-001 §2.2); the feature MUST NOT instantiate raw `axios`/`fetch`.
- Every visible string MUST come from `t('invoices.*')` / `t('errors.*')` / shared keys, present in BOTH `en.ts` and `bg.ts` (§6).
- Every grid/dialog/table/field MUST read density from `useLayoutStore` (SDD-UI-001 §2.4).

### 2.12 Edge cases (MUST)
- **Document number only after confirm.** A `Draft` row MUST show `—` (or empty) for document number; the number MUST appear only once the invoice is `Confirmed`/later.
- **Posting-pending.** A `Confirmed` invoice without a linked `JournalEntryId` MUST show "posting…", and an explicit Post MUST surface `INVOICE_NOT_CONFIRMED` until linked (§2.6).
- **Server total overrides client preview.** After create/edit, if the server's persisted totals differ from the client preview (rounding via the country strategy), the UI MUST display the SERVER values, not the preview.
- **Cancel without reason.** The reason-prompt submit MUST stay disabled with an empty/whitespace reason; a server `INVOICE_CANCEL_REASON_REQUIRED` MUST surface via the error helper.
- **Immutability surfaced.** Edit/Delete MUST NOT be offered for `Confirmed`/`Posted`/`Cancelled`/`Reversed`; if the backend returns `INVOICE_POSTED_IMMUTABLE`, it MUST surface via the error helper.
- **EN/BG parity.** Switching locale MUST re-render all invoice strings with no raw key path visible in either locale.

## 3. Validation Rules (client-side zod / form — mirrors SDD-INV-001 §3.1; server authoritative)

> The form MUST mirror the backend shape so the operator gets immediate feedback, but the backend remains authoritative; every server validation error MUST still surface via `getApiErrorMessage` (§5). Validation messages MUST be i18n keys (`invoices.validation.*`), mirroring the journal schema pattern.

### 3.1 Field-level (zod)

| Field | Client rule | Mirrors backend code (surfaced if server rejects) |
|---|---|---|
| `DocumentType` | Required; one of `PurchaseInvoice`/`SaleInvoice`/`CreditNote`/`DebitNote` | `INVALID_INVOICE_DOCUMENT_TYPE` |
| `CounterpartyId` | Required (non-empty) | `INVOICE_COUNTERPARTY_REQUIRED` |
| `CurrencyCode` | Required; ISO 4217 (3 chars) | `INVALID_INVOICE_CURRENCY` |
| `IssueDate` | Required | `INVALID_INVOICE_DATE` |
| `DueDate` | Required; `DueDate ≥ IssueDate` | `INVALID_INVOICE_DUE_DATE` |
| `Lines` | Manual create: ≥ 1 line | `INVOICE_LINES_REQUIRED` |
| `Lines[].Quantity` | `> 0` | `INVALID_INVOICE_LINE` |
| `Lines[].UnitPrice` | `≥ 0` | `INVALID_INVOICE_LINE` |
| `Lines[].TaxRate` | `≥ 0` (a rate the country strategy recognizes — server validates) | `INVALID_INVOICE_TAX_RATE` |
| Cancel `Reason` | Non-empty / non-whitespace | `INVOICE_CANCEL_REASON_REQUIRED` |

### 3.2 Cross-field (zod `superRefine`)
- `DueDate ≥ IssueDate` MUST be enforced cross-field (a single message keyed `invoices.validation.dueDateBeforeIssue`).
- The client total PREVIEW (`Σ LineNet`, `Σ LineTax`, `Σ LineGross`, with `Gross = Net + Tax`) MUST be shown for feedback; the UI MUST NOT block submission on its own total computation (the server recomputes and is authoritative — SDD-INV-001 §2.8). `INVOICE_TOTALS_MISMATCH` is a defensive server code and MUST be surfaced via the error helper if returned, but the client MUST NOT attempt to reproduce it as a blocking rule.

### 3.3 State-based (UI gating — §2.3)
- Edit/Delete MUST be offered for `Draft` only; Confirm for `Draft` only; Post for `Confirmed` only; Cancel for `Draft`/`Confirmed`; note creation for `Posted` only. The UI MUST NOT offer an action whose backend transition would be rejected.

## 4. Error Rules

UI errors are i18n keys under `errors.*`. Mapping is per SDD-UI-001 §2.5 / SDD-INFRA-001: `getApiErrorMessage(err, t)` looks up `errors.<title>` (the ProblemDetails `title` = the SCREAMING_SNAKE_CASE code); if absent, falls back to ProblemDetails `detail`; if absent, `errors.GENERIC_ERROR`. Components MUST NOT render `err.message`, `err.response.status`, or raw `data.detail`.

**All 17 `InvoiceErrorCodes` MUST have a matching `errors.<CODE>` entry in BOTH `frontend/src/shared/i18n/locales/en.ts` and `bg.ts`.** These 17 keys WERE ALREADY ADDED in this batch (verified present at `en.ts`/`bg.ts` lines 357–373); the frontend phase MUST keep them in sync and MUST NOT remove them. `CONCURRENT_MODIFICATION` (from `CommonErrorCodes`) MUST also have an entry (already present from the Accounts/Currencies batch).

| Code | HTTP | Trigger (from SDD-INV-001 §4) | UI treatment |
|---|---|---|---|
| `INVOICE_NOT_FOUND` | 404 | Invoice id does not exist (get/detail/action) | Toast |
| `INVOICE_LINES_REQUIRED` | 400 | Manual create/confirm with zero lines | Inline (lines field) + toast on confirm |
| `INVALID_INVOICE_DOCUMENT_TYPE` | 400 | Missing/unknown document type | Inline (type field) |
| `INVOICE_COUNTERPARTY_REQUIRED` | 400 | Missing counterparty | Inline (counterparty field) |
| `INVALID_INVOICE_CURRENCY` | 400 | Missing/invalid currency code | Inline (currency field) |
| `INVALID_INVOICE_DATE` | 400 | Missing/invalid issue date | Inline (issue-date field) |
| `INVALID_INVOICE_DUE_DATE` | 400 | Due date missing or before issue date | Inline (due-date field) |
| `INVALID_INVOICE_LINE` | 400 | Line quantity ≤ 0 or unit price < 0 | Inline (line row) |
| `INVALID_INVOICE_TAX_RATE` | 400 | Tax rate negative / not recognized | Inline (line tax-rate field) |
| `INVOICE_TOTALS_MISMATCH` | 400 | Lines do not reconcile (defensive server code) | Toast |
| `INVOICE_NOT_DRAFT` | 409 | Confirm/edit on a non-Draft invoice | Toast |
| `INVOICE_NOT_CONFIRMED` | 409 | Post on a non-Confirmed invoice / posting not yet linked (**posting-pending**) | Toast (retry hint) |
| `INVOICE_POSTED_IMMUTABLE` | 409 | Edit/delete on a Confirmed/Posted/Cancelled/Reversed invoice | Toast |
| `INVALID_INVOICE_STATE_TRANSITION` | 409 | Transition not allowed (e.g. cancel a posted invoice) | Toast |
| `INVOICE_PERIOD_CLOSED` | 409 | Issue date in a closed period | Toast |
| `INVOICE_DUPLICATE_DOCUMENT_NUMBER` | 409 | Confirm/replay would assign a second number | Toast |
| `INVOICE_CANCEL_REASON_REQUIRED` | 400 | Cancel without a non-empty reason | Inline (reason field) + toast |
| `CONCURRENT_MODIFICATION` | 409 | Stale `rowVersion` on update/confirm/cancel | Toast |

- Inline-treated codes (validation, 400) SHOULD map to the offending form field where the field is known; all codes MUST also be safe to surface as a toast via the error helper (the helper is the unconditional fallback).
- A forced server 500 / unmapped code MUST fall back to `errors.GENERIC_ERROR` (never a raw key path).

## 5. i18n

- All new invoice strings MUST be `t('invoices.*')` keys present in BOTH `en.ts` and `bg.ts` in the SAME PR (SDD-UI-001 §2.3). Key groups:
  - **Titles / nav:** `invoices.title`, `invoices.newInvoice`, `invoices.detailTitle`, `invoices.searchPlaceholder`, `invoices.empty`, `invoices.emptyHint`.
  - **Columns:** `invoices.documentNumber`, `invoices.documentType`, `invoices.direction`, `invoices.counterparty`, `invoices.currency`, `invoices.issueDate`, `invoices.dueDate`, `invoices.status`, `invoices.netTotal`, `invoices.taxTotal`, `invoices.grossTotal`.
  - **Document types:** `invoices.type_PurchaseInvoice`, `invoices.type_SaleInvoice`, `invoices.type_CreditNote`, `invoices.type_DebitNote`.
  - **Direction:** `invoices.direction_AP`, `invoices.direction_AR`.
  - **Statuses:** `invoices.status_Draft`, `invoices.status_Confirmed`, `invoices.status_Posting` (the posting-pending affordance), `invoices.status_Posted`, `invoices.status_Cancelled`, `invoices.status_Reversed`.
  - **Actions:** `invoices.confirm`, `invoices.post`, `invoices.cancel`, `invoices.delete`, `invoices.edit`, `invoices.createCreditNote`, `invoices.createDebitNote`.
  - **Dialogs / confirmations:** `invoices.confirmTitle`/`invoices.confirmMessage`, `invoices.postTitle`/`invoices.postMessage`, `invoices.cancelTitle`/`invoices.cancelReasonLabel`, `invoices.deleteTitle`/`invoices.deleteMessage`, `invoices.created`/`invoices.updated`/`invoices.confirmed`/`invoices.posted`/`invoices.cancelled`/`invoices.deleted`.
  - **Validation:** `invoices.validation.documentTypeRequired`, `.counterpartyRequired`, `.currencyRequired`, `.issueDateRequired`, `.dueDateRequired`, `.dueDateBeforeIssue`, `.minOneLine`, `.quantityPositive`, `.unitPriceNonNegative`, `.taxRateNonNegative`, `.cancelReasonRequired`.
  - **Source origin (SDD-INT-WH-001):** `invoices.sourceOrigin`, `invoices.systemCreated`.
  - **Errors:** the 17 `errors.<CODE>` entries above (already present) + `errors.CONCURRENT_MODIFICATION` + `errors.GENERIC_ERROR` (already present).
- EN and BG MUST stay key-for-key in sync; an `I18n_AllKeysExistInEnAndBg`-style parity check MUST cover every new `invoices.*` key (§7).

## 6. Versioning Notes

- **v1 — Initial specification (Drafted).** The Invoices SPA feature surface consuming all 8 `SDD-INV-001` endpoints in dialog-mode: list (paged `FilterRequest`), detail, create/edit draft, confirm, post (+ posting-pending), cancel (reason required), delete draft, Credit/Debit-Note correction (`CorrectsInvoiceId`), and surfacing of Warehouse-created drafts (SDD-INT-WH-001). Built on the SDD-UI-001 ledger shell (theme, density, EN+BG i18n, axios + `X-Correlation-ID`, error helper), with nomenclature-backed counterparty/currency dropdowns (SDD-NOM-001) and the 17 `InvoiceErrorCodes` mapped to EN+BG `errors.*` keys. RBAC is enforced by the backend `finance.invoice:*` permissions (SDD-INT-AUTH-001), with action availability gated by entity status in the UI.
- **Deferred (future versions / specs):** payments/settlement UI (`SDD-PAY-*`), reporting/VAT-journal UI (`SDD-RPT-003`), the automatic `Posted → Reversed` full-offset presentation (deferred backend), bulk operations, approval/maker-checker, page-mode forms (`SDD-UI-002`), the per-aggregate audit-log panel, and counterparty name enrichment (`SDD-INT-WH-002`).
- Changing a consumed endpoint contract, the status set, or the error-code set is a **breaking** change that originates in the owning backend spec (`SDD-INV-001`) and requires a coordinated update here (new `errors.*` keys in both locales, new wire types). Adding a column, a filter, or a display affordance is **non-breaking/additive**.

## 7. Test Plan

> Vitest component/unit tests live under `frontend/src/components/pages/InvoicesListPage.test.tsx` + `frontend/src/features/invoices/schema.test.ts` (mirroring the existing journal/periods test files), rendered via `src/test/renderWithProviders.tsx`. `ui-validate` golden-path checks drive the live SPA + gateway via Chrome DevTools MCP (Phase 7). Business tests SHOULD reference `[Category]`-equivalent spec tagging in their describe/title (`SDD-UI-FIN-001`).

### 7.1 Vitest component / unit

| Test name | Kind |
|---|---|
| `InvoicesList_Authenticated_RendersPagedResultEnvelope` — list reads `{ items, totalCount, page, pageSize }`, not a bare array | [Unit] |
| `InvoicesList_FilterSortPage_SendsFilterRequestParams_ServerSidePaging` — paging/sort issues `FilterRequest`; no client-side paging; `pageSize ≤ 200` | [Unit] |
| `InvoicesList_DraftRow_ShowsDashForDocumentNumber` — Draft shows `—`; Confirmed shows the assigned number | [Unit] |
| `InvoicesList_ActionsGatedByStatus_DraftEditDeleteConfirm_ConfirmedPostCancel_PostedNoteOnly` — status gates which actions render (§2.3) | [Unit] |
| `InvoicesList_PostingPending_ShowsPostingAffordance` — Confirmed without `JournalEntryId` renders "posting…" | [Unit] |
| `InvoiceForm_MissingCounterparty_ShowsCounterpartyRequired` — zod field validation (`invoices.validation.counterpartyRequired`) | [Unit] |
| `InvoiceForm_DueDateBeforeIssue_ShowsDueDateError` — cross-field rule | [Unit] |
| `InvoiceForm_NoLines_ShowsMinOneLine` — manual create requires ≥ 1 line | [Unit] |
| `InvoiceForm_NegativeQuantityOrTaxRate_ShowsLineError` — line-level validation | [Unit] |
| `InvoiceForm_TotalsPreview_MatchesLineSums_ServerOverridesAfterSave` — preview shown, server totals re-displayed after save | [Unit] |
| `InvoiceMutations_Create_InvalidatesListAndShowsSuccess` — create success path | [Unit] |
| `InvoiceMutations_Confirm_NotDraft_ShowsInvoiceNotDraftToast` — `INVOICE_NOT_DRAFT` mapped | [Unit] |
| `InvoiceMutations_Post_PostingNotLinked_ShowsInvoiceNotConfirmedToast` — `INVOICE_NOT_CONFIRMED` mapped (posting-pending) | [Unit] |
| `InvoiceMutations_Edit_StaleRowVersion_ShowsConcurrentModificationToast` — `CONCURRENT_MODIFICATION` mapped | [Unit] |
| `InvoiceCancel_EmptyReason_SubmitDisabled_AndServerCodeMapped` — reason required + `INVOICE_CANCEL_REASON_REQUIRED` | [Unit] |
| `InvoiceImmutability_PostedRow_NoEditOrDelete_ImmutableCodeMapped` — `INVOICE_POSTED_IMMUTABLE` mapped | [Unit] |
| `InvoiceNote_FromPostedInvoice_OpensFormWithCorrectsInvoiceId` — credit/debit note pre-set with `CorrectsInvoiceId` | [Unit] |
| `InvoiceList_WarehouseDraft_ShowsSourceOriginWhenPresent` — SDD-INT-WH-001 origin surfaced when DTO carries source fields | [Unit] |
| `InvoiceError_UnmappedCode_FallsBackToGenericError` — never a raw key path | [Unit] |
| `Invoices_I18n_AllKeysExistInEnAndBg` — every `invoices.*` + the 17 `errors.<INVOICE_CODE>` keys present in both locales | [Unit] |

### 7.2 `ui-validate` (Chrome DevTools MCP — live SPA + gateway)

| Check | Kind |
|---|---|
| `InvoicesUi_Boot_NavToInvoices_RendersList` — app boots, navigate to Invoices, list renders the paged envelope | [UI] |
| `InvoicesUi_I18n_NoRawKeys_EnAndBg` — switch EN↔BG; no raw `invoices.*`/`errors.*` key path renders in either locale | [UI] |
| `InvoicesUi_Density_CompactTightensGrid` — density toggle flows from `useLayoutStore` to the grid/dialogs | [UI] |
| `InvoicesUi_CreateDialog_ClientValidation_BlocksInvalidSubmit` — zod validation surfaces before the request | [UI] |
| `InvoicesUi_Confirm_AssignsAndDisplaysDocumentNumber` — confirm a draft → server number appears (was `—`) | [UI] |
| `InvoicesUi_PostingPending_ShowsPostingThenPosted` — confirmed → "posting…" → `Posted` once linked (refetch) | [UI] |
| `InvoicesUi_Cancel_RequiresReason_AndMapsServerErrors` — reason-prompt + mapped error toast | [UI] |
| `InvoicesUi_Note_FromPostedInvoice_CreatesLinkedCreditNote` — correction flow leaves the original `Posted` | [UI] |
| `InvoicesUi_RbacHiddenActions_403SurfacesTranslated` — a 403 on an action surfaces the translated forbidden message (never a raw status) | [UI] |
| `InvoicesUi_CorrelationId_EveryRequestHasUniqueHeader` — every outbound invoice request carries a unique `X-Correlation-ID` | [UI] |
| `InvoicesUi_ErrorToast_MappedNotRaw` — a forced server error surfaces the mapped `errors.*` message, never `err.message`/raw `detail`/status | [UI] |
