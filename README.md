# Finance Service

Finance & accounting platform with a country-agnostic core and pluggable Country Strategy. **ISA-95 Level 4** (Business Planning & Logistics). Integrates with the [Warehouse Management System](../warehouse) via shared MassTransit events (RabbitMQ) and Gateway REST APIs. Shares JWT authentication with Warehouse through the [auth-service](https://github.com/radigurev/auth-service) library.

## Status

**Phase 0 — Repo bootstrap.** SDD documentation and project plan in place; source code not yet scaffolded.

## Architecture

Module-per-service decomposition (7 backend microservices + EventLog + Gateway + React SPA):

| Service | Port | Purpose |
|---|---|---|
| `Finance.Gateway` | 6000 | YARP reverse proxy — single external surface; also the proxy Warehouse calls into |
| `Finance.Accounts.API` | 6001 | Chart of Accounts |
| `Finance.Periods.API` | 6002 | Fiscal Periods |
| `Finance.Currency.API` | 6003 | Currencies + Exchange Rates (BNB provider) |
| `Finance.Journal.API` | 6004 | Journal Entries + GL + Posting Engine |
| `Finance.Invoices.API` | 6005 | Purchase + Sale invoices + lifecycle |
| `Finance.Payments.API` | 6006 | Payments + Allocations + Matching |
| `Finance.Reporting.API` | 6007 | Statements + Tax journals + Regulatory exports |
| `Finance.EventLog.API` | 6008 | Inbound event log (Warehouse + Finance) |
| `Finance.Frontend` | 6100 | React 18 + Vite + MUI v5 SPA |

Plus shared libraries published to GitHub Packages: `Finance.Common`, `Finance.ServiceModel`, `Finance.Infrastructure`, `Finance.Country.Abstractions`, `Finance.Country.BG`, `Finance.Mapping`, `Finance.GenericFiltering`.

## Tech Stack

- **Backend:** .NET 8, ASP.NET Core, EF Core, AutoMapper, FluentValidation, MassTransit (Transactional Outbox) + RabbitMQ, Refit + Polly (`Microsoft.Extensions.Http.Resilience`)
- **Frontend:** React 18 + TypeScript + Vite + MUI v5 + React Router v6 + TanStack Query + Zustand + react-i18next (EN + BG)
- **Database:** SQL Server (shared instance with Warehouse, database-per-service)
- **Cache:** Redis (reference data only)
- **Observability:** NLog → Loki, OpenTelemetry → Jaeger, Grafana
- **Auth:** Shared with Warehouse via `Warehouse.Auth.Shared` NuGet (GitHub Packages) — JWT validation only, no separate user store

## Repository Layout

```
finance-service/
├── CLAUDE.md                          ← Project instructions (always loaded)
├── FINANCE-MICROSERVICES-PLAN.md      ← Full implementation plan
├── README.md                          ← This file
├── docs/
│   ├── README.md                      ← SDD documentation guide
│   ├── cross-reference-map.md         ← Spec ↔ test ↔ impl traceability
│   ├── core/                          ← Universal engine specs (double-entry, journal, posting, periods, currency)
│   ├── domain/                        ← Documents, payments, country strategy, sub-ledgers, reporting
│   ├── integration/                   ← Warehouse events, auth, BNB, НАП
│   ├── infrastructure/                ← Gateway, observability, MassTransit, outbox, sequences
│   └── changes/                       ← CHG-* change specs (template in _TEMPLATE.md)
└── src/                               ← (to be added in Phase 1)
```

## Country Support

| Country | Status |
|---|---|
| Bulgaria (BG) — НСС chart, ДДС tax, НАП export, BNB rates | Phase 2 target |
| Germany (DE) — SKR03/04, USt, ELSTER, ECB rates | Future |

Adding a new country = implementing `ICountryStrategy` in a new NuGet package. No core code changes.

## Getting Started

Phase 0 only. See `FINANCE-MICROSERVICES-PLAN.md` §9 for the phased implementation roadmap.

## Related Repositories

- [warehouse](#) — Warehouse Management System (consumer of Finance read APIs, publisher of source-of-truth domain events)
- [auth-service](https://github.com/radigurev/auth-service) — Shared identity provider library

## License

Proprietary. © M2M Services.
