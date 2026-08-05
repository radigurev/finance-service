# CHG-FIX-006 — `IdempotencyFilter<T>` swallows every failed consume: retry and dead-letter recovery never ran

> Created: 2026-08-05
> Author: adversarial spec review (Payments batch — round 3, finding W1)
> Status: Implemented
> Related specs: SDD-INFRA-006 (Resilient Message Publisher — authoritative, §1/§2.3/§2.4/§2.5/§4/§6), SDD-INFRA-004 (Redis Distributed Cache — owns the `IConnectionMultiplexer` the filter claims keys on), SDD-INT-WH-001 (the four Warehouse inbound consumers), SDD-INV-001 (`InvoiceConfirmedEventConsumer` posting handshake + `InvoicePostedEventConsumer` back-event), SDD-EVTLOG-001 (the six EventLog consumers), SDD-AUDIT-001 (a lost `EventLog` row is a lost audit row), SDD-OBS-001 (the DLQ-depth alert never fires), SDD-PAY-001 / SDD-PAY-002 (Drafted — their retry-then-dead-letter recovery wording depends on this fix)
> Originating ticket: adversarial review of the SDD-PAY-001/-002/-003 batch — round-3 finding W1 (BLOCKER, in shipped infrastructure)

---

## 1. Summary

`IdempotencyFilter<T>.Send` claimed the Redis key `finance:processed:{MessageId}` with a 7-day TTL **before** forwarding the message down the consume pipe, and had **no `try`/`catch`** around that forward. Because `bus.UseMessageRetry(...)` is registered **before** `bus.UseFinanceIdempotency(...)`, retry is the **outer** filter and idempotency the **inner** one. So when a consumer threw, the outer retry filter re-sent the *same* `ConsumeContext` with the *same* `MessageId`, the inner idempotency filter found **its own claim from the failed attempt**, logged `DUPLICATE_MESSAGE_SKIPPED`, and returned **without calling `next`**. The retry filter saw a completed, non-faulted task, treated attempt 2 as a success, and MassTransit **ACKed and discarded the message**.

Net effect: **no Finance message could ever reach `<queue>_error`.** The retry ladder never got past its first interval, the DLQ-depth alert could never fire, and every "retries then dead-letters, an operator replays it" recovery contract in the platform was fiction — for **every** shipped consumer.

The fix ships in this batch: a failed consume now releases the claim and rethrows with the exception identity intact. **This restores behavior SDD-INFRA-006 already mandated** (§2.3, §2.4, §6) — see §7. No system-spec behavior change is required.

## 2. Evidence — verified against shipped code

### 2.1 The defect (pre-fix shape)

| # | File : line | What the code did |
|---|---|---|
| 1 | `docs/infrastructure/SDD-INFRA-006-resilient-message-publisher.md:99-110` (§2.5) | The spec's own reference implementation is the **pre-fix shape verbatim**, and is the origin of the defect: `StringSetAsync(key, "1", TimeSpan.FromDays(7), When.NotExists)` → `if (!isNew) { LogWarning; return; }` → a bare `await next.Send(context);` on line 109 with **no `try`/`catch` and no release path**. The shipped filter was a faithful, XML-documented implementation of exactly this. **This was not implementation drift — the illustrative snippet itself contained the hole.** |
| 2 | `src/Infrastructure/Messaging/Finance.Infrastructure.Messaging/Filters/IdempotencyFilter.cs:67-69` | The claim is taken **before** the forward: `await database.StringSetAsync(key, "1", ProcessedTtl, When.NotExists)`. `ProcessedTtl = TimeSpan.FromDays(7)` (`:25`). The key is `$"finance:processed:{messageId}"` (`:65`). |
| 3 | `…/Filters/IdempotencyFilter.cs:71-79` | The duplicate branch logs `MessagingErrorCodes.DUPLICATE_MESSAGE_SKIPPED` (`:77`) and `return`s — **it never calls `next`, and it never faults**. Pre-fix this branch could not distinguish a genuine replay from the filter's own abandoned claim. |
| 4 | `…/Filters/IdempotencyFilter.cs:83` | The forward, pre-fix, was `await next.Send(context).ConfigureAwait(false);` **unguarded** — the region now occupied by the `try`/`catch` at `:81-89`. On a thrown consumer exception the claim stayed live for the full 7-day TTL. |
| 5 | `…/Filters/IdempotencyFilter.cs:125-128` (`ResolveMessageId`) | Falls back to `NewId.NextGuid()` when the transport supplies no `MessageId`. That fallback would have masked the defect (each attempt claiming a fresh key) — but every Finance event carries a `required Guid MessageId` per `IFinanceEvent` (SDD-INFRA-006 §2.2, `:57-62`), so **in practice every Finance message hit the defect**. |

### 2.2 The filter ordering that turns the stale claim into a silent ACK

| # | File : line | What the code does |
|---|---|---|
| 6 | `src/Infrastructure/Messaging/Finance.Infrastructure.Messaging/MessagingServiceCollectionExtensions.cs:122-123` | `bus.UseMessageRetry(retry => retry.Intervals(RetryIntervals));` on **122**, then `bus.UseFinanceIdempotency(context);` on **123**. MassTransit composes consume filters in registration order, so **retry is OUTER and idempotency is INNER**. `RetryIntervals` = 1 s / 5 s / 15 s (`:23-28`). |
| 7 | `src/Infrastructure/EventLog/Finance.EventLog.API/Extensions/EventLogMessagingExtensions.cs:91-92` | **A second, independent registration site with the identical ordering** — `UseMessageRetry` on **91**, `UseFinanceIdempotency` on **92**. `AddEventLogConsumers` (`:40`, called from `Finance.EventLog.API/Program.cs:60`) does not go through `AddFinanceMessageBus`, so the EventLog service was exposed by its own wiring. |
| 8 | `src/Infrastructure/Messaging/Finance.Infrastructure.Messaging/FinanceIdempotencyConfiguratorExtensions.cs:26` | `configurator.UseConsumeFilter(typeof(IdempotencyFilter<>), registration);` — registered as an **open generic on the bus-level consume pipeline**, so the defect applied uniformly to every receive endpoint of every service, with no per-consumer opt-out. |

### 2.3 Why the retry filter reads the skip as a success

The inner filter's duplicate branch (`:71-79`) `return`s a **completed, non-faulted** `Task`. MassTransit's `UseMessageRetry` decides whether to retry by observing whether the downstream pipe faulted. Attempt 1 faults (consumer threw) → the retry filter waits 1 s and re-sends the same `ConsumeContext`; attempt 2 does **not** fault (the idempotency filter short-circuited) → the retry filter concludes the message was handled → the consume pipeline completes → the transport ACKs. Two consequences:

- The **5 s and 15 s retry intervals were unreachable dead configuration** — the ladder always terminated at attempt 2.
- **Nothing ever reached `<queue>_error`**, so the SDD-INFRA-006 §2.4 non-zero-DLQ-depth Grafana alert and the `MESSAGE_DEAD_LETTERED` code (§4) could never fire. The *only* trace of a destroyed message was one `LogWarning` (`:73-77`) **byte-identical to a legitimate replay skip** — unalertable, and indistinguishable in Loki from healthy duplicate suppression.

### 2.4 Scope limit (stated for precision)

The defect bit only consumers that **throw** — which is exactly the path SDD-INFRA-006 §2.3 (`:87`) *mandates*: *"Retryable infrastructure failures — DB connection lost — DO throw so MassTransit retries."* Consumers correctly following the same rule's first half (log-and-acknowledge on permanent business failures) were unaffected. **The defect therefore destroyed precisely the class of message the platform was designed to recover — transient infrastructure failures — and spared the class it was designed to drop.**

## 3. Failing scenario (dated, Bulgaria / BGN, `SALE_INVOICE` = Dr 411 / Cr 701 + Cr 4532)

1. **2026-08-04 11:07:12** — An operator confirms sale invoice `INV-2026-000814` (net 1000.00, VAT 200.00, gross 1200.00 BGN). `ConfirmInTransactionAsync` allocates the gapless number, stamps `Confirmed`, writes the audit row, and enqueues `InvoiceConfirmedEvent` to the transactional outbox with `MessageId = 7f3ac1d2-…` and `CorrelationId = c-8841`. Commit. **200 OK.**
2. **11:07:13** — The outbox delivery service publishes to the `finance-journal` queue. The inner `IdempotencyFilter<InvoiceConfirmedEvent>` runs `SETNX finance:processed:7f3ac1d2-… "1" EX 7d` → **true** (first occurrence), and forwards to `InvoiceConfirmedEventConsumer`.
3. **11:07:13** — The consumer resolves rule `SALE_INVOICE` and calls `IPostingEngine.ApplyAsync(PostImmediately = true)`. The SQL Server connection drops mid-transaction — a textbook transient failure. The transaction rolls back; **no journal entry exists**. The consumer throws, exactly as §2.3 requires. **Pre-fix, the claim on `7f3ac1d2-…` is not released.**
4. **11:07:14** — The outer retry filter's first interval (1 s) elapses and re-sends the **same** `ConsumeContext` with the **same** `MessageId` down the pipe. The inner idempotency filter runs `SETNX` on `finance:processed:7f3ac1d2-…` → **false** — it has found **its own claim from step 2**, whose TTL has 6 days 23 h 59 min left. It logs `"Duplicate message 7f3ac1d2-… of type InvoiceConfirmedEvent skipped. Code=DUPLICATE_MESSAGE_SKIPPED"` and returns **without invoking the consumer**.
5. **11:07:14** — The retry filter sees a non-faulted attempt 2, reports success, and MassTransit **ACKs the message to RabbitMQ. The event is gone.** `finance-journal_error` depth stays 0.
6. **End state, permanent:**
   - **No journal entry exists for `INV-2026-000814`.** Dr 411 1200.00 / Cr 701 1000.00 / Cr 4532 200.00 never reach the general ledger.
   - No `InvoicePostedEvent` is ever published, so `LinkPostedJournalEntryAsync` never runs: the invoice is stuck in `Confirmed` with `JournalEntryId = null`, holding a legally issued gapless НАП number with **no GL entry behind it**.
   - The August trial balance (SDD-FIN-003) **understates** revenue by 1000.00 and output VAT on 4532 by 200.00 — the filed VAT return is short by 200.00.
   - No DLQ message, no DLQ alert, no `MESSAGE_DEAD_LETTERED`. There is **nothing for an operator to replay**.
   - Redis holds `finance:processed:7f3ac1d2-…` for a further **7 days**, so even a manual re-publish of the same event is silently skipped. Recovery required either waiting out the TTL or deleting the key by hand — and nobody knew to, because the only symptom was a warning that looks exactly like healthy deduplication.

**This is not a narrow race.** It fired on *every* transient failure of *every* consumer: a SQL failover, a connection-pool exhaustion, a deadlock victim, a `TimeoutException`, a Redis blip inside a consumer, a rolling deploy mid-consume. Each one silently destroyed the message.

## 4. Blast radius — every shipped consumer

The filter is registered as an open generic on the bus consume pipeline (evidence #8) at **both** registration sites (evidence #6, #7), so **all twelve shipped consumers were affected**, with no exceptions:

| Consumer | File | Spec | What a swallowed failure cost |
|---|---|---|---|
| `GoodsReceiptCompletedConsumer` | `src/Interfaces/Invoices/Finance.Invoices.API/Consumers/GoodsReceiptCompletedConsumer.cs` | SDD-INT-WH-001 | A completed Warehouse goods receipt produces **no purchase-invoice draft, ever**. AP is understated and there is no record that the event arrived. |
| `ShipmentCompletedConsumer` | `…/Finance.Invoices.API/Consumers/ShipmentCompletedConsumer.cs` | SDD-INT-WH-001 | A completed shipment produces **no sale-invoice draft** — goods leave the warehouse and are never billed. Revenue and output VAT are silently lost. |
| `SupplierReturnShippedConsumer` | `…/Finance.Invoices.API/Consumers/SupplierReturnShippedConsumer.cs` | SDD-INT-WH-001 | No debit-note draft — the supplier is never debited for returned goods. |
| `CustomerReturnCompletedConsumer` | `…/Finance.Invoices.API/Consumers/CustomerReturnCompletedConsumer.cs` | SDD-INT-WH-001 | No credit-note draft — the customer is never credited for a return, and the original invoice stays fully payable. |
| `InvoiceConfirmedEventConsumer` | `src/Interfaces/Journal/Finance.Journal.API/Consumers/InvoiceConfirmedEventConsumer.cs` | SDD-INV-001 §2.5 | **The §3 scenario.** A confirmed invoice never posts to the GL and is stranded in `Confirmed` forever with a gapless number and no entry. The most severe case: it breaks the invoice→journal posting handshake. |
| `InvoicePostedEventConsumer` | `src/Interfaces/Invoices/Finance.Invoices.API/Consumers/InvoicePostedEventConsumer.cs` | SDD-INV-001 | The back-event is lost: the journal entry **is** posted but `JournalEntryId` is never linked and the invoice never reaches `Posted`. The GL and the invoice disagree, with no DLQ entry to explain it. |
| `AccountCreatedEventConsumer` | `src/Infrastructure/EventLog/Finance.EventLog.API/Consumers/AccountCreatedEventConsumer.cs` | SDD-EVTLOG-001, SDD-AUDIT-001 | A missing `EventLog` row is a **missing audit row** on a chart-of-accounts mutation, under a ≥ 10-year retention obligation. |
| `AccountUpdatedEventConsumer` | `…/Finance.EventLog.API/Consumers/AccountUpdatedEventConsumer.cs` | SDD-EVTLOG-001, SDD-AUDIT-001 | As above. |
| `AccountDeactivatedEventConsumer` | `…/Finance.EventLog.API/Consumers/AccountDeactivatedEventConsumer.cs` | SDD-EVTLOG-001, SDD-AUDIT-001 | As above. |
| `CurrencyCreatedEventConsumer` | `…/Finance.EventLog.API/Consumers/CurrencyCreatedEventConsumer.cs` | SDD-EVTLOG-001, SDD-AUDIT-001 | As above. |
| `CurrencyUpdatedEventConsumer` | `…/Finance.EventLog.API/Consumers/CurrencyUpdatedEventConsumer.cs` | SDD-EVTLOG-001, SDD-AUDIT-001 | As above. |
| `CurrencyDeactivatedEventConsumer` | `…/Finance.EventLog.API/Consumers/CurrencyDeactivatedEventConsumer.cs` | SDD-EVTLOG-001, SDD-AUDIT-001 | As above. |

Cross-cutting consequences:

- **SDD-INFRA-006 §2.4 / §4** — the retry ladder terminated at attempt 2 (5 s and 15 s unreachable); `<queue>_error` was unreachable; the non-zero-DLQ-depth alert and `MESSAGE_DEAD_LETTERED` could never fire.
- **SDD-OBS-001** — the platform's only failure signal for a lost message was a `LogWarning` identical to a healthy replay skip. Message loss was **unobservable** in Loki and in Grafana.
- **SDD-AUDIT-001** — the EventLog consumers are the audit sink; silently dropping their messages breaches the append-only audit guarantee at the *ingest* boundary, where the `audit`-schema UPDATE/DELETE DENY offers no protection.
- **SDD-INFRA-006 §1's central promise** — *"in Finance an unpublished `JournalEntryPostedEvent` could mean a missing audit row in `EventLog`, which is unacceptable"* — the outbox made publishing reliable, and then the consume side threw the message away.
- **SDD-PAY-001 / SDD-PAY-002 (Drafted)** — their retry-then-dead-letter recovery wording (PAY-001 §2.5/§2.18, PAY-002 §2.3/§2.14) and SDD-INV-001 §2.13/§2.15 are only true **because this fix ships**; each cites CHG-FIX-006 as its prerequisite (round-3 W1 part 5).

## 5. Scope

### In scope (shipped in this batch)

- Release the Redis claim in `IdempotencyFilter<T>.Send` when the downstream pipe throws, then rethrow preserving the exception's identity and stack.
- XML documentation on the class, on `Send`, and on the new private release helper stating the release-on-failure contract.
- `[Unit]` tests pinning both the release-on-failure path and the unchanged genuine-duplicate path.
- This change spec, and a one-line record in SDD-INFRA-006's status/change log.

### Out of scope (explicit)

- **The filter registration order.** `UseMessageRetry` stays before `UseFinanceIdempotency` at both sites (evidence #6, #7) — see §8.
- The duplicate short-circuit for a **genuine** replay, the `finance:processed:{MessageId}` key convention, and the 7-day TTL — all unchanged.
- **Redis failure behavior** — unchanged: a `SETNX` failure still propagates so MassTransit retries rather than risk double-processing. (Note the deliberate asymmetry with SDD-INFRA-004's fall-through-to-DB rule: for *caching* Redis is optional, but for *idempotency* it is the correctness guarantee, so it must fail loudly.)
- **Aggregate-level idempotency.** `MessageId` dedupe is a transport-level optimization, never a substitute for a source-document guard — see §9 and round-3 finding W3.
- The MassTransit `InboxState` inbox pattern as a replacement for this filter.

## 6. Behavior (Implemented — testable rules)

1. When the downstream consume pipe throws, `IdempotencyFilter<T>.Send` **MUST** delete `finance:processed:{MessageId}` before the exception leaves the filter.
2. The original exception **MUST** propagate with its identity and stack intact (bare `catch { … throw; }` — never `throw ex;`, never wrapped), so MassTransit's retry and dead-letter policy governs the message.
3. The release **MUST** be logged at Warning with the `MessageId` and message type, in wording distinguishable from the duplicate-skipped warning, so a released claim is separately searchable in Loki.
4. A **genuine** duplicate (a replay arriving after a *successful* consume) **MUST** still be short-circuited: `next` is not invoked a second time and the claim is **not** deleted.
5. A first occurrence that consumes successfully **MUST** leave its claim in place for the full 7-day TTL.
6. The key convention (`finance:processed:{MessageId}`), the TTL (7 days), the `When.NotExists` semantics, and Redis-failure propagation **MUST** be unchanged by this fix.

Implemented at `src/Infrastructure/Messaging/Finance.Infrastructure.Messaging/Filters/IdempotencyFilter.cs`:

- `:81-89` — the `try { await next.Send(context)…; } catch { await ReleaseClaimAsync(…); throw; }` guard (rules 1, 2).
- `:109-117` — `ReleaseClaimAsync`, performing `KeyDeleteAsync` and the distinct Warning (rules 1, 3).
- `:71-79` — the duplicate short-circuit, **unchanged** (rules 4, 6).
- `:8-20` and `:44-57` — class-level and `Send` XML docs stating the release-on-failure contract and citing CHG-FIX-006.

## 7. Why no system-spec behavior change is required

SDD-INFRA-006 **already mandated** the fixed behavior. Only the illustrative snippet was wrong:

| SDD-INFRA-006 : line | Existing wording | Status after the fix |
|---|---|---|
| `:18` (§1) | *"Every consumer is wrapped in `IdempotencyFilter<T>` … so retries and **DLQ replays** cannot double-post."* | Now true. Pre-fix a DLQ replay was impossible (nothing reached the DLQ) and a same-`MessageId` re-publish was blocked for 7 days. |
| `:87` (§2.3) | *"Retryable infrastructure failures — DB connection lost — **DO throw so MassTransit retries**."* | Now true. Pre-fix throwing caused the message to be *discarded*, the opposite of the mandate. |
| `:90-92` (§2.4) | *"After retries are exhausted, the message moves to `<queue>_error`. Operators get a Grafana alert on any non-zero DLQ depth."* + the replay tool | Now reachable. Pre-fix all three were unreachable. |
| `:131` (§4) | `MESSAGE_DEAD_LETTERED` — *"Consumer exhausted retries"* | Now emittable. |
| `:155-156` (§6) | `Consumer_RetriesOnTransientException`, `Consumer_DeadLettersAfterRetriesExhausted` — both `[Integration]` | Already-planned tests that would have caught this. They are `[Category("Integration")]` and excluded from the offline default run, which is **why the defect shipped**. |

The **only** stale artefact is the *illustrative* §2.5 snippet (`:99-110`), which predates and caused the defect. Per the round-3 resolution it is deliberately **not** rewritten here; the shipped `IdempotencyFilter.cs` is authoritative for the release step, and the one-line change-log entry added to SDD-INFRA-006 records that. **No MUST in SDD-INFRA-006 changes, is added, or is removed.**

## 8. Why the registration order was left unchanged

Swapping evidence #6/#7 so idempotency became the OUTER filter would **not** have fixed the defect — it only relocates it. With idempotency outer, the claim is taken once, the whole retry ladder runs inside it, and on exhaustion the exception propagates out through the idempotency filter, which pre-fix would still have left the claim live — blocking the subsequent DLQ replay for 7 days. **The release-on-failure fix is required and sufficient under either ordering**, so the order stays as shipped at both sites and the retry-attempt semantics operators already observe are preserved.

## 9. Risks

- **A partial-success consumer can now be re-run.** Releasing the claim means a consumer that commits a side effect and *then* throws will be retried. This is the intended trade — a *lost* financial event is strictly worse than a *retried* one — but it makes clear that `MessageId` dedupe is a transport-level optimization only. Aggregate-level idempotency is the real guard, and is already shipped where it matters: the Warehouse consumers dedupe on the source document (pinned by `Consumer_DistinctMessageIdSameSourceDocument_DoesNotCreateSecondDraft` and `Consumer_TransientFailureThenRetry_CreatesExactlyOneDraft` in `WarehouseConsumerIdempotencyTests`). Round-3 finding **W3** extends the same source-document guard to `JournalEntry` for `InvoiceConfirmedEventConsumer` and the new Payments consumer — **that work is the necessary complement to this fix** and ships alongside it.
- **DLQ depth will become non-zero for the first time.** Previously-invisible failures now surface as real dead-lettered messages and fire the §2.4 Grafana alert. Operators must expect a one-off rise in alerts after deploy — this is the defect becoming visible, not a regression.
- **A Redis `KeyDeleteAsync` failure during release** propagates from inside the `catch`, replacing the original consumer exception. The message still faults (so retry/dead-letter still runs, and correctness is preserved), but the root-cause exception can be masked in that narrow window. Accepted: a Redis outage is independently alerted, and the alternative — swallowing it — reintroduces a stale claim.
- **`ResolveMessageId`'s `NewId.NextGuid()` fallback** (`:125-128`) still means a transport-supplied-`MessageId`-less message gets no dedupe across attempts. Unchanged by this fix and harmless for Finance events (`required MessageId`, §2.2), but it remains a latent gap for any future non-`IFinanceEvent` contract.

## 10. Testing

`[Unit]`, in `src/Infrastructure/Finance.Infrastructure.Stateful.Tests/Messaging/IdempotencyFilterTests.cs` (`[Category("SDD-INFRA-006")]`, Redis faked via a mocked `IConnectionMultiplexer` — no broker, no real Redis):

| Test | Line | Pins |
|---|---|---|
| `Send_DownstreamPipeThrows_DeletesProcessedKeyAndRethrowsOriginalException` | `:99-127` | Rules 1 + 2 — asserts `Is.SameAs(consumerFailure)` (exception identity, not just type), `KeyDeleteAsync` on `finance:processed:{messageId}` exactly once, and `next.Send` invoked once. |
| `Send_GenuineDuplicateAfterSuccessfulConsume_DoesNotInvokeNextAgain_AndKeepsClaim` | `:133-160` | Rules 4 + 5 — a `SetupSequence` of `true` then `false` over two `Send` calls asserts `next.Send` ran once and `KeyDeleteAsync` was **never** called. |

Regression guard (pre-existing, unchanged and green — proving rule 6):

- `Send_SkipsDuplicateMessageId_WhenSetNxReportsDuplicate` (`:40-54`)
- `Send_ForwardsFirstOccurrence_WhenSetNxClaimsKey` (`:58-70`)
- `Send_UsesProcessedKeyConvention_WithSevenDayTtlAndNotExists` (`:74-93`) — key, `"1"`, 7-day TTL, `When.NotExists`
- `Send_NullContext_ThrowsArgumentNullException` (`:164-173`)
- `Consumer_DuplicateMessageId_IsSkipped_ByIdempotencyFilter` in `src/Interfaces/Invoices/Finance.Invoices.API.Tests/Unit/Consumers/WarehouseConsumerIdempotencyTests.cs:52-78` — confirms the Warehouse consumers still skip genuine replays.

**Coverage gap that let this ship, recorded for the record:** the two `[Integration]` tests that would have caught it — `Consumer_RetriesOnTransientException` and `Consumer_DeadLettersAfterRetriesExhausted` (SDD-INFRA-006 §6, `:155-156`) — require a real RabbitMQ broker and are excluded from the default offline run. The new `[Unit]` tests close the gap **without** a broker by asserting on the filter's own contract (claim released + exception identity preserved) rather than on observed MassTransit behavior. Both integration tests remain owed once a broker-backed environment exists, and they are the ones that prove the *end-to-end* dead-letter path.

## 11. Affected specs / code

| Spec / file | Change |
|---|---|
| `src/Infrastructure/Messaging/Finance.Infrastructure.Messaging/Filters/IdempotencyFilter.cs` | **Fixed.** `try`/`catch` around the forward (`:81-89`); new private `ReleaseClaimAsync` (`:109-117`); class and `Send` XML docs updated (`:8-20`, `:44-57`). |
| `src/Infrastructure/Finance.Infrastructure.Stateful.Tests/Messaging/IdempotencyFilterTests.cs` | **Two `[Unit]` tests added** (`:99-127`, `:133-160`); fixture summary updated to state the release-on-failure contract. |
| `docs/infrastructure/SDD-INFRA-006-…md` | **One-line record only** in the existing Status/change-log style, citing CHG-FIX-006. No MUST added, changed, or removed (§7). The illustrative §2.5 snippet (`:99-110`) is deliberately left as-is. |
| `MessagingServiceCollectionExtensions.cs:122-123`, `EventLogMessagingExtensions.cs:91-92` | **Unchanged** — order deliberately preserved (§8). |
| SDD-PAY-001 §2.5/§2.18, SDD-PAY-002 §2.3/§2.14, SDD-INV-001 §2.13/§2.15 | Keep their retry-then-dead-letter recovery wording, each **citing CHG-FIX-006 as the prerequisite** (round-3 W1 part 5). |
| `docs/cross-reference-map.md` | Row for the two new tests against SDD-INFRA-006 + CHG-FIX-006. |

No migration. No API change. No event-contract change. No frontend impact. No new error code (`DUPLICATE_MESSAGE_SKIPPED` and `MESSAGE_DEAD_LETTERED` already exist, SDD-INFRA-006 §4) and therefore no i18n keys — the filter is never on a request path.

## 12. Rollout

No feature flag: the pre-fix behavior is message loss, so there is nothing worth being able to switch back to. The fix is confined to one library class; every consuming service picks it up on its next deploy. No ordering constraint against other changes in this batch, but W3's source-document guard **should** land in the same deploy so the newly enabled retries are idempotent at the aggregate level (§9). Operators must be told to expect first-ever non-zero DLQ depth and the resulting alerts.

## 13. Status

**Implemented and verified** in this batch: fix + two `[Unit]` tests + this change spec + the one-line SDD-INFRA-006 record. Owed: the two broker-backed `[Integration]` tests (§10).

## 14. Open questions

- Should the release-on-failure Warning be promoted to an alertable metric (`idempotency_claim_released_total`) so repeated releases on one `MessageId` surface a poison message *before* it exhausts the ladder? Deferred to SDD-OBS-001.
- Should the filter be replaced by MassTransit's `InboxState` inbox pattern, which handles claim lifecycle transactionally with the consumer's DB work and would remove this class of bug by construction? Out of scope here; a candidate for a future `CHG-REFAC`.
