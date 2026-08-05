# SDD-CTRY-001 — Country Strategy Interface (minimal v1)

> Status: Implemented (Batch 14 — shipped + 15 green `[Unit]` tests + validated. The lean `ICountryStrategy` seam + a single `BulgariaStrategy` binding. v1 exposed ONLY what SDD-FIN-006 needed: country identity (`CountryCode`, `BaseCurrencyCode`) + `GetDefaultPostingRules()`. The interface is GROWN per spec, never speculatively widened — Batch 16 (SDD-INV-001 / SDD-INT-WH-001) added FOUR shipped members (`ApplyTaxRounding`, `IsValidTaxRate`, `GenerateDocumentNumber`, `StandardTaxRate`), implemented in `BulgariaStrategy`, taking the interface to SEVEN members; **Batch 17 (SDD-PAY-001) added an EIGHTH** — the additive payment-typed overload `GenerateDocumentNumber(PaymentDocumentType, long)` (`ICountryStrategy.cs:93`) — and grew `BulgariaStrategy.GetDefaultPostingRules()` from three templates to **SEVEN** by adding `CREDIT_NOTE`, `DEBIT_NOTE`, `PAYMENT_CUSTOMER_RECEIPT`, and `PAYMENT_SUPPLIER_PAYMENT` (`BulgariaStrategy.cs:101-110`). The whole `Finance.Country.BG.Tests` suite is **38** green `[Unit]` test cases across two fixtures (see §5 growth log, §6.4). The remaining country responsibilities the plan lists — statement layouts, regulatory exports, CoA seed, exchange-rate provider, counterparty validation — stay DEFERRED to their owning specs. No country factory/resolver/registry: a single static `AddSingleton<ICountryStrategy, BulgariaStrategy>()` binding per consuming service (FINANCE-MICROSERVICES-PLAN §1.3 writes `AddScoped`; shipped as `AddSingleton` — §7). Multi-tenant country resolution is DEFERRED to a future `CHG-FEAT-*`.)
> Owner: Finance
> Last updated: 2026-08-05
> Category: Domain
> Projects: `Finance.Country.Abstractions` (the `ICountryStrategy` contract + posting-rule template DTOs) and `Finance.Country.BG` (`BulgariaStrategy`) — shared libraries (FINANCE-MICROSERVICES-PLAN §2.1). Consumed first by `Finance.Journal.API` (port **6004**); since Batch 16/17 also bound in `Finance.Invoices.API` (`Program.cs:105`) and `Finance.Payments.API` (port **6006**, `Program.cs:109`) — one `AddSingleton` binding each, no factory. Tests: `Finance.Country.BG.Tests`.
> Related: SDD-FIN-006 (Posting Engine + Posting Rules — the FIRST v1 consumer; `GetDefaultPostingRules()` exists for FIN-006's `PostingRuleSeeder`), SDD-ACCT-001 (Chart of Accounts — posting-rule line account selectors reference account codes; the BG defaults use НСС account codes; the CoA seed itself is DEFERRED here), SDD-FIN-001 (Double-Entry Engine — a returned template MUST be balanceable so the entry it derives satisfies the balance invariant), SDD-FIN-005 (Multi-Currency Engine — owns the deferred exchange-rate-provider member; `BaseCurrencyCode` here is the single base-currency datum FIN-006 needs), SDD-INFRA-003 (Sequence Generation — owns `NextValueAsync`, the gapless counter every `GenerateDocumentNumber` overload formats; §2.1 there is the authoritative format table), SDD-INV-001 (Invoice Lifecycle — the Batch-16 consumer that grew the tax + invoice-typed document-number members, and the owner of the `CREDIT_NOTE`/`DEBIT_NOTE` templates seeded in Batch 17), SDD-PAY-001 (Payment Recording & Lifecycle — the Batch-17 consumer; its §2.13 specifies the four newly seeded posting-rule templates line-by-line with their sides, and §2.4 specifies the payment-typed document-number overload), SDD-PAY-002 (Payment Allocation & Settlement — consumes the same shipped templates through the payment posting handshake), SDD-INT-NAP-001 (НАП export — owns the deferred regulatory-export members), SDD-CTRY-BG-001 (Bulgaria Strategy — the FULLER BG strategy: tax system, rounding, CoA seed JSON, statement layouts, counterparty validation; this spec is only the minimal seam those grow onto), SDD-RPT-001/-002/-003 (Reporting — own the deferred statement-layout members)
> ISA-95: Level 4 (Business Planning & Logistics) — reference / master-data provider

---

## 1. Context & Scope

`ICountryStrategy` is the seam that keeps the Finance core **country-agnostic** (FINANCE-MICROSERVICES-PLAN §1). Anything country-specific — chart of accounts, tax, document numbering, statement layouts, regulatory exports, posting-rule seeds, rounding, counterparty validation, the base currency and its rate provider — is meant to live behind this interface so the universal engine never branches on a country code.

This spec defines the **minimal v1** of that seam — deliberately lean. The first and only consumer this batch is **SDD-FIN-006** (the Posting Engine), which needs exactly two things from a country: (1) the country's identity and base currency, and (2) a set of **default posting-rule templates** to seed its rule store. Therefore v1 of `ICountryStrategy` exposes ONLY those members. Every other responsibility the plan attributes to a country strategy is named here as a **DEFERRED member owned by another spec**, so the interface is grown one member at a time as each consuming spec lands — never widened speculatively into a fat god-interface (SOLID / Interface Segregation, CLAUDE.md guardrails).

**Pattern = Strategy, registered by plain DI — no factory.** There is exactly ONE implementation in v1 (`BulgariaStrategy`) bound by a single `services.AddScoped<ICountryStrategy, BulgariaStrategy>()` (FINANCE-MICROSERVICES-PLAN §1.3). This spec **explicitly forbids** introducing a country factory, resolver, registry, or `ICountryStrategyProvider` in v1: a factory that always returns one static strategy is the precise over-engineering anti-pattern the project rejects. Multi-tenant country resolution (choosing a strategy per request/tenant) is a real future need but is **DEFERRED to a future `CHG-FEAT-*`** (FINANCE-MICROSERVICES-PLAN §1.3); when it lands it adds a resolver around the *same* interface without changing consumers that depend on the injected `ICountryStrategy`.

**Read-only at runtime — no events, no audit, no DB.** `ICountryStrategy` is a **seed/reference source**, not a stateful aggregate. Its members are pure, deterministic, in-memory reads computed from compiled-in country knowledge (and, later, bundled resource files). It owns no table, publishes no event, writes no audit row, and runs no workflow. Persisted, editable country data (e.g. the posting rules once seeded, the chart of accounts once seeded) is owned by the consuming service's own tables and specs (SDD-FIN-006 owns `posting_rules`; SDD-ACCT-001 owns accounts). The strategy is the *source* of the initial seed, not the system of record.

**ISA-95 classification.** `ICountryStrategy` is an ISA-95 **Level 4 (Business Planning & Logistics)** reference / master-data provider (ISA-95 / IEC 62264 Part 1, §5 — Business Planning & Logistics). It supplies Level-4 master data (base currency, default posting-rule templates) that configure the Level-4 bookkeeping engine. It performs no state change, so it requires **no immutable domain event** (the event obligation in SDD-INFRA-006 applies to state-changing operations — none occur here). No Level-3 (MES) production activity is modelled.

**Scope — covered (v1):**
- The `ICountryStrategy` interface with exactly three members: `CountryCode`, `BaseCurrencyCode`, `GetDefaultPostingRules()`. (Per-spec growth has since taken it to EIGHT — §2.1, §5.)
- The posting-rule template contract DTOs returned by `GetDefaultPostingRules()` — `PostingRuleTemplate` + `PostingRuleLineTemplate` (+ the `PostingDebitOrCredit` / `PostingAmountSource` enums they reference) — defined in `Finance.Country.Abstractions` so both the strategy (producer) and SDD-FIN-006's seeder (consumer) share one shape.
- `BulgariaStrategy` in `Finance.Country.BG`: `CountryCode = "BG"`, `BaseCurrencyCode = "BGN"`, and a small handful of НСС-flavoured default posting-rule templates (sample/seed data — see §2.4).
- Registration as a single `AddScoped<ICountryStrategy, BulgariaStrategy>()` in the consuming service (SDD-FIN-006's `Finance.Journal.API`).

**Scope — excluded (DEFERRED — grow the interface per the owning spec):**
- **Tax calculation & rounding** (`ApplyTaxRounding`, tax-rate lookup) — DEFERRED to SDD-CTRY-BG-001 / SDD-INV-001 (invoice tax) and SDD-FIN-005 (decimal rounding policy). CLAUDE.md §0.3 names `ICountryStrategy.ApplyTaxRounding`; it is added when invoice/tax work lands, not now. **(GROWN since — shipped in Batch 16 per SDD-INV-001; see §5.)**
- **Document-number formatting** (`GenerateDocumentNumber`) — DEFERRED to SDD-INFRA-003 (the gapless sequence generator already exists; the country-specific *format* member is added when a country needs a non-default format). CLAUDE.md §0.3 names it as future. **(GROWN since — the invoice-typed member shipped in Batch 16 per SDD-INV-001, the payment-typed overload in Batch 17 per SDD-PAY-001; see §5.)**
- **Chart-of-accounts seed** (the initial НСС account list) — DEFERRED to SDD-CTRY-BG-001 + SDD-ACCT-001 (SDD-ACCT-001 §1 already records CoA seeding from `ICountryStrategy` as "Phase 2"). The posting-rule templates here *reference* account codes by string; they do NOT seed the accounts themselves.
- **Statement layouts** (Balance Sheet / Income Statement / VAT-journal structure) — DEFERRED to SDD-RPT-001/-002/-003.
- **Regulatory exports** (НАП export definitions + exporters) — DEFERRED to SDD-INT-NAP-001.
- **Exchange-rate provider** (the BNB rate source) — DEFERRED to SDD-FIN-005 / SDD-INT-BNB-001. `BaseCurrencyCode` is the only currency datum v1 exposes.
- **Counterparty / legal-metadata validation** (EIK/VAT-number format, legal entity rules) — DEFERRED to SDD-CTRY-BG-001.
- **Multi-tenant country resolution** (a factory/resolver choosing a strategy per request) — DEFERRED to a future `CHG-FEAT-*` (FINANCE-MICROSERVICES-PLAN §1.3). v1 is single-tenant single-binding.

## 2. Behavior

> **Purity & DI contract.** `ICountryStrategy` members MUST be pure, deterministic, side-effect-free reads — no I/O, no DB, no events, no async (none of the v1 members need a `CancellationToken`). The strategy MUST be safe to register as a single `AddScoped` (or `AddSingleton` — it is stateless) binding and MUST be injected by interface; consumers MUST NOT `new BulgariaStrategy()` and MUST NOT branch on `CountryCode` to pick behavior (that is what the strategy abstracts away).

### 2.1 The interface (MUST)
- `ICountryStrategy` MUST live in `Finance.Country.Abstractions` and MUST declare EXACTLY these three members in v1:
  - `string CountryCode { get; }` — the ISO 3166-1 alpha-2 country code this strategy serves (e.g. `"BG"`). Used for tagging seeded reference data (posting rules carry the `CountryCode`, matching SDD-ACCT-001's account `CountryCode`).
  - `string BaseCurrencyCode { get; }` — the ISO 4217 alphabetic base currency the country books in (e.g. `"BGN"`). This is the single base-currency datum the engine needs; the full multi-currency / rate-provider surface is SDD-FIN-005.
  - `IReadOnlyList<PostingRuleTemplate> GetDefaultPostingRules()` — the country's default posting-rule templates, returned as a read-only list for SDD-FIN-006's seeder to upsert into the rule store.
- The interface MUST NOT declare any other member in v1. Each deferred responsibility (§1) is added ONLY by the spec that owns it, with that spec recording the interface growth in its Versioning Notes. This is an explicit Interface-Segregation decision, not an oversight. (Growth has since occurred per this rule, twice: Batch 16 / SDD-INV-001 added the tax + document-number members — `ApplyTaxRounding`, `IsValidTaxRate`, `GenerateDocumentNumber(InvoiceDocumentType, long)`, `StandardTaxRate` — and Batch 17 / SDD-PAY-001 added the payment-typed `GenerateDocumentNumber(PaymentDocumentType, long)` overload. See the §5 growth log.)
- **Shipped member list (eight, `ICountryStrategy.cs`).** The interface as it stands after Batch 17 is: `CountryCode` (`:20`), `BaseCurrencyCode` (`:26`), `GetDefaultPostingRules()` (`:35`), `StandardTaxRate` (`:44`), `ApplyTaxRounding(decimal)` (`:55`), `IsValidTaxRate(decimal)` (`:65`), `GenerateDocumentNumber(InvoiceDocumentType, long)` (`:77`), and `GenerateDocumentNumber(PaymentDocumentType, long)` (`:93`). The two `GenerateDocumentNumber` members are an OVERLOAD PAIR keyed by document-type enum, not one generic member — growth here was purely additive, so no existing consumer or implementation signature changed (§5).
- `CountryCode` and `BaseCurrencyCode` MUST be non-null, non-empty, and uppercase. They MUST be stable across calls (the same instance always returns the same values).

### 2.2 The posting-rule template contract (MUST)
- `Finance.Country.Abstractions` MUST define the template DTOs that `GetDefaultPostingRules()` returns, so the producer (`BulgariaStrategy`) and the consumer (SDD-FIN-006's `PostingRuleSeeder`) share ONE shape and neither re-declares it (DRY):
  - `PostingRuleTemplate` — `RuleKey` (stable machine key, e.g. `"SALE_INVOICE"`), `Description`, `CountryCode`, and an ordered `IReadOnlyList<PostingRuleLineTemplate> Lines`.
  - `PostingRuleLineTemplate` — `AccountSelector` (the account this line posts to — an НСС account *code* string in v1, e.g. `"411"`; resolved to an `AccountId` by SDD-FIN-006 at seed/apply time against SDD-ACCT-001), `DebitOrCredit` (a `PostingDebitOrCredit` enum: `Debit` | `Credit`), and `AmountSource` (a `PostingAmountSource` enum naming which amount from the apply-time context feeds this line — see SDD-FIN-006 §2.3; v1 values e.g. `Net` | `Tax` | `Gross`).
- These DTOs MUST be plain immutable records with no behavior — they are a data contract, not a domain model. The enums MUST live in `Finance.Country.Abstractions` (shared) — NOT duplicated in `Finance.Common` and the strategy.
- A template MUST be **balanceable in principle**: the lines MUST include at least one `Debit` and at least one `Credit` line, and the set of `AmountSource`s used MUST be such that a balanced entry is *possible* for some context (the *actual* per-context balance check is SDD-FIN-006 §2.4 at apply time — the strategy author cannot know runtime amounts). A template that is structurally impossible to balance (e.g. all-debit) is a defect and MUST be caught by SDD-FIN-006's seed-time validation (`POSTING_RULE_UNBALANCED`).

### 2.3 `GetDefaultPostingRules()` (MUST)
- The method MUST return a non-null, possibly-empty, read-only list. It MUST be deterministic (same templates every call) and MUST NOT perform I/O.
- Every returned `PostingRuleTemplate.CountryCode` MUST equal the strategy's `CountryCode` (a BG strategy MUST NOT return rules tagged for another country).
- Every returned template's `RuleKey` MUST be unique within the returned list (the seeder relies on `RuleKey` as the upsert key — duplicates in the seed source are a defect).
- The returned templates are **seed data**, not the live rule store. SDD-FIN-006 owns the persisted, editable `posting_rules`; this method is only the initial source the seeder reads (mirroring how `ICurrencySeeder` reads a bundled ISO 4217 list — SDD-NOM-001 §2.5).

### 2.4 `BulgariaStrategy` (MUST)
- `BulgariaStrategy` MUST live in `Finance.Country.BG`, implement `ICountryStrategy`, and return `CountryCode = "BG"`, `BaseCurrencyCode = "BGN"`.
- `GetDefaultPostingRules()` MUST return a SMALL, illustrative set of НСС-flavoured templates — a handful, not an exhaustive accounting manual. The v1 set SHOULD be approximately:
  - `SALE_INVOICE` — Debit `411` (Клиенти / customers, `Gross`), Credit `701`/`702` (Приходи от продажби / sales revenue, `Net`), Credit `4532` (ДДС за начисляване / output VAT, `Tax`).
  - `PURCHASE_INVOICE` — Debit `601`/`304` (разходи / стоки — expense/goods, `Net`), Debit `4531` (ДДС за приспадане / input VAT, `Tax`), Credit `401` (Доставчици / suppliers, `Gross`).
  - `CUSTOMER_PAYMENT` — Debit `503`/`501` (Банка / Каса — bank/cash, `Gross`), Credit `411` (Клиенти, `Gross`).
- **GROWN in Batch 17 — the shipped set is SEVEN templates, not three.** `BulgariaStrategy.BuildDefaultRules()` (`BulgariaStrategy.cs:101-110`) returns, in order: `SALE_INVOICE`, `PURCHASE_INVOICE`, `CUSTOMER_PAYMENT` (the three above) plus FOUR added for the payment lifecycle and the note-posting debt SDD-INV-001 §7 had left open. **SDD-PAY-001 §2.13 is the owning spec for their selectors and sides**; this spec records what shipped:
  - `CREDIT_NOTE` (`:150-161`) — the sides of `SALE_INVOICE` REVERSED: **Credit** `411` (`Gross`), **Debit** `701` (`Net`), **Debit** `4532` (`Tax`).
  - `DEBIT_NOTE` (`:163-174`) — the sides of `SALE_INVOICE` REPEATED: **Debit** `411` (`Gross`), **Credit** `701` (`Net`), **Credit** `4532` (`Tax`).
  - `PAYMENT_CUSTOMER_RECEIPT` (`:176-186`) — **Debit** `503` (bank/cash, `Gross`), **Credit** `411` (customers, `Gross`).
  - `PAYMENT_SUPPLIER_PAYMENT` (`:188-198`) — **Debit** `401` (suppliers, `Gross`), **Credit** `503` (bank/cash, `Gross`).
- Each of the four MUST keep the SIDE it shipped with, not merely balance: `PostingEngine.CheckBalanced` compares only total base debits to total base credits, so a template whose sides were copied instead of flipped is exactly as "balanced" as one whose sides were flipped, and `PostingRuleSeeder` never overwrites an existing `RuleKey` — a wrong-sided `CREDIT_NOTE` would make every credit note permanently INCREASE receivables, revenue, and output VAT (SDD-PAY-001 §2.13). FIVE side-asserting `[Unit]` tests — one covering both payment templates, four covering the two notes (including two anti-copy-paste tests that walk every `SALE_INVOICE` line and assert the note takes the opposite / identical side) — pin the selector, the side, AND the `PostingAmountSource` of every line (§6.4).
- The superseded sample `CUSTOMER_PAYMENT` (`503`/`411`) is RETAINED alongside `PAYMENT_CUSTOMER_RECEIPT` and MUST NOT be renamed or reused — the seeder never overwrites, and renaming a live rule key would orphan administrator edits. Retiring it (deactivate, never delete) is a future `CHG-DEBT-*` (SDD-PAY-001 §2.13, §7).
- **Seeding these templates is gated, NOT automatic.** They reach a rule store only through SDD-FIN-006's `IPostingRuleSeeder` behind the `EnablePostingRuleSeeding` feature flag (default `false`), and they remain sample data pending accountant sign-off. "Shipped in `BulgariaStrategy`" therefore MUST NOT be read as "seeded in a running environment" (SDD-PAY-001 §2.13).
- **Document-number formats (`GenerateDocumentNumber`, both overloads).** `Format` (`:78-83`) composes `{prefix}-{yyyy}-{counter}` where `yyyy` is the ambient `DateTimeOffset.UtcNow.Year` and `counter` is the caller-supplied gapless sequence value left-padded to SIX digits. Prefixes are per document type: invoice — `ФПок` (purchase) / `ФПр` (sale) / `КИ` (credit note) / `ДИ` (debit note) (`:85-92`); payment — **`RCT`** (customer receipt) / **`PAY`** (supplier payment) (`:94-99`), i.e. `RCT-{yyyy}-{nnnnnn}` and `PAY-{yyyy}-{nnnnnn}`. The payment prefixes are LATIN, matching the `PAY`/`RCT` rows already in SDD-INFRA-003 §2.1 — they are deliberately NOT Cyrillic, unlike the НАП-ledger invoice prefixes.
  - Padding is a MINIMUM, not a cap: a counter wider than six digits MUST widen the number rather than truncate (`PadLeft`), pinned by a `1000000 → "1000000"` case (§6.4).
  - Neither overload allocates a sequence value and neither takes a date — by design, so a number's year segment and the yearly counter series it came from agree (SDD-PAY-001 §2.4). Note the two clocks are not the same abstraction: `BulgariaStrategy.Format` reads `DateTimeOffset.UtcNow` directly (`:80`), whereas `SequenceGenerator` composes its `{key}:{yyyy}` reset segment from an injected `TimeProvider` (`SequenceGenerator.cs:67`, `SequenceKeyComposer.cs:23`). Both are UTC "now" in production, so the agreement holds; a test that fakes `TimeProvider` MUST expect the strategy to keep using the real UTC year. Routing the strategy through `TimeProvider` too is a candidate `CHG-DEBT-*`, not a shipped guarantee.
  - The `PAY` / `RCT` prefixes and the six-digit padding here match the shipped `SequenceDefinitions` entries for the same document types (`SequenceDefinitions.cs:73-74`, both `Yearly`), so the country format and the counter definition do not disagree.
  - An unrecognized `InvoiceDocumentType`/`PaymentDocumentType` throws `ArgumentOutOfRangeException` (`:91`, `:98`). This is a programming-defect guard on a closed enum, not a business outcome, and it is unreachable from the payment path: the type is screened at draft creation by `PaymentDocumentTypeMap.IsSupported` returning `INVALID_PAYMENT_DOCUMENT_TYPE` (`PaymentService.cs:124-129`, SDD-PAY-001 §2.3) long before confirm formats the number (`PaymentService.cs:836-838`). **Gap:** no test currently pins either throw; adding one is a tester follow-up (§6.4).
- These templates are **explicitly sample/seed data that an accountant MUST validate** before production use (FINANCE-MICROSERVICES-PLAN §10 risk #1 — "posting rules need accountant sign-off"). The spec MUST NOT claim regulatory correctness; the exact account codes are a starting point that SDD-CTRY-BG-001 (the fuller BG strategy) and the accountant refine. The seeded rules are editable in the rule store afterward (SDD-FIN-006 §2.1).
- The implementation MUST be stateless and pattern = **Strategy** (one implementation, plain DI). No reflection-based discovery, no plugin loader in v1.

### 2.5 Registration (MUST — no factory)
- The consuming service (SDD-FIN-006's `Finance.Journal.API`) MUST register the strategy with a single binding: `services.AddScoped<ICountryStrategy, BulgariaStrategy>()` (or `AddSingleton`, since the strategy is stateless — the implementator decides; §7). 
- A country factory, resolver, registry, or `ICountryStrategyProvider` MUST NOT be introduced in v1. Consumers MUST depend on the injected `ICountryStrategy` directly. (When multi-tenant resolution lands as a future CHG, it wraps the same interface — consumers are untouched.)
- **Shipped bindings (three consumers, one binding each).** `Finance.Journal.API` (`Program.cs:108`), `Finance.Invoices.API` (`Program.cs:105`), and — added in Batch 17 — `Finance.Payments.API` (`Program.cs:109`) each register exactly `services.AddSingleton<ICountryStrategy, BulgariaStrategy>()`. Every additional consumer MUST follow the same shape: one `AddSingleton` in its own composition root, no shared registrar, no factory. **Coverage caveat:** `CountryStrategy_RegisteredAsSingleScopedBinding_NoFactory` (§6.3) asserts the single-binding/no-factory property against a hand-built `ServiceCollection` using `AddScoped`; it does NOT read any real `Program.cs`, so the three shipped registrations are verified by inspection only, and the test's name says `Scoped` where production uses `Singleton`. Renaming it and/or asserting the real composition roots is a tester follow-up (§6.4).

### 2.6 Edge cases (MUST)
- **Empty rule set.** A strategy whose `GetDefaultPostingRules()` returns an empty list MUST be valid (a country with no default rules seeds nothing). The consuming seeder (SDD-FIN-006) MUST treat an empty seed as a no-op, not an error.
- **Duplicate `RuleKey` in the seed source.** If `GetDefaultPostingRules()` returns two templates with the same `RuleKey`, that is a defect in the strategy. A `[Unit]` test MUST assert `BulgariaStrategy` returns no duplicate `RuleKey`s (so the defect is caught in the producer, before the seeder's own duplicate handling — SDD-FIN-006 §2.6).
- **Structurally unbalanceable template.** A template with no `Debit` line OR no `Credit` line MUST be treated as a defect; a `[Unit]` test MUST assert every `BulgariaStrategy` template has at least one debit and one credit line. (The per-context numeric balance is SDD-FIN-006's concern.)
- **Country-code consistency.** Every template's `CountryCode` MUST equal the strategy's `CountryCode`; a `[Unit]` test MUST assert this for `BulgariaStrategy`.

## 3. Validation Rules

### 3.1 Field-level (contract invariants, asserted by tests — not request validation)

`ICountryStrategy` is not an HTTP-request surface, so there is no FluentValidation here. The invariants are contract guarantees asserted by `[Unit]` tests against the implementation.

| Member | Rule | Asserted by |
|---|---|---|
| `CountryCode` | Non-null, non-empty, uppercase, stable | `BulgariaStrategy_CountryCode_IsBG` |
| `BaseCurrencyCode` | Non-null, non-empty, uppercase ISO 4217 | `BulgariaStrategy_BaseCurrencyCode_IsBGN` |
| `GetDefaultPostingRules()` | Non-null read-only list, deterministic | `BulgariaStrategy_GetDefaultPostingRules_IsDeterministic` |
| `PostingRuleTemplate.RuleKey` | Unique within the returned list | `BulgariaStrategy_DefaultRules_HaveUniqueRuleKeys` |
| `PostingRuleTemplate.CountryCode` | Equals the strategy's `CountryCode` | `BulgariaStrategy_DefaultRules_AllTaggedBG` |
| `PostingRuleTemplate.Lines` | ≥ 1 `Debit` line AND ≥ 1 `Credit` line | `BulgariaStrategy_EveryTemplate_HasDebitAndCreditLine` |
| `PostingRuleLineTemplate.AmountSource` | Only `Net` / `Tax` / `Gross` are used | `BulgariaStrategy_DefaultRules_UseOnlyNetTaxGrossSources` |
| `GenerateDocumentNumber(PaymentDocumentType, long)` | `RCT-{yyyy}-{nnnnnn}` / `PAY-{yyyy}-{nnnnnn}`; six-digit minimum padding, never truncating | `BulgariaStrategy_GeneratesPaymentDocumentNumber_WithCountryPrefixAndPaddedCounter`, `BulgariaStrategy_GeneratesPaymentDocumentNumber_PadsCounterToSixDigits` |

### 3.2 Cross-field / structural

| Rule | Mechanism | Surfaced as |
|---|---|---|
| A template is structurally balanceable (has both sides) | structural check in the producing strategy; numeric check deferred to consumer | SDD-FIN-006 `POSTING_RULE_UNBALANCED` at seed/apply time |
| Seed `RuleKey`s are unique | producer responsibility | SDD-FIN-006 seeder upserts by `RuleKey`; a duplicate seed source is a defect |

### 3.3 State-based

`ICountryStrategy` is stateless and read-only — there are no state-based rules. (The persisted posting rules' state is owned by SDD-FIN-006.)

## 4. Error Rules

`ICountryStrategy` has **no runtime error surface**: it is a pure in-memory provider with no HTTP endpoint, no I/O, and no failure mode of its own that produces a ProblemDetails. Contract-invariant violations (§3) are **defects** caught by `[Unit]` tests at build time, not runtime errors.

The one exception is a **defect guard, not an error surface**: both `GenerateDocumentNumber` overloads throw `ArgumentOutOfRangeException` for a document-type enum value the country does not recognize (`BulgariaStrategy.cs:91`, `:98` — §2.4). It is not mapped to a ProblemDetails, MUST NOT be caught and converted into a business failure, and is unreachable from the shipped payment path because the caller screens the type first and returns `INVALID_PAYMENT_DOCUMENT_TYPE` (owned by `PaymentErrorCodes`, SDD-PAY-001 §2.3/§4). If it ever surfaces at runtime it means a new enum member was added without extending the strategy — a build-time omission that MUST be fixed in `BulgariaStrategy`, not handled at the boundary.

The errors that *relate* to country data — `POSTING_RULE_UNBALANCED`, `DUPLICATE_POSTING_RULE_KEY`, `POSTING_RULE_NOT_FOUND`, `MISSING_POSTING_AMOUNT` — are raised by the **consumer** (SDD-FIN-006) when it seeds/validates/applies the templates, and are owned by `Finance.Common/ErrorCodes/PostingErrorCodes.cs` (defined in SDD-FIN-006 §4). This spec introduces **no new error codes**.

(For completeness: if a future deferred member becomes async/I/O-bound — e.g. an exchange-rate provider under SDD-FIN-005 — that member's spec defines its own error surface. Neither Batch-16 nor Batch-17 growth added one — all eight shipped members are synchronous and pure.)

## 5. Versioning Notes

`Finance.Country.Abstractions` (`ICountryStrategy` + template DTOs) and `Finance.Country.BG` (`BulgariaStrategy`) are the v1 shared libraries.

- **v1 — Initial specification (Batch 14).** Lean three-member `ICountryStrategy` (`CountryCode`, `BaseCurrencyCode`, `GetDefaultPostingRules()`); `PostingRuleTemplate`/`PostingRuleLineTemplate` + `PostingDebitOrCredit`/`PostingAmountSource` enums in `Finance.Country.Abstractions`; `BulgariaStrategy` (`BG`/`BGN` + a handful of НСС sample templates) in `Finance.Country.BG`; single `AddScoped<ICountryStrategy, BulgariaStrategy>()` binding; **no factory/resolver/registry**.
- **Interface growth is additive and per-spec** — adding a NEW member to `ICountryStrategy` is a **breaking change to the interface** (every implementation must implement it), so each deferred member is introduced by the spec that needs it, in lock-step with at least one implementation (`BulgariaStrategy`) and a default for any future country. Growth log / order:
  - **Batch 16 (SDD-INV-001 / SDD-INT-WH-001) — GROWN (shipped).** `ICountryStrategy` grew from the three v1 members by FOUR shipped members, implemented in `BulgariaStrategy`:
    - `decimal ApplyTaxRounding(decimal amount)` — rounds a monetary amount per the country rounding policy (`MidpointRounding.AwayFromZero` to 2 dp for BG).
    - `bool IsValidTaxRate(decimal rate)` — whether a tax rate is legal for the country (BG recognizes 20% / 9% / 0%).
    - `string GenerateDocumentNumber(InvoiceDocumentType documentType, long sequenceValue)` — the country-specific document-number FORMAT, fed the raw gapless counter from `ISequenceGenerator.NextValueAsync` (SDD-INFRA-003); BG prefixes ФПок (purchase) / ФПр (sale) / КИ (credit note) / ДИ (debit note).
    - `decimal StandardTaxRate { get; }` — the country's standard rate (BG = 20%).
    The base currency stays `BGN`. SDD-INV-001 §5 is the owning record for the tax + document-number members; SDD-INFRA-003 records the matching `NextValueAsync` member. The interface reached SEVEN members (the three v1 members + these four).
  - **Batch 17 (SDD-PAY-001) — GROWN (shipped, 2026-08-05).** `ICountryStrategy` grew by ONE member and `BulgariaStrategy`'s seed set grew by FOUR templates (SDD-PAY-002 consumes the same shipped templates through the payment posting handshake but drove no growth of its own):
    - `string GenerateDocumentNumber(PaymentDocumentType documentType, long sequenceValue)` (`ICountryStrategy.cs:93`) — the payment-typed OVERLOAD of the Batch-16 member, formatting a gapless counter as `RCT-{yyyy}-{nnnnnn}` (customer receipt) / `PAY-{yyyy}-{nnnnnn}` (supplier payment) — the LATIN prefixes already tabled in SDD-INFRA-003 §2.1, deliberately not Cyrillic. Like its invoice-typed sibling it takes no date and allocates no sequence value (§2.4). The interface is now EIGHT members (the three v1 members + the Batch-16 four + this overload).
    - `BulgariaStrategy.GetDefaultPostingRules()` (`BulgariaStrategy.cs:101-110`) grew from three templates to SEVEN: `PAYMENT_CUSTOMER_RECEIPT` and `PAYMENT_SUPPLIER_PAYMENT` for the payment posting handshake, plus `CREDIT_NOTE` and `DEBIT_NOTE` — the note-posting debt SDD-INV-001 §7 left open in Batch 16 — repaired in the same method change. Sides and selectors as recorded in §2.4; **SDD-PAY-001 §2.13 is the owning specification** for all four.
    - **Breaking-change boundary for the seed set.** ADDING a `RuleKey` is non-breaking: `PostingRuleSeeder` loads the existing keys and inserts only the absent ones (`PostingRuleSeeder.cs:82-98`), so a new key stays inert until the gated seed next runs. CHANGING a shipped template's sides, selectors, or amount sources IS breaking, precisely because that same skip means an already-seeded key is never rewritten — a correction needs a coordinated re-seed / rule-edit plan, not just a code edit (SDD-PAY-001 §2.13).
    - Evidence: 11 new green `[Unit]` test cases in `Finance.Country.BG.Tests/BulgariaStrategyPostingRuleTemplateTests.cs` (six template tests — five of them side-asserting — plus five payment-numbering cases), taking the `Finance.Country.BG.Tests` suite to **38** green `[Unit]` test cases across two fixtures (§6.4). `Finance.Payments.API` became the third consumer of the binding (§2.5). **Deferred:** no `[Integration]` test exercises a seeded BG template end-to-end through `PostingRuleSeeder` — that coverage belongs to SDD-FIN-006 / the deferred Payments integration suite (SDD-PAY-001 §6.7), and no test pins the `ArgumentOutOfRangeException` guards (§2.4).
  - Anticipated further growth:
    - **CoA seed** (`GetDefaultChartOfAccounts()`) — SDD-CTRY-BG-001 / SDD-ACCT-001 Phase 2.
    - **Statement layouts** — SDD-RPT-001/-002/-003.
    - **Regulatory export definitions** — SDD-INT-NAP-001.
    - **Exchange-rate provider** — SDD-FIN-005 / SDD-INT-BNB-001.
    - **Counterparty / legal-metadata validation** — SDD-CTRY-BG-001.
- **Multi-tenant resolution** (a factory/resolver around the interface) is a future `CHG-FEAT-*`; it is additive to consumers (they still inject `ICountryStrategy`) and does NOT change this contract.
- Changing the SHAPE of an existing member (e.g. `GetDefaultPostingRules` return type) is breaking and requires a coordinated bump across `Finance.Country.Abstractions`, every implementation, and SDD-FIN-006.

## 6. Test Plan

> Environment: `ICountryStrategy` is pure in-memory with no DB, broker, or HTTP — every test is a fast `[Unit]` test with no infrastructure. There are no `[Integration]` tests for this spec (the seeding/apply integration that *uses* these templates is owned and tested by SDD-FIN-006). All tests MUST carry `[Category("SDD-CTRY-001")]`. **Shipped location:** a dedicated `src/Country/Finance.Country.BG.Tests` project — `BulgariaStrategyTests` (§6.1–6.3 plus the Batch-16 grown-member rows of §6.4) and `BulgariaStrategyPostingRuleTemplateTests` (the Batch-17 rows of §6.4; class-tagged `SDD-PAY-001` + `SDD-CTRY-001`, with the note-template tests additionally tagged `SDD-INV-001`) — **38** green `[Unit]` test cases in total. No `[Category("SDD-CTRY-001")]` test lives in a consumer suite (§7).

### 6.1 `BulgariaStrategy` identity (Unit)

| Test name | Kind |
|---|---|
| `BulgariaStrategy_CountryCode_IsBG` | [Unit] |
| `BulgariaStrategy_BaseCurrencyCode_IsBGN` | [Unit] |
| `BulgariaStrategy_IdentityValues_AreUppercaseAndStable` | [Unit] |

### 6.2 Default posting-rule templates (Unit)

| Test name | Kind |
|---|---|
| `BulgariaStrategy_GetDefaultPostingRules_ReturnsNonEmptyReadOnlyList` | [Unit] |
| `BulgariaStrategy_GetDefaultPostingRules_IsDeterministic` | [Unit] |
| `BulgariaStrategy_DefaultRules_HaveUniqueRuleKeys` | [Unit] |
| `BulgariaStrategy_DefaultRules_AllTaggedBG` | [Unit] |
| `BulgariaStrategy_EveryTemplate_HasDebitAndCreditLine` | [Unit] |
| `BulgariaStrategy_DefaultRules_UseOnlyNetTaxGrossSources` | [Unit] |
| `BulgariaStrategy_SaleInvoiceTemplate_DebitsReceivableCreditsRevenueAndVat` | [Unit] |
| `BulgariaStrategy_PurchaseInvoiceTemplate_DebitsExpenseAndInputVatCreditsPayable` | [Unit] |
| `BulgariaStrategy_CustomerPaymentTemplate_DebitsCashCreditsReceivable` | [Unit] |

### 6.3 Contract & registration (Unit)

| Test name | Kind |
|---|---|
| `PostingRuleTemplate_AndLineTemplate_AreImmutableRecords_NoBehavior` | [Unit] |
| `PostingAmountSource_DefinesNetTaxGross` | [Unit] |
| `CountryStrategy_RegisteredAsSingleScopedBinding_NoFactory` | [Unit] |

### 6.4 Grown members & Batch-17 seed set (Unit — shipped)

> These cover the members and templates §5's growth log records. The Batch-16 rows live in `BulgariaStrategyTests`; the Batch-17 rows live in `BulgariaStrategyPostingRuleTemplateTests`, which is tagged for BOTH the owning spec (`SDD-PAY-001`) and this one, with the note-template tests additionally tagged `SDD-INV-001`.

| Test name | Kind |
|---|---|
| `BulgariaStrategy_ApplyTaxRounding_RoundsAwayFromZeroToTwoDecimals` | [Unit] |
| `BulgariaStrategy_ApplyTaxRounding_IsDeterministic` | [Unit] |
| `BulgariaStrategy_IsValidTaxRate_AcceptsRecognizedRates` | [Unit] — `0.20` / `0.09` / `0.00` |
| `BulgariaStrategy_IsValidTaxRate_RejectsUnrecognizedRates` | [Unit] — `-0.01` / `0.05` / `0.21` |
| `BulgariaStrategy_GenerateDocumentNumber_PrefixesPerDocumentType` | [Unit] — `ФПок` / `ФПр` / `КИ` / `ДИ` |
| `BulgariaStrategy_SeedsPaymentCustomerReceiptAndPaymentSupplierPaymentRules_WithAssertedSelectorsAndSides` | [Unit] |
| `BulgariaStrategy_RetainsSupersededCustomerPaymentRule_AlongsideNewPaymentCustomerReceiptRule` | [Unit] |
| `BulgariaStrategy_SeedsCreditNoteRule_WithSidesMirroringSaleInvoice` | [Unit] |
| `BulgariaStrategy_CreditNoteRule_TakesOppositeSideToSaleInvoice_ForEveryAccountSelector` | [Unit] |
| `BulgariaStrategy_SeedsDebitNoteRule_WithSidesRepeatingSaleInvoice` | [Unit] |
| `BulgariaStrategy_DebitNoteRule_RepeatsEverySaleInvoiceLine_SelectorSideAndAmountSource` | [Unit] |
| `BulgariaStrategy_GeneratesPaymentDocumentNumber_WithCountryPrefixAndPaddedCounter` | [Unit] — `RCT` / `PAY` |
| `BulgariaStrategy_GeneratesPaymentDocumentNumber_PadsCounterToSixDigits` | [Unit] — `1` / `999999` / `1000000` |

**Known gaps (tester follow-up — recorded, NOT claimed as covered):**
- No test pins the `ArgumentOutOfRangeException` thrown by either `PrefixFor` overload for an out-of-range document-type enum (§2.4). Unreachable from the shipped payment path, but unasserted.
- `StandardTaxRate` has NO test in `Finance.Country.BG.Tests` at all (verified: the string does not appear in the project) — neither its BG value (`0.20`) nor the contract stated on `ICountryStrategy.cs:42` that the value MUST satisfy `IsValidTaxRate`.
- No `[Integration]` test drives a shipped BG template through `PostingRuleSeeder` into a real rule store; that seam is owned by SDD-FIN-006 and by the deferred Payments integration suite (SDD-PAY-001 §6.7).
- `CountryStrategy_RegisteredAsSingleScopedBinding_NoFactory` asserts the binding property on a hand-built `ServiceCollection`, not on the three shipped composition roots, and its name says `Scoped` where all three register `AddSingleton` (§2.5).

## 7. Resolved Decisions & Open Items

### Resolved
- **Lean three-member interface.** `ICountryStrategy` v1 = `CountryCode`, `BaseCurrencyCode`, `GetDefaultPostingRules()` — only what SDD-FIN-006 needs. Every other country responsibility is DEFERRED to its owning spec; the interface grows per spec (§5). This is a deliberate Interface-Segregation / anti-overengineering decision. **Holding as designed:** two batches of growth have added five members (Batch 16: four; Batch 17: one) — each demanded by a shipped consumer, none speculative — so the interface stands at eight members (§2.1) and no deferred responsibility was pulled forward.
- **No factory.** A single `AddScoped<ICountryStrategy, BulgariaStrategy>()` binding per consuming service — shipped as `AddSingleton`, see the Open item below; no factory/resolver/registry in v1 (FINANCE-MICROSERVICES-PLAN §1.3). Multi-tenant resolution is a deferred CHG.
- **Strategy pattern, plain DI.** One implementation (`BulgariaStrategy`), stateless, injected by interface. No reflection/plugin discovery.
- **Two projects.** `Finance.Country.Abstractions` (interface + template DTOs + enums — the shared contract) and `Finance.Country.BG` (`BulgariaStrategy`). The template DTOs live in Abstractions so producer and consumer (SDD-FIN-006) share one shape (DRY).
- **BG templates are sample/seed data.** A handful of НСС-flavoured templates needing accountant validation (Plan §10 risk #1); not claimed regulatorily correct; editable after seeding in SDD-FIN-006's rule store.
- **Read-only, no events/audit/DB.** A seed source, not a system of record. ISA-95 L4 master-data provider; no state change → no event.

### Open / deferred (for the implementator)
- **`AddScoped` vs `AddSingleton`.** The strategy is stateless and the members are pure, so `AddSingleton` is defensible and cheaper. FINANCE-MICROSERVICES-PLAN §1.3 writes `AddScoped`; the implementator MAY use `AddSingleton` and SHOULD document the choice. **Implemented as `services.AddSingleton<ICountryStrategy, BulgariaStrategy>()`** (Batch 14 — stateless strategy). Either way it is a SINGLE binding, no factory.
- **`AccountSelector` representation.** v1 uses the НСС account **code** string (e.g. `"411"`). SDD-FIN-006 resolves code → `AccountId` against SDD-ACCT-001 at seed/apply time. If a future country needs richer selection (by account type, by tag), the selector type can be widened — coordinate with SDD-FIN-006 §7. v1 = code string.
- **Test project location.** `Finance.Country.BG` has no test project yet. The implementator MAY place the `[Category("SDD-CTRY-001")]` tests in the existing `Finance.Journal.API.Tests` (the consumer) or create a small `Finance.Country.BG.Tests`. Recommendation: a dedicated `Finance.Country.BG.Tests` to keep the abstraction's tests independent of the consumer. **RESOLVED as recommended — `src/Country/Finance.Country.BG.Tests` ships with `BulgariaStrategyTests` (Batch 14/16) and `BulgariaStrategyPostingRuleTemplateTests` (Batch 17); no `[Category("SDD-CTRY-001")]` test lives in a consumer suite (§6).**
- **Exact BG account codes.** The §2.4 codes (411/701/702/4532/601/304/4531/401/503/501) are a starting point. The accountant + SDD-CTRY-BG-001 finalize them; the seeded rules are editable afterward. Flag for accountant sign-off before production (Plan §10 risk #1). **Still open, and widened by Batch 17:** the four new templates (§2.4) reuse `411`/`701`/`4532`/`401`/`503` and inherit the same sign-off obligation. Because `PostingRuleSeeder` never overwrites a seeded key, sign-off SHOULD happen BEFORE `EnablePostingRuleSeeding` is switched on in an environment (§5, SDD-PAY-001 §2.13).
- **Retiring the superseded `CUSTOMER_PAYMENT` template.** Kept alongside `PAYMENT_CUSTOMER_RECEIPT` (§2.4) so no live rule key is renamed. Deactivating it — never deleting — is a future `CHG-DEBT-*` owned with SDD-PAY-001 §7; decide once so the two rules are not both wired.
