# SDD-INFRA-003 — Centralized Sequence Generation (Auto-Code)

> Status: Planned
> Owner: Platform
> Last updated: 2026-05-28
> Category: Infrastructure
> Related: SDD-INFRA-001, SDD-ACCT-001, SDD-INV-001 (future), SDD-PAY-001 (future), SDD-INT-NAP-001 (future)
> Mirrors: Warehouse `SDD-INFRA-003`

---

## 1. Context & Scope

This spec defines `Finance.Infrastructure.Sequences`, a centralized sequence/number generator that every Finance microservice uses to produce gapless, formatted, unique document numbers and entity auto-codes. It replaces ad-hoc inline generation methods scattered across services. The Bulgarian tax authority (НАП) requires **gapless** numbering per document type per fiscal year — no skipped or duplicate numbers under concurrent users. This service makes that guarantee using row-level locking (`UPDLOCK, HOLDLOCK`) on a single `infrastructure.Sequences` table.

The generator is country-agnostic at the core; the actual format pattern for each document type comes from `ICountryStrategy.GenerateDocumentNumber(...)` (SDD-CTRY-001) so a deployment in DE can produce different prefixes than BG without changing the generator code.

**In scope:**
- `ISequenceGenerator` interface and `SequenceGenerator` implementation in `Finance.Infrastructure.Sequences`
- `SequenceDefinition` configuration for prefix, padding, reset policy, fiscal-year segment
- Database table `infrastructure.Sequences` (per-service DB) with row-level locking
- Reset policies: `Yearly` (per fiscal year — required for НАП), `Monthly`, `Daily`, `Never`
- DI extension `services.AddSequenceGenerator<TDbContext>()` per microservice
- Built-in finance sequences: see §2.1 table
- Integration with `ICountryStrategy` for format pattern resolution

**Out of scope:**
- Distributed sequence generation across multiple SQL Server instances (single instance assumed; each service has its own DB)
- Sequence reservation / batch allocation
- A REST admin API for sequences (deferred)

## 2. Behavior

### 2.1 Built-in finance sequence keys (MUST)

| Sequence Key | Format Pattern (BG default) | Example | Reset Policy | Padding | Used By |
|---|---|---|---|---|---|
| `JE` | `JE-{yyyy}-{nnnnnn}` | `JE-2026-000001` | Yearly | 6 | Journal Entries |
| `PINV` | `ФПок-{yyyy}-{nnnnnn}` | `ФПок-2026-000001` | Yearly | 6 | Purchase Invoice (НАП ledger) |
| `SINV` | `ФПр-{yyyy}-{nnnnnn}` | `ФПр-2026-000001` | Yearly | 6 | Sale Invoice (НАП ledger) |
| `CN` | `КИ-{yyyy}-{nnnnnn}` | `КИ-2026-000001` | Yearly | 6 | Credit Note |
| `DN` | `ДИ-{yyyy}-{nnnnnn}` | `ДИ-2026-000001` | Yearly | 6 | Debit Note |
| `PAY` | `PAY-{yyyy}-{nnnnnn}` | `PAY-2026-000001` | Yearly | 6 | Payment |
| `RCT` | `RCT-{yyyy}-{nnnnnn}` | `RCT-2026-000001` | Yearly | 6 | Receipt |

All format patterns are produced by `ICountryStrategy.GenerateDocumentNumber(DocumentType, fiscalYear, sequence)` — the table above shows BG defaults. A new country can register different prefixes without changing the generator.

### 2.2 `NextAsync(sequenceKey, ct)` (MUST)
1. Resolve the `SequenceDefinition` for the key from the registered definitions (built-in + per-country additions).
2. Compute composite key by reset policy: `{key}:{yyyy}` for Yearly, `{key}:{yyyyMM}` for Monthly, `{key}:{yyyyMMdd}` for Daily, `{key}` for Never.
3. Open a transaction with `IsolationLevel.Serializable`.
4. `SELECT ... WITH (UPDLOCK, HOLDLOCK)` against `infrastructure.Sequences`. If no row, INSERT with `CurrentValue = 1`. Otherwise UPDATE `CurrentValue = CurrentValue + 1` and `ModifiedAt = SYSDATETIMEOFFSET()`.
5. Commit.
6. Hand the new counter + fiscal year to `ICountryStrategy.GenerateDocumentNumber(...)`. Return the formatted string.

The method MUST return a unique sequential value even under concurrent callers. It MUST NOT use any caching layer.

### 2.3 Concurrent caller behavior (MUST)
Two simultaneous calls for the same `sequenceKey` MUST produce different, sequential values; the second caller waits for the first transaction to commit.

### 2.4 Idempotency for retried document creation (SHOULD)
For invoice/payment creation flows that retry (e.g., from MassTransit retry on a transient DB error), the calling service MUST attach the freshly allocated number to the persisted aggregate **inside the same DB transaction** as the row insert. This prevents number burn on partial failures.

### 2.5 Edge cases (MUST)
- First call for a new fiscal year (Yearly reset): counter starts at 1.
- DB connection lost mid-transaction: caller observes a SQL exception; no number is committed; retry produces the same next value (no skip).
- Application crash after `SaveChanges` but before the response reaches the client: the number IS allocated and the document IS persisted; the client must use an idempotency key on subsequent retries.

## 3. Validation Rules

- `sequenceKey` MUST be non-empty and registered. Unknown keys throw `ArgumentException` with code `UNKNOWN_SEQUENCE_KEY`.
- `Padding` MUST be ≥ 1 and ≤ 12.
- `DateFormat` MUST be a valid `DateTimeOffset.ToString` format.

## 4. Error Rules

| Code | HTTP | Trigger |
|---|---|---|
| `UNKNOWN_SEQUENCE_KEY` | 500 | Service requested a key not registered at startup |
| `SEQUENCE_GAP_DETECTED` | 500 | Audit job (future) finds a gap in a Yearly sequence — НАП violation |

Constants live in `Finance.Common.ErrorCodes.SequenceErrorCodes`.

## 5. Versioning Notes

v1: built-in finance sequence keys per §2.1. Adding a new key is additive (no version bump). Changing an existing format pattern requires a `CHG-ENH-*` change spec because Bulgarian regulations require stability of the per-year sequence shape.

## 6. Test Plan

| Test | Kind |
|---|---|
| `NextAsync_ReturnsFormattedValueForRegisteredKey` | [Integration] |
| `NextAsync_IncrementsCounterPerCall` | [Integration] |
| `NextAsync_StartsAtOne_ForNewFiscalYear` | [Integration] |
| `NextAsync_ConcurrentCallers_ProduceUniqueSequentialValues` | [Integration] |
| `NextAsync_Throws_WhenSequenceKeyNotRegistered` | [Unit] |
| `NextAsync_UsesCountryStrategyFormat_WhenAvailable` | [Integration] |
| `SequenceDefinitions_AllBuiltInKeysAreUnique` | [Unit] |

## 7. Open Items

- Year-end rollover ceremony: how to safely retire 2026 sequences and create 2027 (automatic per Yearly policy, but ops verification needed).
- Per-country prefix overrides: today the `Prefix` field on `SequenceDefinition` is BG-default; `ICountryStrategy.GenerateDocumentNumber` may override.
- Multi-tenant: when multi-country deployments arrive, sequences MUST be partitioned by country code in the composite key.
