# SDD-UI-001 — Frontend Shell (React + MUI + i18n + Density)

> Status: Draft
> Owner: Frontend
> Related: SDD-INT-AUTH-001, SDD-INFRA-002, SDD-ACCT-001
> ISA-95: Cross-cutting

---

## 1. Context

The Finance frontend is a single React 18 + TypeScript + Vite SPA served from `Finance.Frontend` (Nginx in production, Vite dev server locally). It calls every backend through the `Finance.Gateway` at `/api/v1/*`. Tokens, language, and density preferences are persisted in `localStorage` via Zustand `persist`.

The shell ships in Phase 0 with: login, an authenticated app shell, and one feature surface (Chart of Accounts list). It mirrors Warehouse's frontend rules (`SDD-UI-001` Vue equivalent) — same patterns, React syntax.

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

### 2.6 Folder structure (SHOULD)
- `src/app/` — shell, router providers.
- `src/features/<feature>/` — per-feature pages, hooks, components.
- `src/shared/` — stores, hooks, utils, api client, i18n, theme.
- Atomic Design (atoms / molecules / organisms / templates / pages) is introduced from Phase 2 as feature surfaces grow.

### 2.7 Routing (MUST)
- `/login` — unauthenticated landing.
- `/` — root, redirects to `/accounts` when authenticated.
- `/accounts` — Chart of Accounts list (Phase 0 only).
- Catch-all redirects to `/`.

## 3. Validation

- Login form requires `username` and `password` — basic HTML5 `required`.
- Detailed field-level validation arrives with the Accounts CRUD form in Phase 2.

## 4. Error Rules

UI-side errors are i18n keys under `errors.*`. Mapping is described in §2.5.

## 5. Versioning

This spec describes the v1 shell. New shell-wide capabilities (e.g., theme switcher, multi-tab support) are added via `CHG-ENH-*`.

## 6. Test Plan

Phase 0 ships without automated UI tests. Phase 2 introduces `ui-validate` (Chrome DevTools MCP) coverage matching Warehouse's pattern:

| Test path | Coverage |
|---|---|
| Login golden path | Login → redirect to `/accounts` |
| Auth required | Visit `/accounts` unauthenticated → redirect to `/login` |
| Accounts list loads | Authenticated visit shows DataGrid (possibly empty) |
| Language switch | EN → BG re-renders all headings |
| Density toggle | Compact tightens DataGrid + Card padding |
| API error toast | Force 500 → user sees translated `errors.GENERIC_ERROR` |
| Correlation ID | Every outbound request has unique `X-Correlation-ID` header |

## 7. Open Items

- Notistack toast wiring (`notification.error(...)`) — Phase 2.
- Refresh-token rotation flow — currently 401 → logout; Phase 2 will swap to a silent refresh on 401.
- Modal vs page form mode (`SDD-UI-002` equivalent) — Phase 2 when CRUD forms ship.
- `useGoBack` hook — Phase 2 when detail pages ship.
- Vuetify-style theming (`SDD-UI-001` Warehouse equivalent has dark mode + brand color picker) — Phase 7.
