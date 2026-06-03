# Finance — SDD Documentation

This folder holds the Specification-Driven Development (SDD) documents for the Finance platform.

## Two-Tier Structure

### Tier 1 — System Specs (`SDD-*`)
Describe the **committed, in-force behavior** of the platform. They are the source of truth that tests and code must align with.

**Status lifecycle (a progression):**

| Status | Meaning | Set when |
|---|---|---|
| `Drafted` | Spec authored; may still change or be dropped. Not yet committed. | spec-writer (Phase 1) output |
| `Active` | Accepted and committed — this **will** be built to the end; it **should** be implemented. May be partially or not-yet built. | the spec is accepted / work begins |
| `Implemented` | Code shipped **and** tests pass and the behavior is in force. A spec whose **core** behavior is shipped + tested is `Implemented` even if it carries explicit `Deferred:` notes for later-phase sub-items. | after the test + validate phases (Phase 3–4) pass |
| `Deprecated` | Retired or superseded; no longer in force. | the behavior is removed/replaced |

The pipeline drives the transitions: spec-writer authors as `Drafted`; the spec becomes `Active` once committed/in-progress; it becomes `Implemented` only after Phase 3 (test) and Phase 4 (validate) pass. (This overrides the generic `Draft / Active / Deprecated` vocabulary in the global `doc-governance` persona for this repo.)

| Folder | Scope |
|---|---|
| `core/` | Universal engine — double-entry, journal lifecycle, posting, periods, currency |
| `domain/` | Documents, payments, country strategy, sub-ledgers, reporting |
| `integration/` | Warehouse events, auth integration, BNB rates, НАП export |
| `infrastructure/` | Gateway, observability, correlation, MassTransit + outbox, idempotency, feature flags, sequences |

### Tier 2 — Change Specs (`CHG-*`)
Describe **proposed changes**. They live in `changes/` until merged, at which point the originating system spec is updated and the change spec is archived.

| Prefix | Use For |
|---|---|
| `CHG-FEAT-NNN` | New features or capabilities |
| `CHG-ENH-NNN` | Enhancements to existing behavior |
| `CHG-FIX-NNN` | Bug fixes |
| `CHG-REFAC-NNN` | Refactoring (no behavior change) |
| `CHG-DEBT-NNN` | Technical debt reduction |

Template: `changes/_TEMPLATE.md`.

## Spec ID Format

`SDD-<DOMAIN>-<NNN>` where DOMAIN is one of:

| Domain | Meaning |
|---|---|
| `FIN` | Core finance engine (journal, GL, posting, periods, currency) |
| `ACCT` | Chart of Accounts |
| `INV` | Invoices (Purchase + Sale + Credit/Debit Notes) |
| `PAY` | Payments + Allocations |
| `RPT` | Reporting (Statements, VAT journals) |
| `CTRY` | Country strategy contracts |
| `CTRY-BG`, `CTRY-DE`, … | Country-specific strategies |
| `INT-WH` | Warehouse integration |
| `INT-AUTH` | Auth integration |
| `INT-BNB` | BNB exchange-rate provider |
| `INT-NAP` | НАП regulatory export |
| `INFRA` | Cross-cutting infrastructure |
| `OBS` | Observability (logs, traces, metrics) |
| `AUDIT` | Audit trail |
| `EVTLOG` | Event log |
| `UI` | Frontend layout, density, navigation, i18n |
| `UI-FIN` | Frontend feature surfaces |

## Required Sections in Every SDD

1. **Header** — ID, title, status, owners, related specs
2. **Context** — Why this exists, ISA-95 placement, dependencies
3. **Behavior** — MUST / SHOULD / MAY rules
4. **Validation** — Input rules
5. **Errors** — Error codes (constant name + HTTP status + meaning)
6. **Versioning** — How breaking changes are handled
7. **Test Plan** — Specific test names with `[Unit]` / `[Integration]` markers

## Cross-Reference Map

`cross-reference-map.md` is the index that ties every SDD to:
- Test classes that cover it
- Implementation files (controller, service, repository, EF config)
- Frontend feature(s) (if applicable)

Update it in the same PR as the spec.
