# Finance Microservices Plan — ISA-95 Level 4

> Created: 2026-05-28
> Type: Greenfield — .NET 8 Microservices + React Frontend (separate repo)
> Database: Shared SQL Server with Warehouse (database-per-service)
> Standard: ISA-95 / IEC 62264 — Level 4 (Business Planning & Logistics)
> Repository: standalone `finance-service` repo, namespace `Finance.*`
> Auth: shared with Warehouse via the GitHub-Packages auth library (`Warehouse.Auth.Shared` / equivalent)
> Integration: consumes Warehouse domain events via the shared RabbitMQ event mesh; exposes read APIs that Warehouse can query through the `Finance.Gateway` proxy

---

## 0. Why a separate repo

Warehouse `MICROSERVICES-PLAN.md` §10 already records the architecture decision: Finance is **ISA-95 Level 4** with fundamentally different concerns (double-entry bookkeeping, multi-currency, period-end close, tax/regulatory reporting, audit trails) from the operations platform, and must remain optionally replaceable by a commercial system (SAP, NetSuite, etc.). Keeping it in its own repo:

- Preserves that optionality
- Lets accounting/audit stakeholders move at their own release cadence
- Stops Level-4 vocabulary (chart of accounts, journals, posting periods, currency revaluation) from leaking into operational services
- Mirrors what was already done for `auth-service`

---

## 1. Architecture: Core + Country Strategy

### 1.1 Universal core (country-agnostic)
Double-entry bookkeeping · GL · Journal Entry lifecycle (Draft → Posted → Reversed) · Document lifecycle (Draft → Confirmed → Posted → Settled → Archived) · Sub-ledgers (AP/AR/Inventory) · Payment matching · Fiscal period management · Multi-currency engine · Posting engine · Audit trail.

### 1.2 Country-specific (behind `ICountryStrategy`)
Chart of accounts · Tax system · Tax reporting · Regulatory exports · Document numbering format · Financial statement layouts · Base currency + rate provider · Legal metadata · Posting rule seeds · Rounding rules · Counterparty validation.

### 1.3 Strategy registration
Single-tenant in v1: `services.AddScoped<ICountryStrategy, BulgariaStrategy>();`
Multi-tenant resolution is supported by the interface but deferred to a future CHG.

### 1.4 Adding a new country
A new `Finance.Country.XX` NuGet package implementing `ICountryStrategy` with: chart-of-accounts seed JSON, tax rates + calculation/rounding, posting rule templates, document number format, counterparty validation, exchange-rate provider, statement layouts, regulatory report definitions + exporters, legal metadata.
**No core service code changes required.**

---

## 2. Service Decomposition (module-per-service)

Each functional module is its own microservice with its own database. A single YARP gateway fronts them and acts as the **proxy that Warehouse calls** for read APIs.

| # | Service | Domain | Port | Database | ISA-95 Activity |
|---|---|---|---|---|---|
| 1 | **Finance.Accounts.API** | Chart of Accounts | 6001 | `finance_accounts` | Reference data |
| 2 | **Finance.Periods.API** | Fiscal Periods | 6002 | `finance_periods` | Reference data |
| 3 | **Finance.Currency.API** | Currencies + Exchange Rates | 6003 | `finance_currency` | Reference data |
| 4 | **Finance.Journal.API** | Journal Entries + GL + Posting Engine | 6004 | `finance_journal` | L4 Bookkeeping |
| 5 | **Finance.Invoices.API** | Purchase + Sale invoices + lifecycle | 6005 | `finance_invoices` | L4 Documents |
| 6 | **Finance.Payments.API** | Payments + Allocations + Matching | 6006 | `finance_payments` | L4 Documents |
| 7 | **Finance.Reporting.API** | Statements + Tax journals + Regulatory exports | 6007 | (read-replicated views) | L4 Reporting |
| 8 | **Finance.EventLog.API** | Inbound event log (Warehouse + Finance) | 6008 | `finance_eventlog` | Cross-cutting |
| — | **Finance.Gateway** | YARP — single entrypoint; also serves Warehouse reads | 6000 | — | Cross-cutting |
| — | **Finance.Frontend** | React SPA | 6100 | — | Cross-cutting |

### 2.1 Shared libraries (NuGet — GitHub Packages)

| Package | Purpose |
|---|---|
| `Finance.Common` | Error code constants (`AccountErrorCodes`, `JournalErrorCodes`, …), shared enums, helpers |
| `Finance.ServiceModel` | DTOs (request/response contracts), event records (`InvoiceConfirmedEvent`, `JournalEntryPostedEvent`, …) |
| `Finance.Infrastructure` | NLog config, correlation-ID middleware + Refit delegating handler, MassTransit + outbox setup, Polly handler factory, health-check extension, Redis cache extension, feature flags extension |
| `Finance.Country.Abstractions` | `ICountryStrategy`, `IExchangeRateProvider`, `IChartOfAccountsSeed`, posting-rule contracts |
| `Finance.Country.BG` | Bulgaria strategy (НСС chart, ДДС, НАП export, BNB rate provider) |
| `Finance.Mapping` | AutoMapper profiles |
| `Finance.GenericFiltering` | Reusable `IQueryable` dynamic filter library (mirrors Warehouse equivalent) |
| `Warehouse.Auth.Shared` (consumed) | JWT validation + `[RequirePermission(...)]` attribute, already published from the auth-service repo |
| `Warehouse.Correlation.Shared` (consumed) | Correlation-ID middleware + delegating handler, already published from the auth-service split |

### 2.2 Proxy/Gateway role — how Warehouse reaches Finance

`Finance.Gateway` is the **single Warehouse-facing surface**. Warehouse never calls Finance microservices directly. The Warehouse YARP gateway adds a route cluster `finance-cluster → http://finance-gateway:6000`, and Finance routes through its own gateway to the right microservice. This gives Finance a stable contract surface for Warehouse and lets Finance reorganize internally without breaking Warehouse.

```
Warehouse SPA ─┐
               ├─► Warehouse.Gateway ─► Warehouse microservices
Warehouse APIs ┘                    └─► Finance.Gateway ─► Finance microservices
                                              ▲
                                              │
Finance React SPA ────────────────────────────┘
```

Warehouse → Finance calls go through `Finance.Integration.Client` (Refit interface published by Finance), which forwards `X-Correlation-ID` and a service-to-service JWT issued by the shared Auth.

---

## 3. Repository Layout (mirrors Warehouse conventions)

```
finance-service/
├── CLAUDE.md                          ← Always-loaded entry point. Pipeline summary, project policies, §0.3 always-active cross-cutting SDDs, context file index.
├── .claude/
│   ├── agents/                        ← Project-local agents (pipeline, isa95-validate)
│   └── context/                       ← On-demand reference (personas, isa95, structure, entities, infrastructure, config, file-paths)
├── README.md
├── docker-compose.infrastructure.yml  ← Compose file that ATTACHES to Warehouse's running infra (external network) and adds Finance services
├── docker/
├── docs/
│   ├── README.md
│   ├── cross-reference-map.md
│   ├── core/                          ← Double-entry, journal, posting, periods, currency
│   ├── domain/                        ← Invoices, payments, country strategy, sub-ledgers
│   ├── infrastructure/                ← Gateway, NLog, correlation, MassTransit, outbox, idempotency, Polly, feature flags
│   ├── integration/                   ← Warehouse integration, auth integration, BNB rate provider, НАП export
│   └── changes/                       ← CHG-* specs
├── frontend/                          ← React + Vite + MUI SPA (port 6100)
│   └── src/
│       ├── app/                       ← Shell, router, providers
│       ├── components/                ← Atomic Design: atoms, molecules, organisms, templates, pages
│       ├── features/                  ← accounts, periods, currency, journal, invoices, payments, reports
│       ├── api/                       ← Generated/typed API clients (one per backend service)
│       ├── shared/                    ← composables → React: hooks (useGoBack, useNavigationStrategy, useSearchParams), stores (Zustand), i18n (EN + BG), utils (getApiErrorMessage)
│       └── theme/                     ← MUI theme + density toggle
└── src/
    ├── Finance.slnx
    ├── Databases/
    │   ├── Finance.Accounts.DBModel/        ← finance_accounts schema
    │   ├── Finance.Periods.DBModel/         ← finance_periods schema
    │   ├── Finance.Currency.DBModel/        ← finance_currency schema
    │   ├── Finance.Journal.DBModel/         ← finance_journal schema (incl. MassTransit outbox tables)
    │   ├── Finance.Invoices.DBModel/        ← finance_invoices schema (incl. outbox)
    │   ├── Finance.Payments.DBModel/        ← finance_payments schema (incl. outbox)
    │   └── Finance.EventLog.DBModel/        ← finance_eventlog schema
    ├── Infrastructure/
    │   ├── Gateway/
    │   │   └── Finance.Gateway/             ← YARP reverse proxy (port 6000)
    │   └── EventLog/
    │       ├── Finance.EventLog.API/        ← MassTransit consumers + query endpoint (port 6008)
    │       └── Finance.EventLog.API.Tests/
    ├── Interfaces/
    │   ├── Accounts/
    │   │   ├── Finance.Accounts.API/        ← port 6001
    │   │   └── Finance.Accounts.API.Tests/
    │   ├── Periods/
    │   │   ├── Finance.Periods.API/         ← port 6002
    │   │   └── Finance.Periods.API.Tests/
    │   ├── Currency/
    │   │   ├── Finance.Currency.API/        ← port 6003
    │   │   └── Finance.Currency.API.Tests/
    │   ├── Journal/
    │   │   ├── Finance.Journal.API/         ← port 6004
    │   │   └── Finance.Journal.API.Tests/
    │   ├── Invoices/
    │   │   ├── Finance.Invoices.API/        ← port 6005
    │   │   └── Finance.Invoices.API.Tests/
    │   ├── Payments/
    │   │   ├── Finance.Payments.API/        ← port 6006
    │   │   └── Finance.Payments.API.Tests/
    │   └── Reporting/
    │       ├── Finance.Reporting.API/       ← port 6007
    │       └── Finance.Reporting.API.Tests/
    ├── Country/
    │   ├── Finance.Country.Abstractions/    ← ICountryStrategy + supporting contracts
    │   └── Finance.Country.BG/              ← Bulgaria strategy + НСС seed JSON
    ├── Finance.Common/
    ├── Finance.Infrastructure/
    ├── Finance.GenericFiltering/
    ├── Finance.Mapping/
    └── Finance.ServiceModel/
        ├── Events/                          ← Outbound event records (sealed record + required + CorrelationId)
        └── Integration/                     ← Refit interfaces for Warehouse → Finance and Finance → Warehouse
```

---

## 4. Stack Decisions (locked)

| Layer | Choice | Rationale |
|---|---|---|
| Backend runtime | .NET 8 | Matches Warehouse |
| HTTP framework | ASP.NET Core Web API | Matches Warehouse |
| ORM | EF Core (one `DbContext` per service) | Matches Warehouse |
| DB strategy | Database-per-service on the **shared** SQL Server instance | Matches Warehouse's single source-of-truth model but at a slightly stricter isolation tier (DB > schema) because Finance is a separate repo |
| Migrations | EF Core migrations, one history table per DB | Per service |
| Mapping | AutoMapper only | Matches Warehouse |
| Validation | FluentValidation with `.WithErrorCode(<constant>)` | Matches Warehouse §0.3.A |
| Errors | ProblemDetails (RFC 7807), `title` = machine code, `detail` = English | Matches Warehouse §0.3.A |
| Auth | JWT validation via shared `Warehouse.Auth.Shared` package | Same user accounts and tokens as Warehouse |
| RBAC | `[RequirePermission("finance.<resource>:<action>")]` | Same attribute as Warehouse |
| API versioning | URL-based `/api/v1/` | Matches Warehouse |
| Health checks | ASP.NET Core HealthChecks (`/health/live`, `/health/ready`) | Matches Warehouse |
| HTTP clients | **Refit** + Polly via `Microsoft.Extensions.Http.Resilience` | Per user decision; correlation-ID delegating handler still applies |
| Resilience | Retry 3× exponential+jitter, circuit breaker, timeout | Matches Warehouse defaults |
| Caching | Redis (`IDistributedCache`) — reference data only (CoA, currencies, periods, tax rates) | Matches Warehouse rules — never cache transactional data (journals, invoices, payments) |
| Messaging | MassTransit + RabbitMQ (shared vhost with Warehouse) | Required so Warehouse events reach Finance |
| Reliable events | MassTransit **Transactional Outbox** (EF Core) | Stronger than Warehouse's fire-and-forget — finance can't lose events |
| Idempotency | Custom MassTransit consume filter + Redis SETNX 7-day TTL | Per v3 plan |
| Logging | **NLog** → Loki | Matches Warehouse |
| Tracing | OpenTelemetry → **Jaeger** | Matches Warehouse |
| Metrics | OpenTelemetry → Prometheus → Grafana | Matches Warehouse direction |
| Feature flags | `Microsoft.FeatureManagement` | Matches Warehouse |
| Sequence numbers | Per-service generator with `UPDLOCK, HOLDLOCK` on `document_sequences` (gapless per НАП) | Stricter than Warehouse `ISequenceGenerator` — NAP requires gapless |
| API docs | Swagger/OpenAPI per service | Matches Warehouse |
| Containers | Docker + Compose, joins external Warehouse network | Shared infra |
| Code layout | Service/Repository/Controller folders | Per user decision — no CQRS folders |

### Frontend stack

| Layer | Choice |
|---|---|
| Framework | React 18 + TypeScript |
| Build | Vite |
| Component library | **MUI (Material UI) v5** |
| Routing | React Router v6 |
| Server state | TanStack Query (React Query) |
| Client state | Zustand (one store per feature) |
| Forms | React Hook Form + Zod (or yup) for client validation |
| HTTP client | Axios (with `X-Correlation-ID` request interceptor) |
| i18n | react-i18next — locales `en` and `bg`, both files MUST stay in sync (mirrors Warehouse SDD-UI-001/002) |
| Theming | MUI theme + density toggle (compact / comfortable) stored in a layout Zustand store — mirrors Warehouse SDD-UI-001 |
| Page/Modal mode | Form organisms accept `mode: 'dialog' | 'page'` (mirrors SDD-UI-002) |
| Back navigation | `useGoBack` hook (mirrors Warehouse composable) |
| Folder pattern | Atomic Design: atoms / molecules / organisms / templates / pages |
| Error toasts | Notistack via `notification.error(getApiErrorMessage(err, t))` |

---

## 5. SDD Documentation Structure (two-tier — mirrors Warehouse)

### Tier 1 — System Specs (`SDD-*`)
Describe current implemented behavior. Source of truth.

| Category | Folder | Purpose |
|---|---|---|
| Core | `docs/core/` | Universal engine: double-entry, journal lifecycle, posting, periods, currency |
| Domain | `docs/domain/` | Documents, payments, country strategy, sub-ledgers, reporting |
| Integration | `docs/integration/` | Warehouse event subscriptions, BNB rates, НАП export, auth |
| Infrastructure | `docs/infrastructure/` | Gateway, observability, correlation, MassTransit + outbox, idempotency, feature flags, sequences |

### Tier 2 — Change Specs (`CHG-*`) in `docs/changes/`
Same prefixes as Warehouse: `CHG-FEAT-NNN`, `CHG-ENH-NNN`, `CHG-FIX-NNN`, `CHG-REFAC-NNN`, `CHG-DEBT-NNN`.

### Initial SDD inventory (to be authored in Phase 1)

| ID | Title | Category |
|---|---|---|
| `SDD-FIN-001` | Double-Entry Engine | core |
| `SDD-FIN-002` | Journal Entry Lifecycle (Draft → Posted → Reversed) | core |
| `SDD-FIN-003` | General Ledger & Trial Balance | core |
| `SDD-FIN-004` | Fiscal Period Management | core |
| `SDD-FIN-005` | Multi-Currency Engine + Exchange Rates | core |
| `SDD-FIN-006` | Posting Engine + Posting Rules | core |
| `SDD-ACCT-001` | Chart of Accounts | domain |
| `SDD-INV-001` | Invoice Lifecycle (Purchase + Sale) | domain |
| `SDD-PAY-001` | Payment Recording & Matching | domain |
| `SDD-PAY-002` | Settlement & Allocation | domain |
| `SDD-RPT-001` | Trial Balance | domain |
| `SDD-RPT-002` | Balance Sheet + Income Statement (country layout) | domain |
| `SDD-RPT-003` | VAT Journals (Дневник покупки/продажби) | domain |
| `SDD-CTRY-001` | Country Strategy interface | domain |
| `SDD-CTRY-BG-001` | Bulgaria Strategy (НСС CoA, ДДС, НАП, BNB) | domain |
| `SDD-INT-WH-001` | Warehouse → Finance event subscriptions | integration |
| `SDD-INT-WH-002` | Finance → Warehouse Refit client (Customers, Products) | integration |
| `SDD-INT-AUTH-001` | Shared JWT authentication with Warehouse auth-service | integration |
| `SDD-INT-BNB-001` | BNB exchange-rate provider | integration |
| `SDD-INT-NAP-001` | НАП regulatory export | integration |
| `SDD-INFRA-001` | Correlation IDs, Polly resilience, Redis cache, MassTransit | infrastructure |
| `SDD-INFRA-002` | Finance Gateway (YARP) | infrastructure |
| `SDD-INFRA-003` | Sequence Generation (gapless per НАП) | infrastructure |
| `SDD-INFRA-004` | Transactional Outbox + Idempotency filter | infrastructure |
| `SDD-INFRA-005` | Feature flags | infrastructure |
| `SDD-OBS-001` | NLog → Loki, OpenTelemetry → Jaeger | infrastructure |
| `SDD-AUDIT-001` | Immutable audit trail of all financial state changes | infrastructure |
| `SDD-EVTLOG-001` | Centralized event log (mirrors Warehouse pattern) | infrastructure |
| `SDD-UI-001` | Layout, density, theme, i18n (EN + BG) | infrastructure |
| `SDD-UI-002` | Modal vs page form mode + `useGoBack` | infrastructure |

---

## 6. Always-Active Cross-Cutting SDDs (`CLAUDE.md` §0.3 equivalent)

These rules apply to EVERY code change and are pre-flight reading before writing code.

### A. Backend

| Concern | Spec | Hard rules |
|---|---|---|
| **Correlation ID** | `SDD-INFRA-001` | Inject `ICorrelationIdAccessor`; copy `CorrelationId` onto every published MassTransit event; never log without the ambient scope; outbound Refit clients use `CorrelationIdDelegatingHandler`. |
| **Error codes & ProblemDetails** | `CHG-ENH-005`-equivalent | All `.WithErrorCode(...)` reference a constant in `Finance.Common/ErrorCodes/<Domain>ErrorCodes.cs`. `title` = SCREAMING_SNAKE_CASE code; `detail` = English. Validation responses MUST put codes in the `errors` dictionary. A matching `errors.<CODE>` entry in `frontend/src/shared/i18n/locales/{en,bg}.ts` ships in the SAME PR. |
| **Auth / RBAC** | `SDD-INT-AUTH-001` | Every controller uses `[RequirePermission("finance.<resource>:<action>")]`. JWT validation comes from the shared `Warehouse.Auth.Shared` package. Permissions are seeded on first run and registered in the shared Auth permissions table. |
| **Sequence generation** | `SDD-INFRA-003` | Document numbers come from `ISequenceGenerator` with `UPDLOCK, HOLDLOCK` — gapless, per НАП. |
| **Domain events** | `SDD-EVTLOG-001` | Event records live in `Finance.ServiceModel/Events/`, are `sealed record` with `required` properties + `CorrelationId`. Publishing goes through MassTransit **outbox** — atomic with the DB transaction. |
| **Idempotency** | `SDD-INFRA-004` | Every consumer wraps in the `IdempotencyFilter<T>` using Redis SETNX 7-day TTL keyed by `MessageId`. |
| **Caching** | `SDD-INFRA-001` | Cache only reference data: chart of accounts, currencies, exchange rates (with short TTL), tax rates, posting rules. Transactional data (journals, invoices, payments, balances) MUST NOT be cached. Invalidate on every write. |
| **Immutability** | `SDD-AUDIT-001` | Posted journal entries MUST NEVER be UPDATEd. To correct, post a reversing entry. Same for invoices that have been posted. |
| **Decimal arithmetic** | `SDD-FIN-005` | All monetary fields are `DECIMAL(18,2)` (or `DECIMAL(18,6)` for rates). Never `FLOAT`. Rounding applied per country strategy. |

### B. Frontend (React equivalents of Warehouse UI rules)

| Concern | Spec | Hard rules |
|---|---|---|
| **i18n sync** | `SDD-UI-001` | Every `t('foo.bar')` key MUST exist in BOTH `en.ts` and `bg.ts`. Adding/renaming a key updates both files in the SAME PR. Backend error codes added to `*ErrorCodes.cs` MUST have matching `errors.<CODE>` entries. |
| **Form display mode** | `SDD-UI-002` | All CRUD form organisms accept a `mode: 'dialog' \| 'page'` prop. List views read `layout.isPageMode` (Zustand) and EITHER `navigate(...)` to the `*CreatePage`/`*EditPage` route OR open the `*FormDialog`. |
| **Density** | `SDD-UI-001` | Every MUI `DataGrid`, `Table`, `TextField`, `Card`, `List` reads `density` from the layout Zustand store. Spacing classes derive from `layout.isCompact ? 'mb-2 p-3' : 'mb-4 p-4'`. Never hard-code. |
| **Back navigation** | `SDD-UI-002` | All detail/create/edit page `goBack` implementations call `useGoBack({ fallback: { name: '<listing>' } }).goBack()` — never hard-code `navigate('/listing')`. |
| **Navigation strategy & filters** | — | New views use `useNavigationStrategy` + `useSearchParams` hooks (port the Warehouse composables verbatim). |
| **API error mapping** | — | All `catch` blocks forward errors through `notification.error(getApiErrorMessage(err, t))`. Never show `err.message` or raw `data.detail`. |
| **Correlation ID on outbound** | `SDD-INFRA-001` | The Axios client sends `X-Correlation-ID` on every request (UUID per request via request interceptor). |

---

## 7. Integration with Warehouse

### 7.1 Inbound events (Warehouse → Finance, via shared RabbitMQ)

| Event (from Warehouse) | Finance consumer | Action |
|---|---|---|
| `GoodsReceiptCompletedEvent` (Purchasing) | `Finance.Invoices` | Create draft Purchase Invoice using `ICountryStrategy.GenerateDocumentNumber` + `CalculateTax` |
| `ShipmentCompletedEvent` (Fulfillment) | `Finance.Invoices` | Create draft Sales Invoice |
| `CustomerReturnCompletedEvent` (Fulfillment) | `Finance.Invoices` | Create draft Credit Note |
| `SupplierReturnShippedEvent` (Purchasing) | `Finance.Invoices` | Create draft Debit Note |
| `ProductionOrderCompletedEvent` (Production) | `Finance.Journal` | Post COGS entry |
| `StockMovementRecordedEvent` (Inventory) | `Finance.Reporting` | Inventory valuation snapshot feed |

All consumers run through the `IdempotencyFilter<T>` (Redis SETNX) so replays from RabbitMQ retries / DLQ recovery don't double-post.

### 7.2 Outbound calls (Finance → Warehouse, via Refit + Polly)

`Finance.ServiceModel/Integration/IWarehouseGatewayApi.cs` — Refit interface:

```csharp
public interface IWarehouseGatewayApi
{
    [Get("/api/v1/customers/{id}")]
    Task<CustomerDto> GetCustomerAsync(Guid id, [Header("X-Correlation-ID")] string correlationId);

    [Get("/api/v1/products/{id}")]
    Task<ProductDto> GetProductAsync(Guid id, [Header("X-Correlation-ID")] string correlationId);

    [Get("/api/v1/sales-orders/{id}")]
    Task<SalesOrderDto> GetSalesOrderAsync(Guid id, [Header("X-Correlation-ID")] string correlationId);

    [Get("/api/v1/purchase-orders/{id}")]
    Task<PurchaseOrderDto> GetPurchaseOrderAsync(Guid id, [Header("X-Correlation-ID")] string correlationId);
}
```

Registered with:

```csharp
services.AddRefitClient<IWarehouseGatewayApi>()
    .ConfigureHttpClient(c => c.BaseAddress = new Uri(config["Warehouse:GatewayUrl"]))
    .AddHttpMessageHandler<CorrelationIdDelegatingHandler>()
    .AddHttpMessageHandler<ServiceToServiceJwtHandler>()  // mints S2S JWT from shared auth
    .AddStandardResilienceHandler();                       // Microsoft.Extensions.Http.Resilience
```

### 7.3 Inbound from Warehouse (Warehouse → Finance reads, via Finance.Gateway)

Warehouse adds a YARP cluster `finance-cluster → http://finance-gateway:6000`. Warehouse-side services that need Finance data (e.g., for displaying invoice/payment status on a Sales Order detail page) call `IFinanceIntegrationClient` (Refit interface) which routes through the Warehouse Gateway → Finance Gateway → the right Finance microservice.

### 7.4 Shared auth

Finance does **not** run its own user/permission database. It validates JWTs issued by the Warehouse `auth-service` using the shared `Warehouse.Auth.Shared` NuGet (already published to GitHub Packages per `feedback_github_packages_for_auth_split`). On first boot, Finance calls the Auth Permissions API once to register its own permission catalogue (`finance.account:read`, `finance.invoice:post`, etc.).

---

## 8. Core Database Schemas (per-service)

Each service owns its database. Outbox tables live alongside the service's own tables. Cross-service joins are forbidden — go through events or the Refit client.

(SQL excerpts retained from v3 §6 — see appendix below for the full DDL of `accounts`, `fiscal_periods`, `currencies`, `exchange_rates`, `posting_rules`, `journal_entries`, `journal_entry_lines`, `invoices`, `invoice_lines`, `payments`, `payment_allocations`, `document_sequences`.)

**Deltas from v3:**
- `id`: `INT IDENTITY` for non-natural-key tables to match Warehouse policy; keep `UNIQUEIDENTIFIER`+`NEWSEQUENTIALID()` where a stable external GUID is required (events, document numbers exposed externally).
- All timestamps: `DATETIMEOFFSET` with `SYSDATETIMEOFFSET()` default (matches v3, slightly different from Warehouse's `DATETIME2(7)` — DateTimeOffset is preferred for finance because period close needs explicit time-zone semantics).
- All money columns `DECIMAL(18,2)`, all rates `DECIMAL(18,6)`.

---

## 9. Implementation Phases

### Phase 0 — Repo bootstrap (week 0)
- [ ] Create `finance-service` GitHub repo
- [ ] Copy `CLAUDE.md`, `.claude/agents/pipeline.md`, `.claude/agents/isa95-validate.md`, `.claude/context/*.md` from Warehouse, adapt for Finance
- [ ] Create `docs/` SDD skeleton + `docs/README.md` + `docs/cross-reference-map.md` + `docs/changes/_TEMPLATE.md`
- [ ] Wire GitHub Packages auth for `Warehouse.Auth.Shared` and `Warehouse.Correlation.Shared`
- [ ] Add the empty `Finance.slnx` with all projects from §3
- [ ] Author the initial SDD inventory from §5 (stub level — sections + headers, marked `Status: Planned`)

### Phase 1 — Foundation + cross-cutting infrastructure (weeks 1–4)
- [ ] `Finance.Common` (error code constants, enums, helpers)
- [ ] `Finance.ServiceModel` (DTOs + event records)
- [ ] `Finance.Infrastructure` — NLog→Loki config, correlation middleware + Refit handler, MassTransit + EF outbox, Polly handler factory, Redis cache extension, feature flags extension, health-check extension, RBAC permission registration helper
- [ ] `Finance.Country.Abstractions` — `ICountryStrategy` and supporting contracts
- [ ] `Finance.Gateway` skeleton (YARP) — single route to start
- [ ] `docker-compose.infrastructure.yml` that joins Warehouse's `warehouse_default` external network and adds Finance services
- [ ] Authoring: `SDD-INFRA-001..005`, `SDD-OBS-001`, `SDD-AUDIT-001`, `SDD-INT-AUTH-001`

### Phase 2 — Reference data services (weeks 5–7)
- [ ] `Finance.Accounts.API` — Chart of Accounts CRUD, country-seeded
- [ ] `Finance.Periods.API` — Fiscal periods (Open/Closed/Locked) + close workflow
- [ ] `Finance.Currency.API` — Currencies + exchange rates + BNB provider (Refit + Polly)
- [ ] `Finance.Country.BG` — НСС chart-of-accounts seed JSON, ДДС tax rates, posting-rule templates, document number format
- [ ] Authoring: `SDD-ACCT-001`, `SDD-FIN-004`, `SDD-FIN-005`, `SDD-CTRY-001`, `SDD-CTRY-BG-001`, `SDD-INT-BNB-001`

### Phase 3 — Journal & GL engine (weeks 8–11)
- [ ] `Finance.Journal.API` — Journal entries Draft → Posted → Reversed; double-entry validation; posting engine that uses `ICountryStrategy.GetDefaultPostingRules`; GL aggregation; trial balance query
- [ ] Outbox configured on the Journal DbContext; publishes `JournalEntryPostedEvent`, `JournalEntryReversedEvent`
- [ ] Authoring: `SDD-FIN-001`, `SDD-FIN-002`, `SDD-FIN-003`, `SDD-FIN-006`

### Phase 4 — Documents (weeks 12–15)
- [ ] `Finance.Invoices.API` — Purchase + Sale invoice CRUD, gapless numbering via `ISequenceGenerator`, country-aware tax calculation + rounding, posting integration (publishes `InvoiceConfirmedEvent` via outbox → Journal consumer posts the JE)
- [ ] Inbound consumers: `GoodsReceiptCompletedEvent`, `ShipmentCompletedEvent`, `CustomerReturnCompletedEvent`, `SupplierReturnShippedEvent` → create draft documents
- [ ] Authoring: `SDD-INV-001`, `SDD-INT-WH-001`

### Phase 5 — Payments & settlement (weeks 16–18)
- [ ] `Finance.Payments.API` — Payment recording (cash + bank), payment ↔ invoice matching (full/partial), AP/AR aging, counterparty balances
- [ ] Authoring: `SDD-PAY-001`, `SDD-PAY-002`

### Phase 6 — Frontend MVP (weeks 19–23, runs in parallel from Phase 3)
- [ ] React + Vite + MUI + Router + TanStack Query + Zustand scaffolding
- [ ] Auth integration (login via shared Warehouse auth)
- [ ] Layout shell: density toggle, language switcher (EN/BG), navigation
- [ ] Atomic Design folder structure + shared hooks (`useGoBack`, `useNavigationStrategy`, `useSearchParams`, `getApiErrorMessage`)
- [ ] Feature surfaces: accounts list/form, periods, currencies + rates, journal entries (list/post/reverse), invoices (purchase + sale CRUD + post), payments
- [ ] Authoring: `SDD-UI-001`, `SDD-UI-002` + per-feature `SDD-UI-FIN-NNN`

### Phase 7 — Reporting (weeks 24–27)
- [ ] `Finance.Reporting.API` — Trial Balance, Balance Sheet, Income Statement (country layout), Account statements, Counterparty statements, VAT journals
- [ ] НАП export (TXT) via `ICountryStrategy.GenerateRegulatoryExport`
- [ ] Inbound consumer: `StockMovementRecordedEvent` for inventory valuation snapshots
- [ ] Authoring: `SDD-RPT-001..003`, `SDD-INT-NAP-001`

### Phase 8 — Hardening (weeks 28–30)
- [ ] Period-close workflow (status machine + locking)
- [ ] Multi-currency revaluation (behind feature flag)
- [ ] Audit trail completeness review
- [ ] DLQ monitoring dashboard in Grafana
- [ ] Performance tuning (GL queries, materialized reporting views)
- [ ] Rate limiting on `Finance.Gateway`
- [ ] Bank reconciliation MVP (behind feature flag)

---

## 10. Risks & Open Questions

| # | Risk | Mitigation |
|---|---|---|
| 1 | Bulgarian accounting specifics (НСС chart, НАП export format) need expert validation | Engage an accountant in Phase 2; ship Phase 2 BG seeds behind a sample dataset that can be reviewed independently |
| 2 | Gapless numbering under concurrent users | `UPDLOCK, HOLDLOCK` on `document_sequences` per `(document_type, fiscal_year)` row + retry-on-deadlock policy |
| 3 | Outbox growth | Schedule MassTransit outbox cleanup job (built-in delivered-message purge) |
| 4 | Eventual consistency between Journal and Reporting | Acceptable for reports; real-time balance checks hit `Finance.Journal.API` directly |
| 5 | Decimal arithmetic precision (VAT + FX rounding) | Always `DECIMAL`, never `FLOAT`; per-country rounding through `ICountryStrategy.ApplyTaxRounding`; unit tests with edge-case amounts (1/3 splits, 0.01 boundaries) |
| 6 | DLQ stuck messages = missing journal entries | Grafana alert on DLQ depth > 0; on-call playbook for replay from DLQ |
| 7 | Service-to-service auth between Warehouse and Finance | Mint short-lived S2S JWTs from the shared Auth using a dedicated `service:finance-integration` permission |
| 8 | Test data setup | Each test project uses an isolated SQL Server database (Testcontainers) seeded by the country strategy; MassTransit test harness for in-memory event verification |

---

## 11. Open Items To Confirm

These are deliberate deferrals or assumptions I made; flag any you want to change before Phase 1 starts:

1. **JWT signing key sharing.** I assume Warehouse `auth-service` already exposes a `/.well-known/jwks.json` (or equivalent) that Finance can fetch. If not, Finance needs the shared symmetric key via secret store.
2. **S2S JWT issuance.** Assumed the shared auth supports issuing service-account tokens. If not, fall back to a static shared secret in `Finance.Gateway`-protected appsettings for Warehouse → Finance calls.
3. **GitHub Packages for `Finance.*` libraries.** Assumed yes (matches your `feedback_github_packages_for_auth_split` memory).
4. **Multi-tenant / multi-country.** Single-tenant BG-only for v1. `ICountryStrategy` already supports multi-tenant resolution; flip it on later with a `CHG-FEAT`.
5. **CQRS read/write split inside `Finance.Reporting.API`.** Not using CQRS folders, but the service is read-only by design and will use direct queries against the writable databases for v1. Materialized views or read replicas come in Phase 8 if needed.
6. **Country package distribution.** `Finance.Country.BG` shipped as a NuGet on GitHub Packages. Each deployment picks the country package it installs.
7. **`Finance.Gateway` vs Warehouse Gateway.** Two separate gateways (one per system). Warehouse Gateway has a `finance-cluster` route block. Alternative: route everything through Warehouse Gateway and skip `Finance.Gateway` — simpler but couples Finance's external surface to Warehouse. **Current choice: keep them separate.**
8. **i18n locale set.** EN + BG for v1 (matches Warehouse). Add more locales per country package.

---

## 12. Naming Conventions (locked)

Same as Warehouse §3 except prefix:

| Item | Convention | Example |
|---|---|---|
| Controller | `{Entity}Controller` | `JournalEntriesController` |
| Service interface | `I{Entity}Service` | `IJournalEntryService` |
| Service implementation | `{Entity}Service` | `JournalEntryService` |
| Repository | `I{Entity}Repository` / `{Entity}Repository` | `IJournalEntryRepository` |
| DTO | `{Entity}Dto` | `JournalEntryDto` |
| Request | `{Action}{Entity}Request` | `PostJournalEntryRequest` |
| Response | `{Entity}Response` | `JournalEntryResponse` |
| AutoMapper profile | `{Entity}MappingProfile` | `JournalEntryMappingProfile` |
| Validator | `{Action}{Entity}RequestValidator` | `PostJournalEntryRequestValidator` |
| EF Core config | `{Entity}Configuration` | `JournalEntryConfiguration` |
| Domain event | `{Entity}{PastTenseVerb}Event` | `JournalEntryPostedEvent` |
| Feature flag | `Enable{Feature}` | `EnableMultiCurrency` |
| Cache key | `{service}:{entity}:all` | `finance-accounts:chart:all` |
| Error code constant | `SCREAMING_SNAKE_CASE` | `INVALID_PERIOD_STATUS_TRANSITION` |
| Permission | `finance.<resource>:<action>` | `finance.invoice:post` |

---

## 13. Git Commit Authoring

Same rule as Warehouse: **no AI attribution** (no `Co-Authored-By: Claude`, no AI tooling references in messages, branches, or PR descriptions).

---

## 14. Task Reporting

Every completed task logged to `reporting/YYYY-MM.md` at the repo root per `~/.claude/rules/reporting.md`.

---

## Appendix A — Core DDL (kept from v3 plan §6, deltas applied)

See v3 plan §6 for the full DDL. Apply these deltas when authoring the EF Core configurations:

- `accounts.id`, `fiscal_periods.id`, `exchange_rates.id`, `posting_rules.id`, `payment_allocations.id` → `INT IDENTITY` (internal-only references; no external GUID exposure needed)
- `journal_entries.id`, `invoices.id`, `payments.id` → keep `UNIQUEIDENTIFIER DEFAULT NEWSEQUENTIALID()` (exposed via events and external document references)
- Add `correlation_id UNIQUEIDENTIFIER NOT NULL` to `invoices` and `payments` (parity with `journal_entries`)
- All `created_at` / `posted_at` / `closed_at` columns → `DATETIMEOFFSET` with `SYSDATETIMEOFFSET()` default
- MassTransit outbox tables (`OutboxMessage`, `OutboxState`, `InboxState`) live in the same database as the service's own tables — created by the EF Core migration

---

## Appendix B — Frontend mapping from Vue to React

| Warehouse Vue artifact | Finance React equivalent |
|---|---|
| Vuetify component | MUI component (`v-data-table` → `DataGrid`, `v-card` → `Card`, `v-text-field` → `TextField`, `v-dialog` → `Dialog`) |
| Composition API `setup()` | React functional component + hooks |
| Composable (`useGoBack.ts`) | Custom hook (`useGoBack.ts`) — same name and contract |
| Pinia store | Zustand store (one per feature) |
| Vue Router | React Router v6 |
| Vue-i18n | react-i18next |
| `vm.layout.vuetifyDensity` | `useLayoutStore(s => s.density)` returning `'compact' \| 'comfortable'` mapped to MUI props |
| `mode: 'dialog' \| 'page'` prop on organisms | Same — React component prop |
| Axios instance in `shared/api/axios.ts` | Same file, same `X-Correlation-ID` interceptor |
| Notistack / global toast | Same notification helper signature: `notification.error(getApiErrorMessage(err, t))` |
