# SDD-CTRY-001 — Country Strategy Interface (minimal v1)

> Status: Implemented (Batch 14 — shipped + 15 green `[Unit]` tests + validated. The lean `ICountryStrategy` seam + a single `BulgariaStrategy` binding. v1 exposed ONLY what SDD-FIN-006 needed: country identity (`CountryCode`, `BaseCurrencyCode`) + `GetDefaultPostingRules()`. The interface is GROWN per spec, never speculatively widened — Batch 16 (SDD-INV-001 / SDD-INT-WH-001) added FOUR shipped members (`ApplyTaxRounding`, `IsValidTaxRate`, `GenerateDocumentNumber`, `StandardTaxRate`), implemented in `BulgariaStrategy`, so the interface is now SEVEN members (see §5 growth log). The remaining country responsibilities the plan lists — statement layouts, regulatory exports, CoA seed, exchange-rate provider, counterparty validation — stay DEFERRED to their owning specs. No country factory/resolver/registry: a single static `AddScoped<ICountryStrategy, BulgariaStrategy>()` binding (FINANCE-MICROSERVICES-PLAN §1.3). Multi-tenant country resolution is DEFERRED to a future `CHG-FEAT-*`.)
> Owner: Finance
> Last updated: 2026-06-10
> Category: Domain
> Projects: `Finance.Country.Abstractions` (the `ICountryStrategy` contract + posting-rule template DTOs) and `Finance.Country.BG` (`BulgariaStrategy`) — shared libraries (FINANCE-MICROSERVICES-PLAN §2.1). Consumed first by `Finance.Journal.API` (port **6004**).
> Related: SDD-FIN-006 (Posting Engine + Posting Rules — the FIRST and ONLY v1 consumer; `GetDefaultPostingRules()` exists for FIN-006's `PostingRuleSeeder`), SDD-ACCT-001 (Chart of Accounts — posting-rule line account selectors reference account codes; the BG defaults use НСС account codes; the CoA seed itself is DEFERRED here), SDD-FIN-001 (Double-Entry Engine — a returned template MUST be balanceable so the entry it derives satisfies the balance invariant), SDD-FIN-005 (Multi-Currency Engine — owns the deferred exchange-rate-provider member; `BaseCurrencyCode` here is the single base-currency datum FIN-006 needs), SDD-INFRA-003 (Sequence Generation — owns the deferred `GenerateDocumentNumber` member), SDD-INT-NAP-001 (НАП export — owns the deferred regulatory-export members), SDD-CTRY-BG-001 (Bulgaria Strategy — the FULLER BG strategy: tax system, rounding, CoA seed JSON, statement layouts, counterparty validation; this spec is only the minimal seam those grow onto), SDD-RPT-001/-002/-003 (Reporting — own the deferred statement-layout members)
> ISA-95: Level 4 (Business Planning & Logistics) — reference / master-data provider

---

## 1. Context & Scope

`ICountryStrategy` is the seam that keeps the Finance core **country-agnostic** (FINANCE-MICROSERVICES-PLAN §1). Anything country-specific — chart of accounts, tax, document numbering, statement layouts, regulatory exports, posting-rule seeds, rounding, counterparty validation, the base currency and its rate provider — is meant to live behind this interface so the universal engine never branches on a country code.

This spec defines the **minimal v1** of that seam — deliberately lean. The first and only consumer this batch is **SDD-FIN-006** (the Posting Engine), which needs exactly two things from a country: (1) the country's identity and base currency, and (2) a set of **default posting-rule templates** to seed its rule store. Therefore v1 of `ICountryStrategy` exposes ONLY those members. Every other responsibility the plan attributes to a country strategy is named here as a **DEFERRED member owned by another spec**, so the interface is grown one member at a time as each consuming spec lands — never widened speculatively into a fat god-interface (SOLID / Interface Segregation, CLAUDE.md guardrails).

**Pattern = Strategy, registered by plain DI — no factory.** There is exactly ONE implementation in v1 (`BulgariaStrategy`) bound by a single `services.AddScoped<ICountryStrategy, BulgariaStrategy>()` (FINANCE-MICROSERVICES-PLAN §1.3). This spec **explicitly forbids** introducing a country factory, resolver, registry, or `ICountryStrategyProvider` in v1: a factory that always returns one static strategy is the precise over-engineering anti-pattern the project rejects. Multi-tenant country resolution (choosing a strategy per request/tenant) is a real future need but is **DEFERRED to a future `CHG-FEAT-*`** (FINANCE-MICROSERVICES-PLAN §1.3); when it lands it adds a resolver around the *same* interface without changing consumers that depend on the injected `ICountryStrategy`.

**Read-only at runtime — no events, no audit, no DB.** `ICountryStrategy` is a **seed/reference source**, not a stateful aggregate. Its members are pure, deterministic, in-memory reads computed from compiled-in country knowledge (and, later, bundled resource files). It owns no table, publishes no event, writes no audit row, and runs no workflow. Persisted, editable country data (e.g. the posting rules once seeded, the chart of accounts once seeded) is owned by the consuming service's own tables and specs (SDD-FIN-006 owns `posting_rules`; SDD-ACCT-001 owns accounts). The strategy is the *source* of the initial seed, not the system of record.

**ISA-95 classification.** `ICountryStrategy` is an ISA-95 **Level 4 (Business Planning & Logistics)** reference / master-data provider (ISA-95 / IEC 62264 Part 1, §5 — Business Planning & Logistics). It supplies Level-4 master data (base currency, default posting-rule templates) that configure the Level-4 bookkeeping engine. It performs no state change, so it requires **no immutable domain event** (the event obligation in SDD-INFRA-006 applies to state-changing operations — none occur here). No Level-3 (MES) production activity is modelled.

**Scope — covered (v1):**
- The `ICountryStrategy` interface with exactly three members: `CountryCode`, `BaseCurrencyCode`, `GetDefaultPostingRules()`.
- The posting-rule template contract DTOs returned by `GetDefaultPostingRules()` — `PostingRuleTemplate` + `PostingRuleLineTemplate` (+ the `PostingDebitOrCredit` / `PostingAmountSource` enums they reference) — defined in `Finance.Country.Abstractions` so both the strategy (producer) and SDD-FIN-006's seeder (consumer) share one shape.
- `BulgariaStrategy` in `Finance.Country.BG`: `CountryCode = "BG"`, `BaseCurrencyCode = "BGN"`, and a small handful of НСС-flavoured default posting-rule templates (sample/seed data — see §2.4).
- Registration as a single `AddScoped<ICountryStrategy, BulgariaStrategy>()` in the consuming service (SDD-FIN-006's `Finance.Journal.API`).

**Scope — excluded (DEFERRED — grow the interface per the owning spec):**
- **Tax calculation & rounding** (`ApplyTaxRounding`, tax-rate lookup) — DEFERRED to SDD-CTRY-BG-001 / SDD-INV-001 (invoice tax) and SDD-FIN-005 (decimal rounding policy). CLAUDE.md §0.3 names `ICountryStrategy.ApplyTaxRounding`; it is added when invoice/tax work lands, not now.
- **Document-number formatting** (`GenerateDocumentNumber`) — DEFERRED to SDD-INFRA-003 (the gapless sequence generator already exists; the country-specific *format* member is added when a country needs a non-default format). CLAUDE.md §0.3 names it as future.
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
- The interface MUST NOT declare any other member in v1. Each deferred responsibility (§1) is added ONLY by the spec that owns it, with that spec recording the interface growth in its Versioning Notes. This is an explicit Interface-Segregation decision, not an oversight. (Growth has since occurred per this rule: Batch 16 / SDD-INV-001 added the tax + document-number members — `ApplyTaxRounding`, `IsValidTaxRate`, `GenerateDocumentNumber`, `StandardTaxRate` — see the §5 growth log.)
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
- These templates are **explicitly sample/seed data that an accountant MUST validate** before production use (FINANCE-MICROSERVICES-PLAN §10 risk #1 — "posting rules need accountant sign-off"). The spec MUST NOT claim regulatory correctness; the exact account codes are a starting point that SDD-CTRY-BG-001 (the fuller BG strategy) and the accountant refine. The seeded rules are editable in the rule store afterward (SDD-FIN-006 §2.1).
- The implementation MUST be stateless and pattern = **Strategy** (one implementation, plain DI). No reflection-based discovery, no plugin loader in v1.

### 2.5 Registration (MUST — no factory)
- The consuming service (SDD-FIN-006's `Finance.Journal.API`) MUST register the strategy with a single binding: `services.AddScoped<ICountryStrategy, BulgariaStrategy>()` (or `AddSingleton`, since the strategy is stateless — the implementator decides; §7). 
- A country factory, resolver, registry, or `ICountryStrategyProvider` MUST NOT be introduced in v1. Consumers MUST depend on the injected `ICountryStrategy` directly. (When multi-tenant resolution lands as a future CHG, it wraps the same interface — consumers are untouched.)

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

### 3.2 Cross-field / structural

| Rule | Mechanism | Surfaced as |
|---|---|---|
| A template is structurally balanceable (has both sides) | structural check in the producing strategy; numeric check deferred to consumer | SDD-FIN-006 `POSTING_RULE_UNBALANCED` at seed/apply time |
| Seed `RuleKey`s are unique | producer responsibility | SDD-FIN-006 seeder upserts by `RuleKey`; a duplicate seed source is a defect |

### 3.3 State-based

`ICountryStrategy` is stateless and read-only — there are no state-based rules. (The persisted posting rules' state is owned by SDD-FIN-006.)

## 4. Error Rules

`ICountryStrategy` v1 has **no runtime error surface**: it is a pure in-memory provider with no HTTP endpoint, no I/O, and no failure modes that produce a ProblemDetails. Contract-invariant violations (§3) are **defects** caught by `[Unit]` tests at build time, not runtime errors.

The errors that *relate* to country data — `POSTING_RULE_UNBALANCED`, `DUPLICATE_POSTING_RULE_KEY`, `POSTING_RULE_NOT_FOUND`, `MISSING_POSTING_AMOUNT` — are raised by the **consumer** (SDD-FIN-006) when it seeds/validates/applies the templates, and are owned by `Finance.Common/ErrorCodes/PostingErrorCodes.cs` (defined in SDD-FIN-006 §4). This spec introduces **no new error codes**.

(For completeness: if a future deferred member becomes async/I/O-bound — e.g. an exchange-rate provider under SDD-FIN-005 — that member's spec defines its own error surface. v1 has none.)

## 5. Versioning Notes

`Finance.Country.Abstractions` (`ICountryStrategy` + template DTOs) and `Finance.Country.BG` (`BulgariaStrategy`) are the v1 shared libraries.

- **v1 — Initial specification (Batch 14).** Lean three-member `ICountryStrategy` (`CountryCode`, `BaseCurrencyCode`, `GetDefaultPostingRules()`); `PostingRuleTemplate`/`PostingRuleLineTemplate` + `PostingDebitOrCredit`/`PostingAmountSource` enums in `Finance.Country.Abstractions`; `BulgariaStrategy` (`BG`/`BGN` + a handful of НСС sample templates) in `Finance.Country.BG`; single `AddScoped<ICountryStrategy, BulgariaStrategy>()` binding; **no factory/resolver/registry**.
- **Interface growth is additive and per-spec** — adding a NEW member to `ICountryStrategy` is a **breaking change to the interface** (every implementation must implement it), so each deferred member is introduced by the spec that needs it, in lock-step with at least one implementation (`BulgariaStrategy`) and a default for any future country. Growth log / order:
  - **Batch 16 (SDD-INV-001 / SDD-INT-WH-001) — GROWN (shipped).** `ICountryStrategy` grew from the three v1 members by FOUR shipped members, implemented in `BulgariaStrategy`:
    - `decimal ApplyTaxRounding(decimal amount)` — rounds a monetary amount per the country rounding policy (`MidpointRounding.AwayFromZero` to 2 dp for BG).
    - `bool IsValidTaxRate(decimal rate)` — whether a tax rate is legal for the country (BG recognizes 20% / 9% / 0%).
    - `string GenerateDocumentNumber(InvoiceDocumentType documentType, long sequenceValue)` — the country-specific document-number FORMAT, fed the raw gapless counter from `ISequenceGenerator.NextValueAsync` (SDD-INFRA-003); BG prefixes ФПок (purchase) / ФПр (sale) / КИ (credit note) / ДИ (debit note).
    - `decimal StandardTaxRate { get; }` — the country's standard rate (BG = 20%).
    The base currency stays `BGN`. SDD-INV-001 §5 is the owning record for the tax + document-number members; SDD-INFRA-003 records the matching `NextValueAsync` member. The interface is now SEVEN members (the three v1 members + these four).
  - Anticipated further growth:
    - **CoA seed** (`GetDefaultChartOfAccounts()`) — SDD-CTRY-BG-001 / SDD-ACCT-001 Phase 2.
    - **Statement layouts** — SDD-RPT-001/-002/-003.
    - **Regulatory export definitions** — SDD-INT-NAP-001.
    - **Exchange-rate provider** — SDD-FIN-005 / SDD-INT-BNB-001.
    - **Counterparty / legal-metadata validation** — SDD-CTRY-BG-001.
- **Multi-tenant resolution** (a factory/resolver around the interface) is a future `CHG-FEAT-*`; it is additive to consumers (they still inject `ICountryStrategy`) and does NOT change this contract.
- Changing the SHAPE of an existing member (e.g. `GetDefaultPostingRules` return type) is breaking and requires a coordinated bump across `Finance.Country.Abstractions`, every implementation, and SDD-FIN-006.

## 6. Test Plan

> Environment: `ICountryStrategy` v1 is pure in-memory with no DB, broker, or HTTP — every test is a fast `[Unit]` test with no infrastructure. There are no `[Integration]` tests for this spec (the seeding/apply integration that *uses* these templates is owned and tested by SDD-FIN-006). All tests MUST carry `[Category("SDD-CTRY-001")]`. Tests live alongside the consumer's suite (`Finance.Journal.API.Tests`, since `Finance.Country.BG` has no test project of its own) or in a small `Finance.Country.BG.Tests` project — implementator decides (§7).

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
| `BulgariaStrategy_SaleInvoiceTemplate_DebitsReceivableCreditsRevenueAndVat` | [Unit] |
| `BulgariaStrategy_PurchaseInvoiceTemplate_DebitsExpenseAndInputVatCreditsPayable` | [Unit] |
| `BulgariaStrategy_CustomerPaymentTemplate_DebitsCashCreditsReceivable` | [Unit] |

### 6.3 Contract & registration (Unit)

| Test name | Kind |
|---|---|
| `PostingRuleTemplate_AndLineTemplate_AreImmutableRecords_NoBehavior` | [Unit] |
| `PostingAmountSource_DefinesNetTaxGross` | [Unit] |
| `CountryStrategy_RegisteredAsSingleScopedBinding_NoFactory` | [Unit] |

## 7. Resolved Decisions & Open Items

### Resolved
- **Lean three-member interface.** `ICountryStrategy` v1 = `CountryCode`, `BaseCurrencyCode`, `GetDefaultPostingRules()` — only what SDD-FIN-006 needs. Every other country responsibility is DEFERRED to its owning spec; the interface grows per spec (§5). This is a deliberate Interface-Segregation / anti-overengineering decision.
- **No factory.** A single `AddScoped<ICountryStrategy, BulgariaStrategy>()` binding; no factory/resolver/registry in v1 (FINANCE-MICROSERVICES-PLAN §1.3). Multi-tenant resolution is a deferred CHG.
- **Strategy pattern, plain DI.** One implementation (`BulgariaStrategy`), stateless, injected by interface. No reflection/plugin discovery.
- **Two projects.** `Finance.Country.Abstractions` (interface + template DTOs + enums — the shared contract) and `Finance.Country.BG` (`BulgariaStrategy`). The template DTOs live in Abstractions so producer and consumer (SDD-FIN-006) share one shape (DRY).
- **BG templates are sample/seed data.** A handful of НСС-flavoured templates needing accountant validation (Plan §10 risk #1); not claimed regulatorily correct; editable after seeding in SDD-FIN-006's rule store.
- **Read-only, no events/audit/DB.** A seed source, not a system of record. ISA-95 L4 master-data provider; no state change → no event.

### Open / deferred (for the implementator)
- **`AddScoped` vs `AddSingleton`.** The strategy is stateless and the members are pure, so `AddSingleton` is defensible and cheaper. FINANCE-MICROSERVICES-PLAN §1.3 writes `AddScoped`; the implementator MAY use `AddSingleton` and SHOULD document the choice. **Implemented as `services.AddSingleton<ICountryStrategy, BulgariaStrategy>()`** (Batch 14 — stateless strategy). Either way it is a SINGLE binding, no factory.
- **`AccountSelector` representation.** v1 uses the НСС account **code** string (e.g. `"411"`). SDD-FIN-006 resolves code → `AccountId` against SDD-ACCT-001 at seed/apply time. If a future country needs richer selection (by account type, by tag), the selector type can be widened — coordinate with SDD-FIN-006 §7. v1 = code string.
- **Test project location.** `Finance.Country.BG` has no test project yet. The implementator MAY place the `[Category("SDD-CTRY-001")]` tests in the existing `Finance.Journal.API.Tests` (the consumer) or create a small `Finance.Country.BG.Tests`. Recommendation: a dedicated `Finance.Country.BG.Tests` to keep the abstraction's tests independent of the consumer.
- **Exact BG account codes.** The §2.4 codes (411/701/702/4532/601/304/4531/401/503/501) are a starting point. The accountant + SDD-CTRY-BG-001 finalize them; the seeded rules are editable afterward. Flag for accountant sign-off before production (Plan §10 risk #1).
