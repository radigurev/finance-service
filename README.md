# Finance Service

Finance & accounting platform with a country-agnostic core and pluggable Country Strategy. **ISA-95 Level 4** (Business Planning & Logistics). Integrates with the Warehouse Management System via shared MassTransit events (RabbitMQ) and Gateway REST APIs. Shares JWT authentication with Warehouse through the [auth-service](https://github.com/radigurev/auth-service) library.

## Status

**Phase 0 — Shell scaffolded.** First microservice (`Finance.Accounts.API`), `Finance.Gateway`, and React + MUI frontend are in place. Auth is wired against the shared `Warehouse.Auth.AspNetCore` NuGet (published from auth-service). 5 SDDs are drafted; the rest are stubs.

## What's scaffolded

### Backend (.NET 8)

| Project | Purpose |
|---|---|
| `src/Finance.Common` | Error code constants (`AccountErrorCodes`), shared enums (`AccountType`). NuGet-packable. |
| `src/Finance.ServiceModel` | DTOs (`AccountDto`, `CreateAccountRequest`, `UpdateAccountRequest`). NuGet-packable. |
| `src/Databases/Finance.Accounts.DBModel` | EF Core `AccountsDbContext`, `Account` entity, `AccountConfiguration`. SQL Server schema `accounts`. |
| `src/Interfaces/Accounts/Finance.Accounts.API` | First microservice (port 6001). Controller + Service + Repository + Validator + AutoMapper profile. JWT auth via `Warehouse.Auth.AspNetCore`, correlation IDs via `Warehouse.Correlation`, structured logging via NLog → Loki. |
| `src/Infrastructure/Gateway/Finance.Gateway` | YARP reverse proxy (port 6000) routing `/api/v1/auth/*` to auth-service, `/api/v1/accounts/*` to Accounts.API. Correlation-ID transform + per-IP rate limit + health aggregation. |

### Frontend (React 18 + TypeScript + Vite + MUI v5)

```
frontend/
├── src/
│   ├── main.tsx               ← ThemeProvider + QueryClientProvider + Router
│   ├── app/
│   │   ├── App.tsx            ← Routes
│   │   └── AppShell.tsx       ← AppBar + language switcher + density toggle + logout
│   ├── features/
│   │   ├── auth/              ← LoginPage, RequireAuth
│   │   └── accounts/          ← AccountsListPage (MUI DataGrid, TanStack Query)
│   └── shared/
│       ├── api/axios.ts       ← X-Correlation-ID + bearer token interceptors
│       ├── stores/            ← Zustand: auth, layout (density), theme
│       ├── i18n/locales/      ← en.ts + bg.ts (kept in sync)
│       └── utils/getApiErrorMessage.ts
```

### SDDs drafted

| ID | Title |
|---|---|
| `SDD-INT-AUTH-001` | Shared JWT Authentication with auth-service |
| `SDD-INFRA-001` | Cross-Cutting Foundations (correlation, ProblemDetails, NLog, versioning, health) |
| `SDD-INFRA-002` | Finance Gateway (YARP) |
| `SDD-ACCT-001` | Chart of Accounts |
| `SDD-UI-001` | Frontend Shell (React + MUI + i18n + Density) |

The remaining ~25 SDDs are stubs in `docs/cross-reference-map.md` to be authored as features land.

## Architecture (target — module-per-service)

| Service | Port | Status |
|---|---|---|
| `Finance.Gateway` | 6000 | **Shell** — proxies Accounts + Auth |
| `Finance.Accounts.API` | 6001 | **Shell** — CRUD wired, EF migrations pending |
| `Finance.Periods.API` | 6002 | Not scaffolded |
| `Finance.Currency.API` | 6003 | Not scaffolded |
| `Finance.Journal.API` | 6004 | Not scaffolded |
| `Finance.Invoices.API` | 6005 | Not scaffolded |
| `Finance.Payments.API` | 6006 | Not scaffolded |
| `Finance.Reporting.API` | 6007 | Not scaffolded |
| `Finance.EventLog.API` | 6008 | Not scaffolded |
| `Finance.Frontend` | 6100 | **Shell** — login + accounts list |

## Tech Stack (locked)

- **Backend:** .NET 8, ASP.NET Core, EF Core, AutoMapper, FluentValidation, MassTransit (Transactional Outbox) + RabbitMQ, Refit + Polly
- **Frontend:** React 18 + TypeScript + Vite + MUI v5 + React Router v6 + TanStack Query + Zustand + react-i18next (EN + BG)
- **Database:** SQL Server (shared instance with Warehouse, database-per-service)
- **Cache:** Redis (reference data only)
- **Observability:** NLog → Loki, OpenTelemetry → Jaeger, Grafana
- **Auth:** Shared with Warehouse via `Warehouse.Auth.AspNetCore` NuGet (GitHub Packages) — JWT validation only, no separate user store

## Building

The shell references shared libraries (`Warehouse.Auth.AspNetCore`, `Warehouse.Correlation`, `Warehouse.Common`) that are **published to GitHub Packages by the auth-service repo**. The Jun 17 cutover finalizes that publishing pipeline. Until then, restore requires:

```powershell
$env:GITHUB_PACKAGES_USER = "<your-github-user>"
$env:GITHUB_PACKAGES_TOKEN = "<PAT with read:packages scope>"
cd src
dotnet restore
dotnet build
```

If the packages are not yet published, `dotnet restore` will fail with `NU1101` on the `Warehouse.*` packages. Workaround until cutover: clone `auth-service` locally and run `dotnet pack --output ../local-feed`, then add a local source to `nuget.config`.

## Running locally (Docker)

The Finance stack attaches to the shared `platform_net` network defined in `Warehouse/docker-compose.platform.yml`.

```powershell
# 1. Make sure Warehouse infrastructure is up (creates platform_net, SQL Server, RabbitMQ, Redis, Loki, Grafana, Jaeger)
cd ../Warehouse
docker compose -f docker-compose.platform.yml up -d
docker compose -f docker-compose.warehouse.yml up -d   # for warehouse-auth-api (until auth-service split is live)

# 2. Bring Finance up
cd ../finance-service
docker compose -f docker-compose.finance.yml up -d --build
```

Then browse:
- Frontend: http://localhost:6100
- Gateway / Swagger via `Finance.Accounts.API`: http://localhost:6001/swagger
- Grafana (shared): http://localhost:3001

## Repository Layout

```
finance-service/
├── CLAUDE.md                          ← Project instructions (always loaded)
├── FINANCE-MICROSERVICES-PLAN.md      ← Full implementation plan
├── README.md                          ← This file
├── nuget.config                       ← GitHub Packages source for Warehouse.* libs
├── global.json                        ← .NET SDK pin
├── docker-compose.finance.yml         ← Finance stack on platform_net
├── docs/
│   ├── README.md
│   ├── cross-reference-map.md
│   ├── core/                          ← Universal engine specs (stubs)
│   ├── domain/
│   │   └── SDD-ACCT-001-chart-of-accounts.md
│   ├── integration/
│   │   └── SDD-INT-AUTH-001-shared-jwt-authentication.md
│   ├── infrastructure/
│   │   ├── SDD-INFRA-001-cross-cutting-foundations.md
│   │   ├── SDD-INFRA-002-finance-gateway.md
│   │   └── SDD-UI-001-frontend-shell.md
│   └── changes/_TEMPLATE.md
├── frontend/                          ← React 18 + Vite + MUI v5
└── src/
    ├── Directory.Build.props
    ├── Finance.slnx
    ├── Finance.Common/
    ├── Finance.ServiceModel/
    ├── Databases/Finance.Accounts.DBModel/
    ├── Interfaces/Accounts/Finance.Accounts.API/
    └── Infrastructure/Gateway/Finance.Gateway/
```

## Country Support

| Country | Status |
|---|---|
| Bulgaria (BG) — НСС chart, ДДС tax, НАП export, BNB rates | Phase 2 target |
| Germany (DE) — SKR03/04, USt, ELSTER, ECB rates | Future |

Adding a new country = implementing `ICountryStrategy` in a new NuGet package. No core code changes.

## Related Repositories

- [warehouse](#) — Warehouse Management System (consumer of Finance read APIs, publisher of source-of-truth domain events)
- [auth-service](https://github.com/radigurev/auth-service) — Shared identity provider library

## License

Proprietary. © M2M Services.
