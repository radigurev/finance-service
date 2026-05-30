# SDD-UI-001 — Frontend Shell (React + MUI + i18n + Density + Ledger Theme)

> Status: Active (Batch 8 — ledger aesthetic + Atomic Design folders + shared hooks; Accounts adopts the PagedResult/FilterRequest contract with CRUD dialogs; Currencies CRUD + exchange-rate view + `useNomenclature()` dropdowns ship)
> Owner: Frontend
> Related: SDD-INT-AUTH-001, SDD-INFRA-002, SDD-ACCT-001, SDD-NOM-001, SDD-INFRA-001, SDD-INFRA-005, SDD-AUDIT-001, SDD-UI-002
> ISA-95: Cross-cutting

---

## 1. Context

The Finance frontend is a single React 18 + TypeScript + Vite SPA served from `Finance.Frontend` (Nginx in production, Vite dev server locally). It calls every backend through the `Finance.Gateway` at `/api/v1/*`. Tokens, language, and density preferences are persisted in `localStorage` via Zustand `persist`.

The shell ships in Phase 0 with: login, an authenticated app shell, and one feature surface (Chart of Accounts list). It mirrors Warehouse's frontend rules (`SDD-UI-001` Vue equivalent) — same patterns, React syntax.

### 1.1 Batch 8 scope (this revision)

Batch 8 upgrades the shell from default Material to a **distinctive "ledger" aesthetic** and grows the first two feature surfaces (Accounts, Currencies) onto the post-Batch-4/5 backend contracts. The covered scope is:

- A **custom MUI theme** ("ledger") replacing default Material — see §2.8. Light mode only (dark deferred).
- **Atomic Design folders** introduced under `src/components/` (`atoms/`, `molecules/`, `organisms/`, `templates/`, `pages/`).
- **Shared hooks** under `src/shared/hooks/` — `useGoBack`, `useNomenclature`, and a `notification` helper (notistack) feeding `notification.error(getApiErrorMessage(err, t))`.
- **Accounts list** adapts to the `PagedResult<AccountDto>` envelope + `FilterRequest` server-side filter/sort/page, with create/edit **dialog** forms (react-hook-form + zod). `AccountDto.rowVersion` (base64) carries optimistic concurrency on update.
- **Currencies CRUD** + an **exchange-rate read view** (latest + range) added as a second feature surface.
- **`useNomenclature()`** backs country / state / city (Warehouse proxy) and currency dropdowns — no hard-coded options.
- EN + BG locale files kept in sync, including every new `errors.<CODE>` entry.

**Explicitly DEFERRED (out of scope this batch — no overengineering):**

- The full `SDD-UI-002` dialog-vs-page dual-mode machinery and the `isPageMode` layout flag. Batch 8 ships clean dialog-based create/edit forms only. A lightweight `useGoBack` hook IS provided (good practice) but no page-mode routes/pages are built.
- The per-aggregate **audit-log panel** and EventLog viewer. The audit READ endpoint (`GET /api/v1/audit/export`) is deferred in `SDD-AUDIT-001`, so no audit UI ships.

## 2. Behavior

### 2.1 Authentication (MUST)
- Unauthenticated users MUST be redirected to `/login`.
- Login POSTs `username` + `password` to `/api/v1/auth/login` via the gateway.
- On success, the response (`accessToken`, `refreshToken`, `username`) MUST be persisted in the `finance.auth` Zustand store.
- The Axios instance MUST attach `Authorization: Bearer <accessToken>` on every outbound request when a token is present.
- On 401 response, the auth store MUST clear and the user MUST be redirected to `/login` on the next render.

### 2.2 Correlation IDs (MUST)
- The shared Axios instance MUST attach a fresh `X-Correlation-ID` (UUID v4) header on EVERY outbound request via a request interceptor.
- Components MUST NOT call `fetch` or instantiate raw `axios` — they MUST import `api` from `@/shared/api/axios`.

### 2.3 i18n (MUST)
- Every visible string MUST come from `t('key.path')`.
- Every key MUST exist in BOTH `frontend/src/shared/i18n/locales/en.ts` and `frontend/src/shared/i18n/locales/bg.ts`. Adding or renaming a key requires updating both files in the SAME PR.
- A backend error code added to `Finance.Common.ErrorCodes.*` MUST have a matching `errors.<CODE>` entry in both locale files.
- The language switcher in the app shell MUST persist selection via `i18next-browser-languagedetector` (localStorage).
- Supported languages: `en`, `bg`. Fallback: `en`.

### 2.4 Layout density (MUST)
- The app shell MUST expose a density toggle (compact / standard).
- The selection MUST be persisted in the `finance.layout` Zustand store.
- Every MUI `DataGrid` MUST read `density` from `useLayoutStore`. Components that have natural padding (Card, container) MUST switch spacing classes based on `isCompact`.
- Components MUST NOT hard-code `size="small"` or fixed padding values that ignore the layout store.

### 2.5 API errors (MUST)
- Components MUST forward Axios errors through `getApiErrorMessage(err, t)` from `@/shared/utils/getApiErrorMessage.ts`.
- The helper MUST look up `errors.<title>` from i18n; if absent, MUST fall back to ProblemDetails `detail`; if absent, MUST show `errors.GENERIC_ERROR`.
- Components MUST NOT show `err.message`, `err.response.status`, or raw `data.detail` directly.

### 2.6 Folder structure (MUST)
- `src/app/` — shell, router providers.
- `src/features/<feature>/` — per-feature pages, hooks, components.
- `src/shared/` — stores, hooks, utils, api client, i18n, theme.
- Atomic Design folders MUST exist under `src/components/`: `atoms/`, `molecules/`, `organisms/`, `templates/`, `pages/`. Reusable building blocks (Panel surface, status dot, ledger DataGrid wrapper, form fields) live here; feature-specific composition stays under `src/features/`.
- Shared cross-feature hooks MUST live under `src/shared/hooks/` — at minimum `useGoBack`, `useNomenclature`, and the `notification` helper.

### 2.7 Routing (MUST)
- `/login` — unauthenticated landing.
- `/` — root, redirects to `/accounts` when authenticated.
- `/accounts` — Chart of Accounts list.
- `/currencies` — Currencies list + exchange-rate view.
- Catch-all redirects to `/`.
- Create/edit for both Accounts and Currencies MUST open as **dialogs** over the list; this batch MUST NOT introduce dedicated `*CreatePage`/`*EditPage` routes (those belong to the deferred `SDD-UI-002`).

### 2.8 Ledger theme (MUST)
The SPA MUST ship a custom MUI theme ("ledger"), not default Material. The theme is editorial, print-like, and hairline-ruled. Light mode only; dark mode is deferred.

- **Palette (MUST):** background paper `#FAF9F6`; surface `#FFFFFF`; text primary (ink) `#18181B`; text secondary (warm gray) `#57534E`; divider/hairline `#E7E5E0`; primary (deep green) `#1B5E3A`; accent-soft `#DCEAE0`; error/danger (oxblood) `#9F1239`; warning `#B45309`; success/positive `#1B5E3A`. Material blue (`#1976d2` / `#1565c0`) MUST NOT appear anywhere.
- **Typography (MUST):** fonts loaded offline via `@fontsource`. Headings = Fraunces (serif, restrained weights, optical sizing). Body/UI = Inter. Monospace face for codes/figures/money = IBM Plex Mono with tabular figures (`font-feature-settings: "tnum"`). Money amounts, account codes, currency codes and IDs MUST render in the mono tabular face and MUST be right-aligned in tables.
- **Surfaces (MUST):** NO `elevation` / `box-shadow` on any surface. The shadowed `Card` is replaced by a "Panel" surface = 1px hairline border (`#E7E5E0`) on `#FFFFFF`, `borderRadius` 8, generous padding. Tables/headers use `borderRadius` 0–4.
- **AppBar/shell (MUST):** paper-colored top bar with a single hairline bottom border — no fill, no shadow, no blue. Serif "Finance" wordmark in ink. Nav = quiet text links; the active link is marked by a 2px green underline (not a filled button). Density toggle, EN/BG switch, and logout are understated icon/text controls.
- **DataGrid (MUST):** column headers UPPERCASE, letter-spacing ~`0.08em`, text-secondary color, paper header background (not gray); hairline row separators; no zebra striping (or a barely-there tint); no outer elevation, a single hairline frame; money/code cells right-aligned in the mono tabular face; row height respects density.
- **Buttons (MUST):** primary = solid deep-green, NO shadow, `borderRadius` 4, subtle letter-spacing; secondary = hairline-outlined or text. NO gradients, glow, or pill shapes.
- **Inputs (MUST):** outlined with hairline borders; focus border = green; labels in text-secondary; helper/error text in oxblood.
- **Status (SHOULD):** small colored dot + label (green = Active, warm-gray = Inactive), NOT large chips.
- **Empty/error states (SHOULD):** editorial thin-ruled box, centered understated message + a single quiet action.
- **Prohibited (MUST NOT):** drop shadows/elevation on cards, gradient hero banners, glassmorphism/blur, glowing accents, default Material blue, ubiquitous pill buttons, emoji in UI, gradient stat-cards.

### 2.9 Accounts list — paged contract + CRUD (MUST)
- The Accounts list MUST consume `GET /api/v1/accounts` as a `PagedResult<AccountDto>` envelope `{ items, totalCount, page, pageSize }` and MUST send `FilterRequest` query params (filters / sort / page / pageSize / search). Server-side filter/sort/paging is authoritative; the grid MUST NOT page client-side.
- Create and edit MUST open as dialog forms (react-hook-form + zod) and submit to `POST` / `PUT /api/v1/accounts` (permission `finance.account:write`).
- On update, the request MUST carry `AccountDto.rowVersion` (base64 string) for optimistic concurrency. A concurrency conflict from the backend (`CONCURRENT_MODIFICATION`) MUST surface through `notification.error(getApiErrorMessage(err, t))`.
- All list/error rendering MUST respect density (§2.4) and use the shared axios instance (§2.2) and error helper (§2.5).

### 2.10 Currencies + exchange rates (MUST)
- A Currencies feature surface MUST list `GET /api/v1/currencies` as `PagedResult<CurrencyDto>` with `FilterRequest`, and provide create/edit dialog forms submitting `POST` / `PUT /api/v1/currencies` (permission `finance.nomenclature:write`).
- An exchange-rate read view MUST display the latest rate (`GET /api/v1/exchange-rates/latest?currency=&date=`) and a range (`GET /api/v1/exchange-rates/range?currency=&from=&to=`). Rates render in the mono tabular face (`DECIMAL(18,6)` semantics).
- The exchange-rate view is read-only — rate WRITE / BNB import is out of scope (`SDD-NOM-001`, `SDD-INT-BNB-001`).

### 2.11 Nomenclature dropdowns (MUST)
- Country / state / city and currency dropdowns MUST load through the shared `useNomenclature()` hook (`src/shared/hooks/`), backed by `Finance.Nomenclature.API`: `GET /api/v1/countries`, `/states?country=`, `/cities?stateId=` (Warehouse proxy) and `GET /api/v1/currencies`.
- Dropdown options MUST NOT be hard-coded. State and city selects MUST cascade (state requires a selected country; city requires a selected state).

### 2.12 Navigation helper (SHOULD)
- A lightweight `useGoBack` hook MUST be provided under `src/shared/hooks/`. Dialog-based forms close in place; any future back-navigation surface SHOULD call `useGoBack({ fallback: { name: '<listing>' } }).goBack()`.
- This batch MUST NOT implement the `SDD-UI-002` `isPageMode` dual-mode machinery; the hook exists for good practice and future page-mode reuse only.

## 3. Validation

- Login form requires `username` and `password` — basic HTML5 `required`.
- Account create/edit dialog (react-hook-form + zod) MUST require `code`, `name`, `type` (one of Asset / Liability / Equity / Revenue / Expense) and `countryCode`; `parentId` is optional. Client validation mirrors the backend shape (`SDD-ACCT-001`); the backend remains authoritative.
- Currency create/edit dialog MUST require `code` (ISO 4217 3-letter) and `name`. Client validation mirrors `SDD-NOM-001`; the backend remains authoritative.
- Exchange-rate range view MUST require a selected currency and a valid `from`/`to` range (`from <= to`) before issuing the request; an invalid range surfaces the backend `INVALID_DATE_RANGE` code via the error helper.
- Cascading dropdowns: a state cannot be selected without a country; a city cannot be selected without a state (§2.11).

## 4. Error Rules

UI-side errors are i18n keys under `errors.*`. Mapping is described in §2.5.

- Every `catch` block in feature hooks MUST forward through `notification.error(getApiErrorMessage(err, t))` (notistack). Components MUST NOT render `err.message`, `err.response.status`, or raw `data.detail`.
- Backend error codes consumed by Batch 8 surfaces MUST have matching `errors.<CODE>` entries in BOTH `en.ts` and `bg.ts`: at minimum `CONCURRENT_MODIFICATION` (Accounts/Currencies optimistic concurrency), `DUPLICATE_CURRENCY_CODE`, `INVALID_CURRENCY_CODE`, `CURRENCY_NOT_FOUND`, `INVALID_DATE_RANGE`, `EXCHANGE_RATE_NOT_FOUND`, plus the existing Accounts codes. Missing/untranslated codes MUST fall back to `errors.GENERIC_ERROR` (never the raw key path).
- A `403` (missing permission) on a write MUST surface the translated permission/forbidden message via the helper, never a raw status.

## 5. Versioning

- **v1 — Initial specification.** Phase 0 shell: login, authenticated app shell, Chart of Accounts list, default Material theme, i18n (EN/BG), density toggle, correlation IDs, Axios error helper.
- **v2 — Batch 8 (non-breaking, additive).** Promoted to Active. Adds: the custom "ledger" MUI theme (§2.8) replacing default Material; Atomic Design folders under `src/components/` and shared hooks under `src/shared/hooks/` (§2.6); the `PagedResult`/`FilterRequest` Accounts contract + create/edit dialogs with `rowVersion` optimistic concurrency (§2.9); the Currencies CRUD + exchange-rate read view (§2.10); `useNomenclature()`-backed cascading country/state/city + currency dropdowns (§2.11); a `useGoBack` hook (§2.12); new `errors.*` codes synced across EN/BG (§4). The visual swap from default Material to the ledger theme is intentional and is not considered a behavioral break. The `SDD-UI-002` dual-mode/`isPageMode` machinery and the per-aggregate audit-log panel remain DEFERRED. Future shell-wide capabilities (theme switcher, dark mode, page-mode forms) are added via `CHG-ENH-*` / `SDD-UI-002`.

## 6. Test Plan

UI behavior is verified by the `ui-validate` agent (Chrome DevTools MCP) against the running SPA. Each scenario is marked `[Integration]` because it drives the live SPA + gateway; theme/locale invariants that can be asserted against the built bundle / theme object are marked `[Unit]`.

Required tests:

- `Login_ValidCredentials_RedirectsToAccounts` — login golden path. `[Integration]`
- `Accounts_Unauthenticated_RedirectsToLogin` — auth guard. `[Integration]`
- `AccountsList_Authenticated_RendersPagedResultEnvelope` — list reads `{ items, totalCount, page, pageSize }`, not a bare array. `[Integration]`
- `AccountsList_FilterSortPage_SendsFilterRequestParams_ServerSidePaging` — grid paging/sort issues `FilterRequest` query params; no client-side paging. `[Integration]`
- `AccountCreateDialog_ValidInput_PostsAndRefreshesList` — create dialog submits `POST /accounts`. `[Integration]`
- `AccountEditDialog_StaleRowVersion_ShowsConcurrentModificationToast` — `CONCURRENT_MODIFICATION` surfaces via `getApiErrorMessage`. `[Integration]`
- `CurrenciesList_Authenticated_RendersPagedResultEnvelope` — currencies list paged contract. `[Integration]`
- `CurrencyCreateDialog_DuplicateCode_ShowsDuplicateCurrencyCodeToast` — `DUPLICATE_CURRENCY_CODE` translated. `[Integration]`
- `ExchangeRateView_LatestAndRange_RendersRatesInMonoTabularFace` — latest + range read views; rates right-aligned mono. `[Integration]`
- `ExchangeRateRange_InvalidRange_ShowsInvalidDateRangeToast` — `from > to` surfaces `INVALID_DATE_RANGE`. `[Integration]`
- `Nomenclature_CountryStateCity_CascadesAndLoadsFromProxy` — `useNomenclature()` cascading dropdowns hit the proxy endpoints; no hard-coded options. `[Integration]`
- `LanguageSwitch_EnToBg_ReRendersAllHeadings` — i18n switch. `[Integration]`
- `DensityToggle_Compact_TightensGridAndPanelSpacing` — density flows from `useLayoutStore`. `[Integration]`
- `ApiError_ServerFailure_ShowsTranslatedGenericError` — forced 500 → `errors.GENERIC_ERROR` (never raw key). `[Integration]`
- `CorrelationId_EveryOutboundRequest_HasUniqueHeader` — each request carries a unique `X-Correlation-ID`. `[Integration]`
- `Theme_NoMaterialBluePresent` — built theme palette contains no `#1976d2`/`#1565c0`; primary is `#1B5E3A`. `[Unit]`
- `Theme_NoElevationOnSurfaces` — Panel/Card overrides emit no `box-shadow`/`elevation`. `[Unit]`
- `Theme_TypographyUsesFrauncesInterIbmPlexMono` — heading/body/mono font families resolve to the configured `@fontsource` faces with `tnum`. `[Unit]`
- `I18n_AllKeysExistInEnAndBg` — every key (incl. new `errors.*` codes) present in both locale files. `[Unit]`

## 7. Open Items

- Refresh-token rotation flow — currently 401 → logout; a later batch will swap to a silent refresh on 401.
- **DEFERRED:** Modal-vs-page dual-mode forms + `isPageMode` (`SDD-UI-002`). Batch 8 ships dialog-only CRUD; `useGoBack` is provided for future page-mode reuse. No `*CreatePage`/`*EditPage` routes this batch.
- **DEFERRED:** Per-aggregate audit-log panel + EventLog viewer. Blocked on the audit READ endpoint deferred in `SDD-AUDIT-001` (`GET /api/v1/audit/export`).
- Dark mode + theme/brand-color switcher (Warehouse `SDD-UI-001` equivalent) — later phase via `CHG-ENH-*`.
