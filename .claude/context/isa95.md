# ISA-95 (IEC 62264) — Finance Service Classification Guide

> Last updated: 2026-06-03
> Referenced by: `CLAUDE.md` §0.3 (Always-Active Cross-Cutting — "ISA-95 compliance") and §0.4 (context index).
> Used by: the `isa95-validate` pipeline phase (Phase 5) and by `spec-writer` when adding the ISA-95 paragraph to a new SDD.

This is the authoritative in-repo reference for how the Finance platform applies ISA-95 / IEC 62264. Before this file existed, validators inferred the convention from `SDD-FIN-001`/`-002`; this file makes that convention explicit. If a spec's ISA-95 section conflicts with this guide, this guide wins (raise a `CHG-*` to reconcile).

---

## 1. What ISA-95 is, and why Finance uses it

ISA-95 (international form: **IEC 62264**) is the standard for integrating **enterprise/business systems** with **manufacturing operations**. It is NOT an accounting standard — accounting correctness is governed by double-entry rules, IFRS/GAAP, and (for Bulgaria) НСС + НАП. ISA-95 is used here for one reason: **the Finance platform is part of a Warehouse/manufacturing ecosystem** (shared MassTransit event mesh + gateway integration). ISA-95 gives a shared vocabulary across the two systems and enforces a clean boundary.

### The functional hierarchy (levels)

| Level | Concern | Examples | Owned by |
|---|---|---|---|
| **4** | **Business planning & logistics** | ERP, **finance & accounting**, order management, business-level planning | **This Finance platform** |
| 3 | Manufacturing operations management (MES) | scheduling, dispatching, production tracking, inventory operations | Warehouse / WMS |
| 0–2 | Process control & physical process | PLC, SCADA, sensors, actuators | (out of scope) |

**The Finance platform operates entirely at Level 4.** Its job is to record the financial consequences of business transactions. It consumes Level-4 facts that the Warehouse ecosystem emits (goods receipts, shipments, production completions) and turns them into ledger entries — but it MUST NOT itself perform Level-3 operational activities (scheduling production, dispatching work, moving stock).

---

## 2. Classification taxonomy for Finance artifacts

Every new entity, operation, and event is classified into one of these Level-4 roles. The spec's ISA-95 paragraph MUST state which.

| Role | Definition | Examples in this repo |
|---|---|---|
| **Business-transaction record** | The canonical Level-4 record of a financial state change. Immutable once committed. | `JournalEntry`, `Invoice` (future), `Payment` (future) |
| **Transaction line / component** | A child of a business-transaction record with no independent lifecycle. | `JournalEntryLine` |
| **Reference / master data** | Slowly-changing Level-4 data that transactions reference or are scoped by. | `Account` (CoA), `Currency`, `FiscalPeriod` |
| **Audit / status sub-record** | Append-only Level-4 record of who/when/why a transaction or master record changed state. Never updated or deleted. | `JournalEntryStatusHistory`, `FiscalPeriodStatusHistory`, `audit.OperationsEvents` |
| **Domain event** | An immutable Level-4 notification that a state change occurred, published via the transactional outbox. | `JournalEntryPostedEvent`, `FiscalPeriodClosedEvent` |

---

## 3. Operation → Level-4 activity mapping

Classify each operation; only **state-changing** operations require an immutable event.

| Operation kind | ISA-95 Level-4 activity | Event required? |
|---|---|---|
| Create/maintain reference data (generate periods, create account) | Master-data maintenance | No (audit row only) |
| Draft a transaction | Recording a not-yet-committed business transaction | No |
| Commit a transaction (post a journal, confirm an invoice) | Committing a Level-4 financial business transaction | **Yes** (immutable, via outbox) + audit row |
| Correct a committed transaction (reverse, credit note) | Correcting a committed transaction by a new offsetting one (never mutate) | **Yes** + audit row |
| Business-planning control (close/reopen a fiscal period) | Level-4 planning/governance control | **Yes** + audit row + `Reason` |
| Read / query | Level-4 read | No |

---

## 4. Hard rules (what the `isa95-validate` phase checks)

A change is ISA-95-compliant for Level 4 when ALL hold:

1. **Every new entity is classified** (§2) in its spec's ISA-95 paragraph.
2. **Every new state-changing operation is mapped** (§3) to a Level-4 activity and emits an immutable domain event via the outbox.
3. **The spec carries the header line** `> ISA-95: Level 4 (Business Planning & Logistics) — <area>` and an "ISA-95 classification" paragraph citing **IEC 62264 Part 1** (the object-model part).
4. **State changes are immutable & event-sourced**: committed records are never UPDATEd; corrections are new offsetting records; status-history rows are append-only.
5. **No Level-3 leakage**: the change introduces no operational/scheduling/production-execution logic. Operational facts arrive as already-computed Level-4 data (via events/Refit), never as Level-3 operations performed inside Finance.
6. **Cross-service reads stay Level-4↔Level-4** (e.g. Journal → Periods `by-date`, Journal → Accounts/Currencies): routed through the gateway, no cross-database joins.

### Common violations to flag

- An entity with no classification paragraph.
- A state change with no immutable event (or one published outside the outbox).
- An `UPDATE` path on a committed transaction (should be reverse/offset).
- Finance code that schedules, dispatches, or executes a Level-3 operation, or that joins across another service's database.
- A status-history / audit record that is mutable.

---

## 5. House style (match the existing specs)

New specs MUST match the ISA-95 section style established by `docs/core/SDD-FIN-001`, `-002`, and `-004`:

- Header line: `> ISA-95: Level 4 (Business Planning & Logistics) — Bookkeeping` (adjust the trailing area).
- A `## 1. Context & Scope` paragraph titled **"ISA-95 classification."** that: names each entity and its role (§2); states which operations are Level-4 state changes emitting immutable events (§3); explicitly says "No Level-3 (MES) production activity is modelled"; and identifies the reference/master data consumed.

---

## 6. Scope note

ISA-95 here is an **integration & boundary discipline**, not an accounting-compliance requirement. It is valuable only because Finance integrates with a manufacturing/warehouse ecosystem. Accounting correctness is owned by the double-entry engine (`SDD-FIN-001`), the country strategy (`SDD-CTRY-001`, future), and Bulgarian regulatory specs (НСС/НАП). If the Warehouse integration were ever removed, the ISA-95 layer could be retired without affecting the ledger.
