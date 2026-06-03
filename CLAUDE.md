# Finance — Project Instructions

> Last updated: 2026-05-28
>
> This file is the always-loaded entry point. It contains only the rules that apply to EVERY task.
> Bulky reference material lives in `.claude/context/*.md` and is read on demand — see §0.4 below for the index.

---

## 0. Execution Pipeline (summary)

```
1. spec-writer
2. implement          (backend C#)
3. test               (backend tests)
4. validate           (spec ↔ code ↔ tests)
5. isa95-validate
6. implement-frontend   ┐ only when the work touches the SPA
7. ui-validate          ┘ (Chrome DevTools MCP)
```

| Phase | Agent | Persona / Reference | Purpose |
|---|---|---|---|
| 1. Spec Writer | `spec-writer` | `doc-governance.md` + `spec-writer.md` | Write/update the SDD before code |
| 2. Backend Implementator | `implement` | `csharp-persona.md` + `persona-dotnet8-microservices.md` (+ `persona-database.md` for DB) | Write production C# code |
| 3. Tester | `test` | `csharp-persona.md` + `tester-base.md` (+ `tester-ef-orm.md` / `tester-integration.md`) | Write NUnit tests |
| 4. Spec Validator | `validate` | `doc-governance.md` + `spec-validator.md` | Validate spec ↔ code ↔ tests |
| 5. ISA-95 Validator | `isa95-validate` | `.claude/agents/isa95-validate.md` + `.claude/context/isa95.md` | Validate standard alignment (Level 4 scope) |
| 6. Frontend Implementator | `implement-frontend` | reads §0.3 (B) below | Write React + MUI + TypeScript code |
| 7. UI Validator | `ui-validate` | Chrome DevTools MCP | Drive the running SPA and verify the feature works (i18n, density, modal/page, back nav, console, network) |

Phases 1–5 always run. Phases 6–7 run only when `involves_frontend == true`.

**System-spec status transitions across the pipeline** (overrides the persona's generic `Draft/Active/Deprecated`):

- Phase 1 (spec-writer) authors a new SDD as **`Drafted`**.
- When the spec is accepted and work begins, it becomes **`Active`** ("committed — will be built to the end; should be implemented"). May be partially or not-yet built.
- After Phase 3 (test) and Phase 4 (validate) pass, it becomes **`Implemented`** (code shipped + tests pass + in force). A spec whose **core** behavior is shipped + tested is `Implemented` even with explicit `Deferred:` notes for later-phase sub-items.
- `Deprecated` is the terminal retired/superseded state.

See `docs/README.md` → "Status lifecycle" for the authoritative table.

**Precedence:** `CLAUDE.md > persona-database > persona-dotnet8-microservices > csharp-persona > doc-governance`.

---

## 0.1 Project Policies (override persona defaults)

- **Target framework:** .NET 8.0
- **Repository:** standalone `finance-service` repo (separate from Warehouse)
- **Namespace prefix:** `Finance.*` (NOT `Warehouse.*`, NOT `WH.Finance.*`)
- **Error responses:** ProblemDetails (RFC 7807). `title` = machine code (SCREAMING_SNAKE_CASE), `detail` = developer English. `type` = `https://finance.local/errors/{code}`.
- **Mapping:** AutoMapper only
- **Logging:** NLog → Loki (matches Warehouse infra)
- **Tracing:** OpenTelemetry → Jaeger (matches Warehouse infra)
- **ORM:** EF Core, one `DbContext` **per microservice** (each service owns one database)
- **DB strategy:** Database-per-service on the shared Warehouse SQL Server instance
- **Validation:** FluentValidation (use `.WithErrorCode(...)` referencing a constant in `Finance.Common/ErrorCodes/<Domain>ErrorCodes.cs`)
- **PK strategy:** `INT IDENTITY` for internal entities; `UNIQUEIDENTIFIER` + `NEWSEQUENTIALID()` only when the ID is exposed via events or external document references (journal_entries, invoices, payments)
- **Monetary fields:** `DECIMAL(18,2)` for amounts, `DECIMAL(18,6)` for rates. NEVER `FLOAT`.
- **Timestamps:** `DATETIMEOFFSET` with `SYSDATETIMEOFFSET()` default — period close needs explicit time-zone semantics
- **API versioning:** URL-based (`/api/v1/`)
- **Authentication:** JWT validation via shared `Warehouse.Auth.Shared` NuGet from GitHub Packages. Same user accounts as Warehouse.
- **Authorization:** `[RequirePermission("finance.<resource>:<action>")]` on every endpoint
- **HTTP clients:** Refit + Polly via `Microsoft.Extensions.Http.Resilience` + `CorrelationIdDelegatingHandler`
- **Messaging:** MassTransit + RabbitMQ (shared vhost with Warehouse) + **EF Core Transactional Outbox** (stronger than Warehouse — Finance cannot lose events) + Redis SETNX idempotency filter
- **Caching:** Redis. Reference data only (chart of accounts, currencies, periods, tax rates). **NEVER** cache transactional data (journals, invoices, payments, balances).
- **Frontend:** React 18 + TypeScript + Vite + MUI v5 + React Router v6 + TanStack Query + Zustand + react-i18next. Atomic Design folder pattern.
- **i18n locales:** EN + BG (both files MUST stay in sync per PR)
- **Sequence numbers:** `ISequenceGenerator` with `UPDLOCK, HOLDLOCK` — gapless per НАП

### Configuration File Policy

- Only `.template` files are tracked in git. Real `appsettings.json` is gitignored.
- Templates use `<PLACEHOLDER>` for connection strings, secrets, keys.
- When adding a new config key, update the matching `.template` file.

---

## 0.2 Specification Alignment Gate

Before implementing any task, check alignment with existing specifications:

- `docs/core/SDD-*.md` — Universal engine (double-entry, journal, posting, periods, currency)
- `docs/domain/SDD-*.md` — Documents, payments, country strategy, sub-ledgers, reporting
- `docs/integration/SDD-*.md` — Warehouse events, auth, BNB rates, НАП export
- `docs/infrastructure/SDD-*.md` — Cross-cutting concerns
- `docs/changes/CHG-*.md` — Proposed work

If a request is outside scope, contradicts a spec, or is ambiguous — **STOP and clarify**.

---

## 0.3 Always-Active Cross-Cutting SDDs (MANDATORY pre-flight)

**These rules apply to EVERY code change regardless of domain. Read or recall them BEFORE writing code that touches their surface.**

### A. Backend (every endpoint, service, event publisher, event consumer)

| Concern | Spec(s) | Hard rules you MUST satisfy |
|---|---|---|
| **Correlation ID propagation** | `SDD-INFRA-001` | Inject `ICorrelationIdAccessor`; copy `CorrelationId` onto every published MassTransit event; do NOT bypass `CorrelationIdMiddleware`; never log without the ambient correlation scope; outbound Refit clients include `CorrelationIdDelegatingHandler`. |
| **Error codes & ProblemDetails** | `SDD-INFRA-001` | All `.WithErrorCode(...)` calls reference a constant in `Finance.Common/ErrorCodes/<Domain>ErrorCodes.cs` — **never** raw string literals. `title` = machine code (SCREAMING_SNAKE_CASE); `detail` = developer English; `type` = `https://finance.local/errors/{code}`. Validation responses MUST use the customized factory (codes in `errors` dictionary). Add a matching `errors.<CODE>` entry in `frontend/src/shared/i18n/locales/{en,bg}.ts` in the SAME PR. |
| **Auth / RBAC** | `SDD-INT-AUTH-001` | Every controller uses `[RequirePermission("finance.<resource>:<action>")]`. JWT decoded via the shared `Warehouse.Auth.Shared` package — do NOT add bespoke auth. Audit-worthy state changes publish to the EventLog. |
| **Sequence generation** | `SDD-INFRA-003` | Document numbers (Journal Entry, Invoice, Payment, Credit Note, Debit Note) come from `ISequenceGenerator` with `UPDLOCK, HOLDLOCK` — gapless per НАП. Never hand-rolled formatting. Format defined by `ICountryStrategy.GenerateDocumentNumber`. |
| **Caching** | `SDD-INFRA-004` | Cache only reference data: chart of accounts, currencies (with short TTL on rates), periods, tax rates, posting rule templates. Transactional data (journals, invoices, payments, account balances) MUST NOT be cached. Invalidate on every write. Key pattern `{service}:{entity}:all`. Falls through to DB if Redis is unreachable — service availability MUST NOT depend on Redis. |
| **Filtering / paging** | `SDD-INFRA-005` | List endpoints accept a `FilterRequest` and run through `IQueryable<T>.ApplyFilter(request)`. Properties MUST opt in via `[Filterable]` / `[Sortable]`. PageSize cap 200. The library always appends PK as the final sort term for deterministic pagination. |
| **Domain events** | `SDD-INFRA-006`, `SDD-EVTLOG-001` | Event records live in `Finance.ServiceModel/Events/`, are `sealed record` with `required` properties + `CorrelationId` + `MessageId`. Publishing goes through MassTransit **Transactional Outbox** — atomic with the DB transaction. Never `await _bus.Publish(...)` directly outside the outbox. Outbox is configured per `DbContext`. |
| **Idempotency** | `SDD-INFRA-006` | Every MassTransit consumer wraps in `IdempotencyFilter<T>` using Redis `SETNX` with 7-day TTL keyed by `MessageId`. Replays from retries/DLQ MUST be safe. |
| **Cross-aggregate validation** | `SDD-INFRA-007` | Multi-table or stateful validations (e.g., "posting against closed period", "allocation exceeds outstanding") use the `IChainValidator<TRequest>` chain registered via `services.AddValidationChain<TRequest>()`. Shape-only validations stay in FluentValidation. |
| **Workflow / state transitions** | `SDD-INFRA-008` | Aggregates with multi-state lifecycles (Journal Entry, Invoice, Payment, Period) use `IWorkflowEngine<TAggregate>`. Each state declares `AllowedNextStates`; the engine enforces transition legality, runs guards, writes status history, increments RowVersion. `Posted` is immutable — reverse via a sign-flipped new entry; never UPDATE. |
| **Base service / controller helpers** | `SDD-INFRA-009` | Services inherit `BaseEntityService<TContext>` (FindOrNotFound, MapAndSave, SaveWithConcurrencyCheck). List services inherit `SearchableServiceBase<TEntity, TDto>` for SDD-INFRA-005 filtering. Controllers inherit `BaseApiController` (`ToActionResult<T>(Result<T>)`). Services return `Result<T>` — never throw for business failures. |
| **Reference data via Nomenclature** | `SDD-NOM-001` | Country / state / city / currency dropdowns load through the `useNomenclature()` React hook backed by `Finance.Nomenclature.API` (which proxies Warehouse for country/state/city). Never hard-code dropdown options. |
| **Audit trail** | `SDD-AUDIT-001` | Every workflow transition, journal post, invoice confirm/post/settle/cancel, payment record/allocate, period state change, and CoA mutation MUST write an `audit.OperationsEvents` row in the SAME transaction. The `audit` schema DENYs UPDATE/DELETE at the DB level. Sensitive ops (period close, reversal, permission revocation) MUST include a `Reason`. Retention ≥ 10 years. |
| **Observability** | `SDD-OBS-001` | NLog → Loki with `service`, `level`, `correlation_id` labels. Structured logging only — no string interpolation in log calls. OpenTelemetry exports to Jaeger; `traceparent` propagated through HTTP + MassTransit; `CorrelationId` stamped onto `Activity.Current` so Jaeger can search by either ID. Sensitive fields (tokens, passwords, full IDs) MUST NEVER be logged. |
| **Immutability** | `SDD-AUDIT-001` | Posted journal entries MUST NEVER be UPDATEd. To correct, post a reversing entry. Confirmed/posted invoices MUST NEVER be edited — issue a Credit/Debit Note. Reversed entries keep both old and new rows. |
| **Decimal arithmetic** | `SDD-FIN-005` | Monetary amounts: `decimal` in C#, `DECIMAL(18,2)` in SQL. Rates: `decimal` / `DECIMAL(18,6)`. Never `double`/`float`. Rounding goes through `ICountryStrategy.ApplyTaxRounding`. |
| **Country strategy** | `SDD-CTRY-001` | Anything country-specific (CoA, tax, document numbering, posting rules, statement layouts, exchange rates, regulatory exports, counterparty validation) goes through `ICountryStrategy`. Core code MUST be country-agnostic. |
| **ISA-95 compliance (Level 4)** | `.claude/context/isa95.md` | New entities classified; new operations mapped; spec references ISA-95 part/section; immutable events for state changes. |

### B. Frontend (every view, hook, dialog)

| Concern | Spec(s) | Hard rules you MUST satisfy |
|---|---|---|
| **i18n placeholder/text sync** | `SDD-UI-001` | Every `t('foo.bar')` key MUST exist in BOTH `en.ts` and `bg.ts`. Adding/renaming a key requires updating both files in the SAME PR. Backend error codes added to `*ErrorCodes.cs` MUST have matching `errors.<CODE>` entries on the frontend. Never let raw key paths render in UI. |
| **Form display mode (modal vs page)** | `SDD-UI-002` | All CRUD form organisms accept a `mode: 'dialog' \| 'page'` prop. List views read `useLayoutStore(s => s.isPageMode)` and EITHER `navigate(...)` to the `*CreatePage`/`*EditPage` route OR open the `*FormDialog` — never both. Page-mode pages embed the same organism inside a `Card` with a Back button that uses the shared `useGoBack` hook. |
| **Layout density (compact / comfortable)** | `SDD-UI-001` | Every MUI `DataGrid`, `Table`, `Card`, `List`, `TextField`, etc. reads `density` from `useLayoutStore`. Spacing derives from `isCompact ? 'mb-2 p-3' : 'mb-4 p-4'`. Never hard-code `size="small"` or padding values that ignore the user's preference. |
| **Back navigation** | `SDD-UI-002` | All detail/create/edit page `goBack` implementations call `useGoBack({ fallback: { name: '<listing>' } }).goBack()` — never hard-code `navigate(...)`. The fallback route is the listing; the actual back jumps to the previous in-app entry. |
| **Navigation strategy & search params** | — | New views use `useNavigationStrategy` + `useSearchParams` hooks instead of inlining `if (isPageMode) navigate(...) else setShowDialog(true)` and ad-hoc filter-watcher patterns. |
| **API error mapping** | `SDD-INFRA-001` | All `catch` blocks in hooks MUST forward errors through `notification.error(getApiErrorMessage(err, t))`. Do NOT show `err.message`, raw `data.detail`, or `err.response.status` directly. |
| **Correlation ID on outbound** | `SDD-INFRA-001` | The Axios client MUST send `X-Correlation-ID` on every request (generate UUID per request via request interceptor). Do NOT bypass the shared axios instance. |

### C. Integration with Warehouse

`SDD-INT-WH-001` (inbound events) / `SDD-INT-WH-002` (outbound calls). When adding a new event consumer or Refit call, register it with the standard handler chain (`CorrelationIdDelegatingHandler` → `ServiceToServiceJwtHandler` → `AddStandardResilienceHandler`). All calls go through `Finance.Gateway` for incoming and through the Warehouse Gateway for outgoing.

### When in doubt

If your work touches a row above but you have not opened the linked spec in this session — **STOP and read it before writing code**.

If a rule in this section conflicts with a domain SDD, the domain SDD wins for the specific behavior — but log the divergence in the change spec.

---

## 0.4 Context File Index (read on demand)

| When you are working on … | Read this file |
|---|---|
| Pipeline phases, persona selection, agent invocation | `.claude/context/personas.md` |
| ISA-95 classification (Level 4), activity mapping, ERP integration | `.claude/context/isa95.md` |
| Adding/moving projects, wiring references, solution layout | `.claude/context/structure.md` |
| Entities, schemas, cross-service references | `.claude/context/entities.md` |
| Health checks, Redis, MassTransit + outbox, Polly, feature flags, observability | `.claude/context/infrastructure.md` |
| Config keys, service ports, endpoint spec pointers | `.claude/context/config.md` |
| Exact paths to a controller, DbContext, shared util, frontend store | `.claude/context/file-paths.md` |
| Country strategy contract + adding a new country | `.claude/context/country-strategy.md` |
| Spec format and category placement | `docs/README.md` |
| Spec ↔ test ↔ code traceability | `docs/cross-reference-map.md` |

---

## 1. Project Purpose

Finance & accounting platform built on a country-agnostic core + pluggable Country Strategy. ISA-95 **Level 4** (Business Planning & Logistics). Designed to integrate with the Warehouse Management System via the shared MassTransit event mesh and Gateway REST APIs.

Each functional module is a microservice: Accounts, Periods, Currency, Journal, Invoices, Payments, Reporting, EventLog. A single YARP gateway (`Finance.Gateway`) is the external surface — it's also the proxy Warehouse calls into for Finance data.

---

## 2. SDD Documentation Structure (TWO-TIER)

### Tier 1 — System Specs (`SDD-*`)

Describe the committed, in-force behavior of the platform. Source of truth. Status lifecycle: **`Drafted` → `Active` → `Implemented`** (+ `Deprecated` terminal) — see §0 and `docs/README.md` for the authoritative definitions. `Active` ≠ "done"; it means committed/will-be-built. `Implemented` = shipped + tests pass.

| Category | Folder | Purpose |
|---|---|---|
| Core | `docs/core/` | Universal engine: double-entry, journal, posting, periods, currency |
| Domain | `docs/domain/` | Documents, payments, country strategy, sub-ledgers, reporting |
| Integration | `docs/integration/` | Warehouse events, auth, BNB, НАП |
| Infrastructure | `docs/infrastructure/` | Gateway, observability, correlation, MassTransit + outbox, idempotency, feature flags, sequences |

### Tier 2 — Change Specs (`CHG-*`)

Describe proposed changes. Live in `docs/changes/`.

| Prefix | Use For |
|---|---|
| `CHG-FEAT-NNN` | New features or capabilities |
| `CHG-ENH-NNN` | Enhancements to existing behavior |
| `CHG-FIX-NNN` | Bug fixes |
| `CHG-REFAC-NNN` | Refactoring (no behavior change) |
| `CHG-DEBT-NNN` | Technical debt reduction |

---

## 3. Naming Conventions

See `FINANCE-MICROSERVICES-PLAN.md` §12.

---

## 4. Known Deviations from Persona Standards

| # | Deviation | Reason |
|---|---|---|
| 1 | `Nullable` enabled in `.csproj` | Default template setting |
| 2 | `ImplicitUsings` enabled | Default template setting |
| 3 | MassTransit **transactional outbox** instead of fire-and-forget | Finance cannot lose events |
| 4 | `DATETIMEOFFSET` instead of Warehouse's `DATETIME2(7)` | Period close needs explicit time-zone semantics |
| 5 | Refit instead of typed HttpClient | User preference |

---

## 5. Git Commit Authoring

- **Do NOT** add `Co-Authored-By: Claude` or any AI attribution trailer to commit messages.
- Commits must appear as authored solely by the user's git profile.
- No AI tooling references in commit messages, branch names, or PR descriptions unless explicitly requested.

---

## 6. Task Reporting

Every completed task is logged to `reporting/YYYY-MM.md` per `~/.claude/rules/reporting.md`.
