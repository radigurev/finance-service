# SDD-FIN-006 — Posting Engine + Posting Rules

> Status: Implemented (Batch 14 — backend shipped + green `[Unit]` tests (engine, rule CRUD, shape validators, seeder, BG strategy) + validated spec↔code↔tests; account resolution is lazy at apply time and the `EnablePostingRuleSeeding` flag is host-startup-gated (§2.2). Deferred: document-triggered auto-posting (SDD-INV-001/SDD-PAY-001), richer account selectors, percentage/fixed split lines, and the `[Category("Integration")]` HTTP/SQL/Redis/RBAC suite (offline). Two capabilities in the EXISTING `Finance.Journal.API`: (A) **Posting Rules** — editable reference data (CRUD over `PostingRule`/`PostingRuleLine`, schema `journal`, INT IDENTITY PKs, filtered/paged, cached, audited), seeded from `ICountryStrategy.GetDefaultPostingRules()` (SDD-CTRY-001) via a feature-flag-gated `PostingRuleSeeder`; and (B) **Posting Engine** — `IPostingEngine.ApplyAsync(ruleKey, amountContext)` resolves an active rule, materializes balanced debit/credit lines by an enum-driven `AmountSource`→amount mapping, and DELEGATES materialization+posting to the EXISTING `IJournalEntryService` (`CreateDraftAsync` → optional `PostAsync`, SDD-FIN-002) — reusing all double-entry/numbering/audit/outbox machinery, reimplementing none. v1 endpoint = generic `POST /api/v1/posting/apply`. Deferred: document-triggered auto-posting (invoice/payment events) to SDD-INV-001/SDD-PAY-001; richer account selectors; percentage/fixed split lines.)
> Owner: Finance
> Last updated: 2026-06-04
> Category: Core
> Service: `Finance.Journal.API` — port **6004**, database `finance_journal` (FINANCE-MICROSERVICES-PLAN §2, service #4 = "Journal Entries + GL + Posting Engine"; FIN-006 lives in the EXISTING Journal service, not a new one)
> Related: SDD-CTRY-001 (Country Strategy — the SOURCE of the default posting-rule templates the seeder loads; the engine uses `ICountryStrategy.GetDefaultPostingRules()` and `BaseCurrencyCode`), SDD-FIN-001 (Double-Entry Engine — the balance invariant the materialized entry MUST satisfy; the engine fails early with `POSTING_RULE_UNBALANCED` before the JE path re-checks it), SDD-FIN-002 (Journal Entry Lifecycle — the engine REUSES `IJournalEntryService.CreateDraftAsync`/`PostAsync`; it does NOT reimplement drafting, numbering, posting, reversal, audit, or the outbox), SDD-FIN-003 (General Ledger — the entries the engine posts roll up here; no change to GL), SDD-ACCT-001 (Chart of Accounts — posting-rule line `AccountSelector` codes resolve to postable `AccountId`s here, via the EXISTING `IReferenceDataReader` seam), SDD-INFRA-004 (Redis cache — posting rules ARE reference data → MAY be cached with invalidation, key `finance-journal:posting-rule:*`; contrast SDD-FIN-003 which forbids caching transactional GL data), SDD-INFRA-005 (filtering/paging — the rules list uses `FilterRequest`→`PagedResult`, PageSize cap 200), SDD-INFRA-007 (validation chain — duplicate-rule-key / has-≥1-line / balanceable cross-aggregate guards), SDD-INFRA-009 (base service/controller, `Result<T>`, `SearchableServiceBase`, `BaseApiController`), SDD-INFRA-001 (correlation, ProblemDetails, decimal arithmetic), SDD-INFRA-006 (outbox — reused via the JE path; the engine itself publishes NO new event), SDD-AUDIT-001 (CoA-style audit on rule mutations; the posted entry's audit is owned by SDD-FIN-002), SDD-INT-AUTH-001 (RBAC — `finance.posting-rule:read|write`, `finance.posting:apply`), SDD-OBS-001 (tracing, structured logging), SDD-INV-001 (Invoice Lifecycle — DEFERRED document-triggered posting consumer), SDD-PAY-001 (Payment Recording — DEFERRED document-triggered posting consumer), SDD-CTRY-BG-001 (fuller BG strategy — refines the seeded BG rules)
> ISA-95: Level 4 (Business Planning & Logistics) — Posting rules = reference/master data; the apply operation produces an L4 bookkeeping business transaction (delegated to SDD-FIN-002)

---

## 1. Context & Scope

The Posting Engine turns a **business event abstraction** (a named rule + a set of monetary amounts) into a **balanced journal entry**, so that callers (and, later, invoice/payment workflows) do not hand-build double-entry lines. It is the bridge between document-level thinking ("a sale invoice for net X + VAT Y") and the bookkeeping engine ("debit Клиенти gross, credit Приходи net, credit ДДС VAT").

This spec defines two capabilities, both in the **existing** `Finance.Journal.API` (FINANCE-MICROSERVICES-PLAN §2 names service #4 "Journal Entries + GL + Posting Engine" — the engine is not a new service):

**(A) Posting Rules — editable reference data.** A `PostingRule` (a named template, e.g. `SALE_INVOICE`) owns an ordered set of `PostingRuleLine`s, each declaring which account to post to, whether it debits or credits, and which amount from the apply-time context it uses. Rules are CRUD-managed reference data: created/edited/deactivated by finance administrators, filtered/paged for listing, cached (they are reference data, unlike transactional journals), and audited on mutation (CoA-style, SDD-AUDIT-001). The initial rule set is **seeded** from `ICountryStrategy.GetDefaultPostingRules()` (SDD-CTRY-001) by a feature-flag-gated `PostingRuleSeeder`, mirroring `Iso4217CurrencySeeder` (SDD-NOM-001 §2.5).

**(B) Posting Engine — the apply operation.** `IPostingEngine.ApplyAsync` takes a `RuleKey` and an **amount context** (named monetary amounts keyed by `PostingAmountSource`, plus currency, entry date, description, and optional account overrides), resolves the active rule, materializes the debit/credit lines by applying each line template's `AmountSource` to the context, and produces a balanced `CreateJournalEntryRequest`. It then **delegates** to the existing `IJournalEntryService.CreateDraftAsync` (and, per a request flag, `PostAsync`) — SDD-FIN-002 — to materialize, number, audit, post, and publish the entry. **The engine reimplements none of that.**

**DRY — the engine REUSES, never reimplements.** The Posting Engine MUST NOT reimplement balancing, gapless numbering, audit writing, outbox publishing, the `Draft→Posted` workflow, or persistence. It produces a `CreateJournalEntryRequest` and calls `IJournalEntryService` (SDD-FIN-002). The double-entry invariants (SDD-FIN-001) are enforced by that path; the engine adds a **defensive early balance check** (§2.4) only so a malformed rule fails with a precise domain code *before* a draft is created, not so it duplicates the engine's own validation. The engine emits **no new domain event** — `JournalEntryPostedEvent` is already published by SDD-FIN-002's posting path.

**No overengineering in the amount mapping.** The `AmountSource` → amount resolution MUST be a **simple enum-driven mapping** (a `switch` or dictionary lookup over `PostingAmountSource`), NOT a class-per-source strategy hierarchy. There are a handful of sources (`Net`, `Tax`, `Gross`, …); a polymorphic resolver hierarchy for them would be the exact over-engineering the project rejects.

**v1 surface is the generic apply + the rule store + the seed.** v1 ships: the `PostingRule`/`PostingRuleLine` CRUD, the `PostingRuleSeeder`, and a generic `POST /api/v1/posting/apply` (apply a named rule to a caller-supplied amount context). **Document-triggered posting** — invoice/payment domain events automatically applying a rule — is DEFERRED to SDD-INV-001 / SDD-PAY-001 (those specs add MassTransit consumers that call `IPostingEngine.ApplyAsync`; the engine surface is unchanged).

**ISA-95 classification.** A `PostingRule` (with its child `PostingRuleLine`s) is ISA-95 **Level 4 (Business Planning & Logistics)** reference/master data (ISA-95 / IEC 62264 Part 1, §5) — its create/update/deactivate are reference-data maintenance operations emitting immutable audit rows (SDD-AUDIT-001) like the Chart of Accounts (SDD-ACCT-001). The **apply** operation produces a Level-4 bookkeeping **business transaction** — but it does so by delegating to SDD-FIN-002, which already emits the immutable `JournalEntryPostedEvent` and audit row for the posted entry. The engine itself creates no new business-transaction record type and therefore emits **no new event** (the event obligation in SDD-INFRA-006 is satisfied by the delegated JE path). No Level-3 (MES) production activity is modelled. Reference data consumed (accounts, currencies, the country strategy) is Level-4 master data.

**Scope — covered (v1):**
- `PostingRule` + `PostingRuleLine` entities (schema `journal`, INT IDENTITY PKs — internal reference data, Plan §8 / Appendix A) with `RuleKey` (unique), `Description`, `CountryCode`, `IsActive`, `RowVersion`; lines ordered, each with `AccountSelector`, `DebitOrCredit`, `AmountSource`.
- CRUD over posting rules: list (`FilterRequest`→`PagedResult`), get-by-id/by-key, create, update, deactivate — via `SearchableServiceBase`/`BaseApiController`/`Result<T>`, validation chain, reference-data caching + invalidation, audit on mutation.
- `PostingRuleSeeder` gated by feature flag `EnablePostingRuleSeeding`, idempotently upserting `ICountryStrategy.GetDefaultPostingRules()` (SDD-CTRY-001) on startup.
- `IPostingEngine.ApplyAsync(ApplyPostingRuleRequest)` — resolve active rule by `RuleKey`, materialize balanced lines via enum-driven `AmountSource` mapping, build `CreateJournalEntryRequest`, delegate to `IJournalEntryService.CreateDraftAsync` (+ optional `PostAsync`), return `Result<JournalEntryDto>`.
- `POST /api/v1/posting/apply` endpoint.
- Defensive early balance check (`POSTING_RULE_UNBALANCED`) before any draft is created.
- New error codes in `Finance.Common/ErrorCodes/PostingErrorCodes.cs`.

**Scope — excluded (DEFERRED):**
- **Document-triggered posting** — invoice/payment events auto-applying rules (MassTransit consumers calling `ApplyAsync`) → SDD-INV-001 / SDD-PAY-001. v1 is the generic apply only.
- **Percentage / fixed-split lines** — a line that posts a *fraction* of an amount, or a fixed amount, or splits across multiple accounts by ratio. v1 lines map one whole `AmountSource` to one account. (`PostingRuleLine` MAY carry optional nullable `Percentage`/`FixedAmount` columns reserved for this, but v1 ignores them — §7.)
- **Account selection by type/tag** — v1 `AccountSelector` is an account **code** string (resolved to `AccountId`); richer selection is future (§7).
- **Rule versioning / effective-dating** — v1 rules are mutable reference data with no temporal versioning; a future CHG MAY add effective-dated rule versions.
- **Tax computation** — the engine does NOT compute Net/Tax/Gross; the caller supplies them in the amount context. Tax calculation is SDD-INV-001 / SDD-CTRY-BG-001 (`ICountryStrategy.ApplyTaxRounding`, deferred per SDD-CTRY-001 §1).
- **FX rate resolution** — the caller supplies the line currency + base amounts (or a single-currency context in base); automatic rate lookup is SDD-FIN-005. v1 SHOULD assume a base-currency context (§2.3).
- **Reversal of rule-derived entries** — reversal is the existing SDD-FIN-002 surface (`POST /api/v1/journal-entries/{id}/reverse`); the engine adds nothing.

## 2. Behavior

> **Service/controller contract (SDD-INFRA-009).** `PostingRuleService` MUST inherit `SearchableServiceBase<PostingRule, PostingRuleDto, JournalDbContext>` (and `BaseEntityService<JournalDbContext>`) and MUST return `Result` / `Result<T>` — never `null`, never a thrown exception for a business outcome. `IPostingEngine` (a focused service, NOT a `SearchableServiceBase`) MUST also return `Result<JournalEntryDto>`. Controllers (`PostingRulesController`, `PostingController`) inherit `BaseApiController` and translate via `ToActionResult(...)`. Error-code→HTTP mapping + the ProblemDetails shape are owned by SDD-INFRA-001 / SDD-INFRA-009. `CancellationToken` MUST be threaded controller → service → JE service → DB.

### 2.1 Posting Rule store — CRUD (MUST)
- `GET /api/v1/posting-rules` MUST accept a `FilterRequest` and return `PagedResult<PostingRuleDto>` (SDD-INFRA-005), default-ordered by `RuleKey` ascending (overriding `BuildBaseQuery`; the library always appends the PK as the final deterministic sort term). `PageSize` capped at 200. Filterable/sortable surface opt-in via `[Filterable]`/`[Sortable]` on `PostingRule`: `RuleKey`, `Description`, `CountryCode`, `IsActive`. Requires `finance.posting-rule:read`.
- `GET /api/v1/posting-rules/{id}` MUST return the rule with its ordered lines, or `POSTING_RULE_NOT_FOUND` (404). Requires `finance.posting-rule:read`. MAY be served from cache (§2.7).
- `POST /api/v1/posting-rules` MUST create a rule + its lines after validation (§3). A duplicate `RuleKey` MUST be rejected with `DUPLICATE_POSTING_RULE_KEY` (validation chain, SDD-INFRA-007). A rule MUST have at least one line (`POSTING_RULE_HAS_NO_LINES`) and MUST be structurally balanceable — ≥ 1 debit AND ≥ 1 credit line (`POSTING_RULE_UNBALANCED`). Create MUST write an audit `Create` row and invalidate the cache (§2.6/§2.7). Requires `finance.posting-rule:write`.
- `PUT /api/v1/posting-rules/{id}` MUST update a rule (description, `IsActive`, lines) under optimistic concurrency via `RowVersion` (stale token → `CONCURRENT_MODIFICATION`, SDD-INFRA-009). The same line/balanceable validations apply. Update MUST write an audit `Update` row and invalidate the cache. Requires `finance.posting-rule:write`. `RuleKey` is **immutable after create**: `UpdatePostingRuleRequest` deliberately omits `RuleKey`, so a `PUT` cannot change it (no key-collision path exists on update). To re-key a rule, deactivate it and create a new one.
- Deactivation MUST be modeled as `PUT` setting `IsActive = false` (CoA-style — SDD-ACCT-001), writing an audit `StateChange` row. An inactive rule MUST be excluded from `ApplyAsync` resolution (§2.3) but MUST remain listable/gettable. There is NO hard delete of a rule that has been used (reference-data immutability of history); whether to allow hard-delete of a never-applied rule is an implementator decision (§7).

### 2.2 Seeding from the country strategy (MUST)
- A `PostingRuleSeeder` (mirroring `Iso4217CurrencySeeder`, SDD-NOM-001 §2.5) MUST idempotently upsert the templates from `ICountryStrategy.GetDefaultPostingRules()` (SDD-CTRY-001) into the `posting_rules` store. The `EnablePostingRuleSeeding` feature flag gate is a **host-startup responsibility**: the composition root (`Program.cs`) invokes the seeder only when the flag is true, keeping `IPostingRuleSeeder` single-responsibility (it seeds; it does not own the flag). The flag-disabled path is therefore a startup-wiring concern verified at the integration layer, not a seeder unit test.
- The seed MUST be idempotent and non-destructive: for each template, if a `PostingRule` with that `RuleKey` does NOT exist, insert it (and its lines); if it ALREADY exists, leave it untouched (never overwrite an administrator's edits — exactly the `ICurrencySeeder` contract). The seeder MUST return the count inserted and MUST log it via structured NLog (SDD-OBS-001).
- Each template line's `AccountSelector` (an НСС account code string, SDD-CTRY-001 §2.2) MUST be stored **as the code** on the persisted `PostingRuleLine` and resolved to a postable `AccountId` **lazily at apply time** via the EXISTING `IReferenceDataReader` seam (SDD-FIN-001 §2.6 / SDD-FIN-003 §2.5 — Refit-through-gateway to SDD-ACCT-001). This is the chosen resolution timing (the §7 alternative): the seeder performs NO account lookup and takes no `IReferenceDataReader` dependency, so seeding never depends on the Accounts service being reachable. An `AccountSelector` that resolves to no postable account surfaces at apply time as `POSTING_RULE_ACCOUNT_NOT_FOUND` (422, §2.3), NOT during seeding.
- The seeder MUST validate each template before insert with the same balanceable / ≥1-line checks as §2.1 (a structurally unbalanceable seed template → logged `POSTING_RULE_UNBALANCED`, skip that rule).

### 2.3 Posting Engine — `ApplyAsync` (MUST)
- `IPostingEngine.ApplyAsync(ApplyPostingRuleRequest request, CancellationToken ct)` MUST:
  1. **Resolve** the active `PostingRule` by `request.RuleKey`. A key that does not exist OR resolves to an `IsActive == false` rule MUST fail with `POSTING_RULE_NOT_FOUND` (no entry created). (Inactive is surfaced as not-found for apply — an inactive rule is not applicable; the distinction is visible via the CRUD `GET`.)
  2. **Materialize** each line: for each `PostingRuleLine`, look up the amount in `request.Amounts` keyed by the line's `AmountSource`. A required source missing from the context (or present but, by §3, not a valid monetary value) MUST fail with `MISSING_POSTING_AMOUNT` (no entry created). The `AmountSource`→amount resolution MUST be a simple enum-driven `switch`/dictionary lookup — NOT a class-per-source hierarchy.
  3. Place the resolved amount on the **debit** side (`DebitAmount`) when the line is `Debit`, or the **credit** side (`CreditAmount`) when `Credit`, building a `JournalEntryLineRequest` per line with the resolved `AccountId` (from `AccountSelector`, §2.2, honoring any `request.AccountOverrides`), the `request.CurrencyCode`, the rate (`1.000000` for a base-currency context — §1 deferred FX), and the base amounts equal to the transactional amounts when the context is in base currency.
  4. **Defensive early balance check (§2.4)** — if Σ debits ≠ Σ credits in base currency, fail with `POSTING_RULE_UNBALANCED` BEFORE any draft is created.
  5. Build a `CreateJournalEntryRequest` (`EntryDate = request.EntryDate`, `Description = request.Description` (or a rule-derived default), `Lines = materialized lines`) and call `IJournalEntryService.CreateDraftAsync(request, baseCurrencyCode, ct)` where `baseCurrencyCode` comes from `ICountryStrategy.BaseCurrencyCode` (SDD-CTRY-001) / configuration — the SAME source SDD-FIN-002 §2.3 uses.
  6. If `request.PostImmediately` is true, call `IJournalEntryService.PostAsync(draft.Id, …, ct)` (SDD-FIN-002 §2.4) to post the draft (gapless number, audit-first→outbox `JournalEntryPostedEvent`, all atomic — owned by FIN-002). If false, leave the entry as a `Draft` for later manual posting.
  7. Return `Result<JournalEntryDto>` carrying the created (draft or posted) entry. The engine MUST NOT swallow a failure from `IJournalEntryService` — a JE-path failure (e.g. `UNBALANCED_ENTRY`, `ACCOUNT_NOT_POSTABLE`, `POSTING_PERIOD_CLOSED`) MUST propagate as the result (the engine's early check catches malformed rules; the JE path remains the authority on the full SDD-FIN-001 surface and period lock SDD-FIN-004).
- The engine MUST NOT reimplement drafting, numbering, posting, audit, or the outbox — it composes `IJournalEntryService`. It MUST NOT publish any new event.
- Requires permission `finance.posting:apply` (§2.8).

### 2.4 Defensive early balance check (MUST)
- After materializing the lines (§2.3 step 2-3) and BEFORE calling `CreateDraftAsync`, the engine MUST compute Σ base debits and Σ base credits and, if they differ to the cent (`DECIMAL(18,2)`), fail with `POSTING_RULE_UNBALANCED` — no draft, no number, no audit row, no event.
- This is **defensive**, not authoritative: the JE path (SDD-FIN-001 §2.3, re-checked at post in SDD-FIN-002 §2.2) is the authority and would also reject an unbalanced entry. The early check exists so a misconfigured rule (e.g. a `SALE_INVOICE` whose lines do not net for the supplied `Net`/`Tax`/`Gross`) fails with a precise, rule-specific domain code rather than a generic `UNBALANCED_ENTRY` after burning effort. It MUST NOT be the only balance check — the engine MUST still let the JE path validate.
- All arithmetic MUST be `decimal` / `DECIMAL(18,2)`, never `double`/`float` (SDD-FIN-005 / SDD-INFRA-001 / CLAUDE.md §0.3).

### 2.5 Amount context (MUST)
- `ApplyPostingRuleRequest` MUST carry: `RuleKey` (string), `Amounts` (a map `PostingAmountSource → decimal`, e.g. `{ Net: 100.00, Tax: 20.00, Gross: 120.00 }`), `CurrencyCode` (ISO 4217), `EntryDate` (`DateTimeOffset`), `Description` (string, optional — defaults to the rule description + a reference), optional `AccountOverrides` (a map `AccountSelector → AccountId`/code allowing the caller to redirect a line's account, e.g. a specific customer's receivable sub-account), and `PostImmediately` (bool; default true).
- The `Amounts` map MUST contain an entry for every `AmountSource` referenced by the resolved rule's lines; a missing one → `MISSING_POSTING_AMOUNT` (§2.3 step 2). Extra amounts not referenced by any line MUST be ignored (forward-compatible).
- The context is currency-agnostic in shape but v1 SHOULD be supplied in the base currency (`CurrencyCode == ICountryStrategy.BaseCurrencyCode`); multi-currency contexts (with rates + base amounts) are SDD-FIN-005. If a non-base currency is supplied without enough rate information to compute base amounts, the resulting lines will fail the JE path's reconciliation (SDD-FIN-001 §2.7) — v1 does not resolve rates.

### 2.6 Audit (MUST — SDD-AUDIT-001)
- Posting-rule **mutations** (create, update, deactivate) MUST write an `audit.OperationsEvents` row in the SAME transaction, CoA-style (SDD-ACCT-001 / SDD-AUDIT-001): `Create` with `BeforeJson = null`, `Update`/`StateChange` with the prior snapshot. Deactivation is a sensitive-enough state change to record but does NOT require a mandatory `Reason` (it is not on SDD-AUDIT-001's mandatory-reason list — period close / reversal / permission revocation are).
- The **apply** operation's audit is owned by SDD-FIN-002: the draft-create writes a `Create` audit row and the post writes a `StateChange`/post row (SDD-FIN-002 §2.3/§2.4). The engine MUST NOT write a second, redundant audit row for the entry — that would double-audit the same operation (the exact mistake fixed in Batch 11.1). The engine MAY (SHOULD) write a lightweight audit/trace noting *which rule* produced the entry, but the entry's lifecycle audit belongs to FIN-002.

### 2.7 Caching (MAY — SDD-INFRA-004)
- Posting rules ARE reference data (a small, slowly-changing, read-heavy set consulted on every apply), so unlike transactional GL data (SDD-FIN-003, which forbids caching) they MAY be cached. The single-rule read (`GET /{id}`, by-key resolution in `ApplyAsync`) MAY be served from cache under key `finance-journal:posting-rule:{key}` (and/or `finance-journal:posting-rule:all` for the list), TTL per SDD-INFRA-004.
- Every rule mutation (create/update/deactivate) and every seed insert MUST invalidate the cache (`finance-journal:posting-rule:*`) so the next apply sees the change. Cache availability MUST NOT gate correctness: if Redis is unreachable, resolution MUST fall through to the DB (SDD-INFRA-004 — service availability never depends on Redis).
- The filtered/paged `GET /posting-rules` list MUST NOT be cached on arbitrary filter combinations (cache only the bounded `all`/`{key}` keys), matching SDD-ACCT-001.

### 2.8 Cross-cutting obligations (MUST)
- `PostingRulesController` actions MUST be protected by `[RequirePermission("finance.posting-rule:read|write")]`; `PostingController.Apply` MUST be protected by `[RequirePermission("finance.posting:apply")]` — all decoded via the shared `Warehouse.Auth.Shared` package (SDD-INT-AUTH-001). (See §7 for the considered alternative of reusing `finance.journal:create` for apply; `finance.posting:apply` is chosen for finer RBAC.)
- `CorrelationId` MUST flow via `ICorrelationIdAccessor`/`CorrelationIdMiddleware`; the delegated `IJournalEntryService` call MUST run under the same correlation scope so the posted entry's event carries the originating correlation id (SDD-INFRA-001).
- The endpoints MUST be traced via OpenTelemetry with the `correlation_id` Activity tag and MUST use NLog structured templates — no string interpolation in log calls (SDD-OBS-001).

### 2.9 Edge cases (MUST)
- **Unbalanced applied rule.** A rule whose materialized lines do not net to zero for the supplied amounts (e.g. `SALE_INVOICE` where `Gross ≠ Net + Tax`) MUST fail with `POSTING_RULE_UNBALANCED` BEFORE any draft is created — no number, no event, no audit row (§2.4).
- **Missing required amount.** Applying a rule whose lines reference an `AmountSource` (e.g. `Tax`) that is absent from `request.Amounts` MUST fail with `MISSING_POSTING_AMOUNT` before any draft is created.
- **Unknown or inactive rule key.** Applying a `RuleKey` that does not exist, or that resolves to an `IsActive == false` rule, MUST fail with `POSTING_RULE_NOT_FOUND` — never create an entry.
- **Duplicate rule key on create.** Creating a `PostingRule` with a `RuleKey` that already exists MUST fail with `DUPLICATE_POSTING_RULE_KEY` (validation chain, SDD-INFRA-007) — no row written.
- **Cache invalidation on rule update.** After a `PUT` changes a rule's lines, the next `ApplyAsync` for that `RuleKey` MUST use the NEW lines (the mutation MUST have invalidated `finance-journal:posting-rule:*`) — a `[Unit]`/`[Integration]` test MUST assert the post-update apply reflects the change, not a stale cache.
- **`PostImmediately = false`.** Applying with `PostImmediately = false` MUST leave the entry as a `Draft` with a NULL `EntryNumber` (SDD-FIN-002 §2.3) — no gapless number consumed, no `JournalEntryPostedEvent`. A later manual `POST /{id}/post` posts it.
- **JE-path failure propagates.** If the delegated `CreateDraftAsync`/`PostAsync` fails (e.g. `ACCOUNT_NOT_POSTABLE` because a selected account is a header account, or `POSTING_PERIOD_CLOSED` post-FIN-004), the engine MUST return that failure as its `Result` — it MUST NOT swallow it or translate it into a misleading posting code.
- **Rule with no lines.** Creating/updating a rule with zero lines MUST fail with `POSTING_RULE_HAS_NO_LINES` (a rule that posts nothing is meaningless).

## 3. Validation Rules

### 3.1 Field-level (FluentValidation — request shape)

| Request | Field | Rule | Error code |
|---|---|---|---|
| Create/Update rule | `RuleKey` | NotEmpty, ≤ 50 chars, uppercase machine key | `INVALID_POSTING_RULE_KEY` |
| Create/Update rule | `Lines` | NotEmpty (≥ 1 line) | `POSTING_RULE_HAS_NO_LINES` |
| Create/Update rule | `Lines[].AccountSelector` | NotEmpty | `INVALID_POSTING_RULE_LINE` |
| Create/Update rule | `Lines[].DebitOrCredit` | Valid enum (`Debit`/`Credit`) | `INVALID_POSTING_RULE_LINE` |
| Create/Update rule | `Lines[].AmountSource` | Valid enum (`Net`/`Tax`/`Gross`/…) | `INVALID_POSTING_RULE_LINE` |
| Apply | `RuleKey` | NotEmpty | `INVALID_POSTING_RULE_KEY` |
| Apply | `Amounts` | NotEmpty; every value finite, non-negative `decimal` | `MISSING_POSTING_AMOUNT` |
| Apply | `CurrencyCode` | NotEmpty, ISO 4217 alpha-3 shape | `INVALID_LINE_CURRENCY` (from SDD-FIN-001, reused) |
| Apply | `EntryDate` | Required | `INVALID_ENTRY_DATE` (from SDD-FIN-001/002, reused) |
| List | `FilterRequest.PageSize` | ≤ 200 (SDD-INFRA-005) | `PAGE_SIZE_TOO_LARGE` (from `FilterErrorCodes`, reused) |

### 3.2 Cross-aggregate / chain guards (SDD-INFRA-007)

| Operation | Guard | Error code |
|---|---|---|
| Create rule | `RuleKey` unique in `posting_rules` | `DUPLICATE_POSTING_RULE_KEY` |
| Create/Update rule | lines include ≥ 1 `Debit` AND ≥ 1 `Credit` (structurally balanceable) | `POSTING_RULE_UNBALANCED` |
| Apply | resolved rule exists AND `IsActive` | `POSTING_RULE_NOT_FOUND` |
| Apply | every line's `AmountSource` present in `Amounts` | `MISSING_POSTING_AMOUNT` |
| Apply | materialized Σ debits == Σ credits (base, to the cent) | `POSTING_RULE_UNBALANCED` |
| Apply (delegated) | full SDD-FIN-001 surface + period lock (SDD-FIN-004) | propagated from `IJournalEntryService` (`UNBALANCED_ENTRY`, `ACCOUNT_NOT_POSTABLE`, `POSTING_PERIOD_CLOSED`, …) |

### 3.3 State-based

| Condition | Rule | Error code |
|---|---|---|
| Rule not found (get/update/apply) | Reject | `POSTING_RULE_NOT_FOUND` |
| Apply against an inactive rule | Reject (not applicable) | `POSTING_RULE_NOT_FOUND` |
| Stale `RowVersion` on rule update | Reject | `CONCURRENT_MODIFICATION` |
| Apply: an `AccountSelector` code resolves to no postable account | Reject (422) | `POSTING_RULE_ACCOUNT_NOT_FOUND` |

## 4. Error Rules

All errors are RFC-7807 ProblemDetails per SDD-INFRA-001: `title` = code (SCREAMING_SNAKE_CASE), `detail` = developer English, `type` = `https://finance.local/errors/{code}`. `BaseApiController.ToActionResult` maps codes to HTTP via the Journal service's `IErrorCodeToStatusMap` (SDD-INFRA-009); services return `Result.Failure(code, detail)`.

| Code | HTTP | Trigger | Type |
|---|---|---|---|
| `POSTING_RULE_NOT_FOUND` | 404 | Rule id/key does not exist, OR apply targets an inactive rule | Not found |
| `DUPLICATE_POSTING_RULE_KEY` | 409 | Create with an already-existing `RuleKey` (RuleKey is immutable on update, §2.1) | Conflict (duplicate) |
| `POSTING_RULE_HAS_NO_LINES` | 400 | Create/update a rule with zero lines (enforced at the request-shape boundary by FluentValidation, §3.1; the service-level chain owns only duplicate-key + balanceable) | Validation |
| `POSTING_RULE_UNBALANCED` | 409 | Rule structurally not balanceable (create/update), OR materialized lines do not net to zero (apply, before the JE path) | Conflict (balance) |
| `MISSING_POSTING_AMOUNT` | 400 | Apply context lacks an amount for an `AmountSource` referenced by the rule | Validation |
| `INVALID_POSTING_RULE_KEY` | 400 | `RuleKey` empty / too long / malformed | Validation (shape) |
| `INVALID_POSTING_RULE_LINE` | 400 | A line has empty `AccountSelector` or an invalid `DebitOrCredit`/`AmountSource` enum | Validation (shape) |
| `POSTING_RULE_ACCOUNT_NOT_FOUND` | 422 | An `AccountSelector` code resolves to no postable account at apply time (resolution is lazy at apply, §2.2) | Unprocessable (reference) |

Constants MUST live in a NEW `Finance.Common/ErrorCodes/PostingErrorCodes.cs`:
`POSTING_RULE_NOT_FOUND`, `DUPLICATE_POSTING_RULE_KEY`, `POSTING_RULE_HAS_NO_LINES`, `POSTING_RULE_UNBALANCED`, `MISSING_POSTING_AMOUNT`, `INVALID_POSTING_RULE_KEY`, `INVALID_POSTING_RULE_LINE`, `POSTING_RULE_ACCOUNT_NOT_FOUND`. Each `.WithErrorCode(...)` call MUST reference one of these constants — never a raw string (CLAUDE.md §0.3). `CONCURRENT_MODIFICATION` is referenced from `CommonErrorCodes` (single source) — NOT redefined. `INVALID_LINE_CURRENCY` / `INVALID_ENTRY_DATE` are referenced from `JournalErrorCodes` (SDD-FIN-001/002, reused). `PAGE_SIZE_TOO_LARGE` is referenced from `FilterErrorCodes` (SDD-INFRA-005, reused).

The `DefaultErrorCodeToStatusMap` (SDD-INFRA-009) maps `*_NOT_FOUND`→404, `*_CONFLICT`/duplicate patterns→409, and 400 by default; the Journal service's `IErrorCodeToStatusMap` MUST be extended so `DUPLICATE_POSTING_RULE_KEY`→409, `POSTING_RULE_UNBALANCED`→409, and `POSTING_RULE_ACCOUNT_NOT_FOUND`→422 (none match the default patterns).

**Frontend obligation (SDD-UI-001).** Every code above MUST get a matching `errors.<CODE>` entry in BOTH `frontend/src/shared/i18n/locales/en.ts` and `bg.ts` in the SAME PR as the posting-rules frontend (the Posting Rules management views — §5). Backend-only this batch; recorded for the frontend phase.

## 5. Versioning Notes

`/api/v1/posting-rules/*` (CRUD) and `/api/v1/posting/apply` are the v1 surface.

- **v1 — Initial specification (Batch 14).** `PostingRule`/`PostingRuleLine` reference-data CRUD (filtered/paged, cached, audited) in `Finance.Journal.API`; `PostingRuleSeeder` (feature flag `EnablePostingRuleSeeding`) idempotently seeding `ICountryStrategy.GetDefaultPostingRules()` (SDD-CTRY-001); `IPostingEngine.ApplyAsync` — resolve active rule by key, materialize balanced lines via enum-driven `AmountSource` mapping, defensive early balance check (`POSTING_RULE_UNBALANCED`), then DELEGATE to `IJournalEntryService.CreateDraftAsync`/`PostAsync` (SDD-FIN-002); generic `POST /api/v1/posting/apply`; `finance.posting-rule:read|write` + `finance.posting:apply`.
- **Deferred (future versions / specs):**
  - **Document-triggered posting** — SDD-INV-001 / SDD-PAY-001 add MassTransit consumers that call `IPostingEngine.ApplyAsync` from invoice/payment events. This is additive (new consumers) — the engine surface is unchanged, no version bump.
  - **Percentage / fixed-split lines** — reserved nullable `Percentage`/`FixedAmount` columns on `PostingRuleLine` are inert in v1; activating them is an additive enhancement (new `AmountSource` semantics) — a `CHG-ENH-*`.
  - **Richer account selectors** (by type/tag) — additive change to `AccountSelector` resolution; coordinate with SDD-CTRY-001 §7.
  - **Rule effective-dating / versioning** — a future CHG; v1 rules are mutable with no temporal versioning.
- Adding a response field or a new `AmountSource` enum value is additive (non-breaking). Changing the apply semantics (e.g. the engine taking over numbering/posting instead of delegating, or computing tax) is breaking and requires `/api/v2/` + a `CHG-ENH-*`.

## 6. Test Plan

> Environment: Docker/SQL/Redis/RabbitMQ are not available — only `[Unit]` tests run by default. EF unit tests use SQLite in-memory; rule CRUD, the seeder (against a faked `ICountryStrategy` + faked `IReferenceDataReader`), the amount-source mapping, and the early balance check are testable without a real broker. The delegation to `IJournalEntryService` is asserted against a **faked/mocked `IJournalEntryService`** in unit tests (the engine's job is to build the right `CreateJournalEntryRequest` and call the service — the JE path's own behavior is covered by SDD-FIN-002's suite). `WebApplicationFactory` HTTP tests, real-SQL CRUD/cache tests, and the real end-to-end apply→post→outbox path carry `[Category("Integration")]` and are excluded from the default run. All business tests MUST reference `[Category("SDD-FIN-006")]`.

### 6.1 Posting Engine — apply (Unit — mocked `IJournalEntryService`)

| Test name | Kind |
|---|---|
| `Apply_ValidRule_BuildsBalancedCreateRequest_CallsCreateDraft` | [Unit] |
| `Apply_PostImmediatelyTrue_CallsPostAsync_AfterCreateDraft` | [Unit] |
| `Apply_PostImmediatelyFalse_LeavesEntryAsDraft_NoPostCall` | [Unit] |
| `Apply_DebitLine_PlacesAmountOnDebitSide_CreditLineOnCreditSide` | [Unit] |
| `Apply_AmountSourceMapping_IsEnumDriven_NotClassPerSource` | [Unit] |
| `Apply_UnbalancedMaterializedLines_ReturnsPostingRuleUnbalanced_BeforeCreateDraft` | [Unit] |
| `Apply_MissingRequiredAmount_ReturnsMissingPostingAmount_BeforeCreateDraft` | [Unit] |
| `Apply_UnknownRuleKey_ReturnsPostingRuleNotFound_NoCreateDraft` | [Unit] |
| `Apply_InactiveRule_ReturnsPostingRuleNotFound` | [Unit] |
| `Apply_AccountOverride_RedirectsLineToOverriddenAccount` | [Unit] |
| `Apply_BaseCurrencyFromCountryStrategy_PassedToCreateDraft` | [Unit] |
| `Apply_JournalServiceFailure_PropagatesAsResult_NotSwallowed` | [Unit] |
| `Apply_EmitsNoNewEvent_ReliesOnJournalPostedEvent` | [Unit] |
| `Apply_DoesNotDoubleAudit_EntryAuditOwnedByFin002` | [Unit] |

### 6.2 Posting Rule CRUD & validation (Unit — SQLite in-memory)

| Test name | Kind |
|---|---|
| `CreateRule_Valid_PersistsWithLines_WritesAuditCreate` | [Unit] |
| `CreateRule_DuplicateRuleKey_ReturnsDuplicatePostingRuleKey` | [Unit] |
| `CreateRule_NoLines_ReturnsPostingRuleHasNoLines` | [Unit] |
| `CreateRule_AllDebitLines_ReturnsPostingRuleUnbalanced` | [Unit] |
| `CreateRule_AllCreditLines_ReturnsPostingRuleUnbalanced` | [Unit] |
| `UpdateRule_StaleRowVersion_ReturnsConcurrentModification` | [Unit] |
| `UpdateRule_Deactivate_WritesAuditStateChange_ExcludedFromApply` | [Unit] |
| `GetRule_NotFound_ReturnsPostingRuleNotFound` | [Unit] |
| `Search_ReturnsPagedResult_OrderedByRuleKeyAscending` | [Unit] |
| `Search_RespectsPageSizeCap_200` | [Unit] |
| `UpdateRule_InvalidatesCache_NextApplyUsesNewLines` | [Unit] |
| `PostingErrorCodes_DefinesAllEightCodes` | [Unit] |

### 6.3 Seeder (Unit — faked `ICountryStrategy` + faked `IReferenceDataReader`)

| Test name | Kind |
|---|---|
| `Seeder_FlagEnabled_InsertsTemplatesFromCountryStrategy` | [Unit] |
| `Seeder_Idempotent_ExistingRuleKeyNotOverwritten` | [Unit] |
| `Seeder_RunTwice_DoesNotDuplicateRules` | [Unit] |
| `Seeder_EmptyStrategyRules_NoOp` | [Unit] |
| `Seeder_UnbalanceableTemplate_SkipsRule_LogsUnbalanced` | [Unit] |

> The seeder does NOT resolve account codes (resolution is at apply time, §2.2) and does NOT own the `EnablePostingRuleSeeding` flag (host-startup gate, §2.2) — so the former `Seeder_FlagDisabled_DoesNothing` / `Seeder_ResolvesAccountSelectorToAccountId…` / `Seeder_AccountCodeUnresolved…` names are intentionally absent. Apply-time account-not-found is covered by `Apply_AccountCodeUnresolved_ReturnsPostingRuleAccountNotFound` (§6.2); the flag gate is an integration-wiring concern (§6.4).

### 6.4 Endpoint & wiring (Integration — `[Category("Integration")]`, excluded from default run)

| Test name | Kind |
|---|---|
| `Apply_Returns200_AndPostsEntry_OverRealSql_WithOutboxAndAudit` | [Integration] |
| `Apply_Returns409_WhenRuleUnbalancedForContext` | [Integration] |
| `Apply_Returns404_WhenRuleKeyUnknown` | [Integration] |
| `CreateRule_Returns201_AndPersists` | [Integration] |
| `CreateRule_Returns409_WhenDuplicateKey` | [Integration] |
| `Seeder_OverRealSql_InsertsBgDefaults_Idempotent` | [Integration] |
| `PostingRules_AreCached_InvalidatedOnWrite_OverRealRedis` | [Integration] |
| `Apply_Endpoint_Returns403_WhenPostingApplyPermissionMissing` | [Integration] |
| `PostingRules_Endpoint_Returns403_WhenWritePermissionMissing` | [Integration] |

## 7. Resolved Decisions & Open Items

### Resolved
- **Lives in the existing Journal service.** Posting Rules + Posting Engine are part of `Finance.Journal.API` (port 6004, db `finance_journal`) — FINANCE-MICROSERVICES-PLAN §2 service #4. No new service/database.
- **Engine REUSES `IJournalEntryService`.** `ApplyAsync` builds a `CreateJournalEntryRequest` and delegates to `CreateDraftAsync` (+ optional `PostAsync`) — SDD-FIN-002. It reimplements no balancing/numbering/audit/outbox/posting and emits no new event. The double-entry invariants are enforced by that path (SDD-FIN-001); the engine adds only a defensive early `POSTING_RULE_UNBALANCED` check.
- **Enum-driven amount mapping.** `AmountSource`→amount is a `switch`/dictionary over `PostingAmountSource` — NOT a class-per-source hierarchy (anti-overengineering).
- **Rules are reference data → cacheable.** Unlike transactional GL data (SDD-FIN-003, no cache), posting rules MAY be cached (`finance-journal:posting-rule:*`) with invalidation on every write; Redis-down falls through to DB (SDD-INFRA-004).
- **Seeder mirrors `Iso4217CurrencySeeder`.** Idempotent non-destructive upsert by `RuleKey`, never overwrites administrator edits. The `EnablePostingRuleSeeding` flag is gated at host startup (§2.2), not inside the seeder. The seeder does NOT resolve account codes (resolution is lazy at apply, §2.2), so seeding does not depend on the Accounts service.
- **Entities.** `PostingRule` (INT IDENTITY, `RuleKey` unique, `Description`, `CountryCode`, `IsActive`, `RowVersion`) + `PostingRuleLine` (INT IDENTITY, ordered, `AccountSelector`, `DebitOrCredit`, `AmountSource`, reserved nullable `Percentage`/`FixedAmount`), schema `journal` — internal reference data, INT PKs per Plan §8/Appendix A.
- **Permissions.** `finance.posting-rule:read|write` (CRUD); `finance.posting:apply` (engine). The alternative — reusing `finance.journal:create` for apply — was considered and rejected for coarse RBAC; `finance.posting:apply` allows segregating "may run a posting rule" from "may hand-build a journal entry".
- **Audit not doubled.** Rule mutations are audited here (CoA-style); the posted entry's audit is owned by SDD-FIN-002 — the engine does NOT write a second entry audit row (the Batch-11.1 double-audit mistake is explicitly avoided).
- **No new event.** `JournalEntryPostedEvent` from SDD-FIN-002 covers the posted entry; the engine emits nothing new.
- **Document-triggered posting deferred.** v1 is the generic apply only; invoice/payment auto-posting consumers are SDD-INV-001/SDD-PAY-001 (additive).

### Open / deferred (for the implementator)
- **Account resolution timing.** `AccountSelector` (an НСС code, SDD-CTRY-001 §2.2) must become an `AccountId`. The implementator MUST choose: resolve at SEED time (store `AccountId` on the line — fails fast if a code is missing, but couples the seed to the CoA being present) OR resolve at APPLY time (store the code on the line — more resilient seed, resolves lazily). The spec leans toward storing the **code** and resolving at apply (matches the resilient `IReferenceDataReader` seam SDD-FIN-003 already uses, and a code missing at seed time shouldn't block startup). Decide once; reflect in `PostingRuleLine` (`AccountSelector` code vs `AccountId`) and in `POSTING_RULE_ACCOUNT_NOT_FOUND` handling (seed = skip+log, apply = 422).
- **`AmountSource` value set.** v1 needs `Net`, `Tax`, `Gross` for the BG sample rules (SDD-CTRY-001 §2.4). Confirm with the accountant whether more sources (e.g. `Discount`, `Rounding`, `WithholdingTax`) are needed before invoice work (SDD-INV-001); adding enum values later is additive.
- **`AddSingleton` vs `AddScoped` for `IPostingEngine`.** The engine is stateless (it depends on scoped `IJournalEntryService` + `DbContext`), so it MUST be `AddScoped` to match the JE service's lifetime. (`ICountryStrategy` lifetime is decided in SDD-CTRY-001 §7.)
- **Hard-delete of an unused rule.** The spec models deactivation (`IsActive = false`) as the standard retire path (CoA-style). Whether to allow a hard `DELETE` of a rule that has NEVER been applied is an implementator/stakeholder decision; default = no hard delete (deactivate only), preserving reference-data history.
- **Default `Description` for derived entries.** When `request.Description` is empty, the engine derives one (e.g. `"{RuleKey}: {reference}"`). Confirm the exact format with the reporting stakeholder; it is cosmetic and non-breaking to change.
- **Frontend (Phase 6).** Posting Rules management list (`PagedResult` + `FilterRequest`, ledger theme) + create/edit organism (rule key, description, active toggle, ordered line editor with account picker via `useNomenclature()`/account lookup, debit/credit + amount-source selectors) + an apply **preview** (enter a sample amount context, show the materialized balanced lines before posting). Add `errors.<CODE>` (EN+BG) for all eight `PostingErrorCodes` per SDD-UI-001.
