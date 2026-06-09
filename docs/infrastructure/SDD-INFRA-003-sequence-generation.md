# SDD-INFRA-003 — Centralized Sequence Generation (Auto-Code)

> Status: Implemented (library: entity + config + generator + DI). Deferred: per-service `infrastructure.Sequences` table/migration (Batch 4+), `ICountryStrategy` format integration (until SDD-CTRY-001 is authored).
> Owner: Platform
> Last updated: 2026-05-30
> Category: Infrastructure
> Related: SDD-INFRA-001, SDD-ACCT-001, SDD-INV-001 (future), SDD-PAY-001 (future), SDD-INT-NAP-001 (future), SDD-CTRY-001 (future — country format seam)
> Mirrors: Warehouse `SDD-INFRA-003`
> Implementation: `src/Infrastructure/Sequences/Finance.Infrastructure.Sequences/` (references `Finance.Common`)

---

## 1. Context & Scope

This spec defines `Finance.Infrastructure.Sequences`, a centralized sequence/number generator that every Finance microservice uses to produce gapless, formatted, unique document numbers and entity auto-codes. It replaces ad-hoc inline generation methods scattered across services. The Bulgarian tax authority (НАП) requires **gapless** numbering per document type per fiscal year — no skipped or duplicate numbers under concurrent users. This service makes that guarantee using row-level locking (`UPDLOCK, HOLDLOCK`) on a single `infrastructure.Sequences` table.

The generator is country-agnostic at the core. Formatting goes through an **`IDocumentNumberFormatter` seam** (see §2.6) with a shipped `DefaultDocumentNumberFormatter` (BG-style patterns from §2.1). When SDD-CTRY-001 is authored, `ICountryStrategy.GenerateDocumentNumber(...)` will supply the per-country formatter so a deployment in DE can produce different prefixes than BG without changing the generator code. **That `ICountryStrategy` integration is explicitly deferred until SDD-CTRY-001 exists** — this batch ships only the seam plus the default formatter.

### Batch-3 resolved decisions

- **Library location:** `src/Infrastructure/Sequences/Finance.Infrastructure.Sequences/`, references `Finance.Common`. Built with `dotnet build` on its own `.csproj`; it does **not** add itself to `src/Finance.slnx` (the Integrate step does that).
- **Ships now (Status Active):** `ISequenceGenerator`, `SequenceGenerator` implementation, `SequenceCounter` entity, its `IEntityTypeConfiguration` (schema `infrastructure`, table `Sequences`), the `IDocumentNumberFormatter` seam + `DefaultDocumentNumberFormatter`, the 7 built-in keys, and `AddSequenceGenerator<TDbContext>()`.
- **Deferred:** the physical `infrastructure.Sequences` table and EF migration land in each **publishing service DbContext** later (Batch 4+) — the library ships the entity + configuration only, not a migration. `ICountryStrategy.GenerateDocumentNumber` integration is deferred to SDD-CTRY-001.
- Error-code constants are referenced from `Finance.Common.ErrorCodes.SequenceErrorCodes` (`UNKNOWN_SEQUENCE_KEY`, `SEQUENCE_GAP_DETECTED`) — never raw strings.

**In scope:**
- `ISequenceGenerator` interface and `SequenceGenerator` implementation in `Finance.Infrastructure.Sequences`
- `SequenceCounter` entity (`Key`, `CurrentValue`, `ModifiedAt`) + `IEntityTypeConfiguration` mapped to schema `infrastructure`, table `Sequences`
- `IDocumentNumberFormatter` seam + `DefaultDocumentNumberFormatter` (BG-style pattern) for prefix, padding, reset policy, fiscal-year segment
- Reset policies: `Yearly` (per fiscal year — required for НАП), `Monthly`, `Daily`, `Never`
- DI extension `services.AddSequenceGenerator<TDbContext>()` per microservice
- Built-in finance sequences: see §2.1 table

**Out of scope:**
- Distributed sequence generation across multiple SQL Server instances (single instance assumed; each service has its own DB)
- Sequence reservation / batch allocation
- A REST admin API for sequences (deferred)
- The physical `infrastructure.Sequences` table + EF migration — owned by each publishing service DbContext (Batch 4+); the library ships entity + configuration only
- `ICountryStrategy` format integration — deferred until SDD-CTRY-001 is authored; the `IDocumentNumberFormatter` seam stands in until then

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

All format patterns are produced by the registered `IDocumentNumberFormatter` (§2.6). The shipped `DefaultDocumentNumberFormatter` emits the BG defaults shown above. When SDD-CTRY-001 lands, `ICountryStrategy.GenerateDocumentNumber(DocumentType, fiscalYear, sequence)` will provide the formatter so a new country can register different prefixes without changing the generator. That integration is **deferred** (see §2.6).

### 2.2 `NextAsync(sequenceKey, ct)` (MUST)
1. Resolve the sequence definition for the key from the registered definitions (built-in keys; per-country additions arrive with SDD-CTRY-001).
2. Compute composite key by reset policy: `{key}:{yyyy}` for Yearly, `{key}:{yyyyMM}` for Monthly, `{key}:{yyyyMMdd}` for Daily, `{key}` for Never.
3. If the caller already has an open (ambient) DB transaction, **enlist in it** — the generator MUST NOT open a nested transaction (SQL Server forbids nested transactions and throws `InvalidOperationException`). Otherwise open a transaction with `IsolationLevel.Serializable`. Either path relies on the row lock in step 4 for the gapless guarantee, so the `UPDLOCK, HOLDLOCK` hints serialize counter access regardless of the ambient isolation level. (See `CHG-FIX-001`.)
4. `SELECT ... WITH (UPDLOCK, HOLDLOCK)` against the `SequenceCounter` row (`infrastructure.Sequences`). If no row, INSERT with `CurrentValue = 1`. Otherwise UPDATE `CurrentValue = CurrentValue + 1` and `ModifiedAt = SYSDATETIMEOFFSET()`.
5. Commit only the transaction this method itself opened. When enlisted in the caller's ambient transaction, the caller owns commit/rollback — so number allocation is atomic with the document it numbers (this is the stronger, preferred path used by journal posting/reversal).
6. Hand the new counter + fiscal year to the registered `IDocumentNumberFormatter` (§2.6). Return the formatted string.

The method MUST return a unique sequential value even under concurrent callers. It MUST NOT use any caching layer. All library async members MUST pass the `CancellationToken` through and use `ConfigureAwait(false)`.

### 2.6 Document-number formatting seam (MUST)
- Formatting MUST go through `IDocumentNumberFormatter.Format(sequenceKey, fiscalYear, counter)`; the generator MUST NOT inline prefix/padding logic.
- A `DefaultDocumentNumberFormatter` MUST be registered by default. It produces the BG-style patterns of §2.1 (prefix, fiscal-year segment, zero-padded counter at the key's padding width).
- `ICountryStrategy.GenerateDocumentNumber(...)` integration is **deferred** until SDD-CTRY-001 is authored. At that point a country-specific `IDocumentNumberFormatter` MAY replace the default via DI; no change to `SequenceGenerator` is required (the seam is the only extension point).

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

Batch-3 unit tests live in `src/Infrastructure/Finance.Infrastructure.Tests`. EF-touching unit tests use SQLite in-memory and run by default. Tests requiring real SQL Server row-level locking (`UPDLOCK, HOLDLOCK` concurrency) are `[Category("Integration")]` and excluded from the default offline run.

| Test | Kind |
|---|---|
| `NextAsync_ReturnsFormattedValueForRegisteredKey` | [Unit] (SQLite in-memory) |
| `NextAsync_IncrementsCounterPerCall` | [Unit] (SQLite in-memory) |
| `NextAsync_StartsAtOne_ForNewFiscalYear` | [Unit] (SQLite in-memory) |
| `NextAsync_ConcurrentCallers_ProduceUniqueSequentialValues` | [Integration] (real SQL Server lock semantics) — Deferred as a standalone generator test; the gapless-under-concurrency guarantee is covered end-to-end by `Post_ConcurrentCallers_AllocateUniqueGaplessJeNumbers_NoGaps` (below), which drives this generator through the real posting path |
| `JournalEndpointIntegrationTests` post/reverse (allocation inside the caller's ambient transaction — `CHG-FIX-001` regression guard) | [Integration] (real SQL Server, Testcontainers) — `Finance.Journal.API.Tests` |
| `Post_ConcurrentCallers_AllocateUniqueGaplessJeNumbers_NoGaps` (N concurrent posts → unique contiguous gapless `JE` numbers, no gaps/dupes — §2.3 guarantee under load) | [Integration] (Batch 15, green) — `Finance.Journal.API.Tests` |
| `NextAsync_Throws_WhenSequenceKeyNotRegistered` | [Unit] |
| `DefaultDocumentNumberFormatter_ProducesBgPattern_WithPadding` | [Unit] |
| `SequenceGenerator_UsesRegisteredFormatter_ForOutput` | [Unit] (SQLite in-memory) |
| `SequenceDefinitions_AllBuiltInKeysAreUnique` | [Unit] |

## 7. Open Items

- **Deferred — per-service table/migration:** the `infrastructure.Sequences` table + EF migration land in each publishing service DbContext in Batch 4+. The library ships only the `SequenceCounter` entity + `IEntityTypeConfiguration`.
- **Deferred — `ICountryStrategy` integration:** when SDD-CTRY-001 is authored, the country strategy supplies the `IDocumentNumberFormatter`. Until then `DefaultDocumentNumberFormatter` (BG-style) is the only formatter.
- Year-end rollover ceremony: how to safely retire 2026 sequences and create 2027 (automatic per Yearly policy, but ops verification needed).
- Multi-tenant: when multi-country deployments arrive, sequences MUST be partitioned by country code in the composite key.
