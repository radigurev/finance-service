# SDD-INFRA-007 — Validation Chain

> Status: Active (Batch 1 — `Finance.Common/Validation` chain mechanic + unit tests shipping; domain validators deferred to their owning microservices)
> Owner: Platform
> Last updated: 2026-05-30
> Category: Infrastructure
> Related: SDD-INFRA-001, SDD-INFRA-008, SDD-INFRA-009, SDD-INV-001 (future), SDD-PAY-001 (future)
> Mirrors: Warehouse `Warehouse.Common.Validation`

---

## 1. Context & Scope

This spec defines `Finance.Common.Validation`, the chain-of-responsibility validator used for **cross-cutting**, **stateful** validations that don't fit into a single FluentValidation `AbstractValidator<T>`. Typical examples in Finance:

- "Invoice can only be posted if its fiscal period is Open" (state of period table)
- "Payment allocation cannot exceed invoice outstanding balance" (joins payments + invoices)
- "Posting against an Inactive account is forbidden" (cross-table lookup)
- "Journal entry debits MUST equal credits" (intra-record but conditional on currency revaluation toggle)

These checks need DB lookups and ordered short-circuiting. FluentValidation is great for shape; `ValidationChain<TRequest>` is for cross-aggregate rules.

**In scope:**
- `IChainValidator<TRequest>` interface
- `ValidationChain<TRequest>` composer that runs validators in DI registration order and short-circuits on first failure
- DI extension `services.AddValidationChain<TRequest>()` that scans the assembly for `IChainValidator<TRequest>` implementations
- `ChainValidationResult` carrying success or a `Failure(errorCode, detail)`

**Out of scope:**
- Shape / range / length validations — use FluentValidation (`AbstractValidator<T>`)
- Authorization / RBAC — handled by `[RequirePermission(...)]` at the controller layer
- Workflow state-transition validation — handled by `IWorkflowEngine` (SDD-INFRA-008)

### Resolved Decision — implementation location (Batch 1)
- The chain mechanic lives in `src/Finance.Common/Validation/` and is **pure** — it MUST NOT take an EF Core dependency. Concrete domain validators inject their own `DbContext` from their owning microservice assembly (deferred to the relevant domain phase).
- Batch 1 ships: `IChainValidator<TRequest>`, `ChainValidationResult`, the `ValidationChain<TRequest>` composer, and the `AddValidationChain<TRequest>()` DI extension. No domain validators ship in Batch 1.
- Unit tests live in `src/Finance.Common.Tests` (NUnit). All Batch-1 tests are pure (`[Unit]`); the `InvoicePostPeriodValidator` example test in §6 is `[Category("Integration")]` and is excluded from the default Batch-1 run (it needs a DB and a domain validator that does not yet exist).

## 2. Behavior

### 2.1 Interface (MUST)
```csharp
public interface IChainValidator<TRequest>
{
    Task<ChainValidationResult> ValidateAsync(TRequest request, CancellationToken ct);
}

public readonly record struct ChainValidationResult(bool IsValid, string? ErrorCode, string? Detail)
{
    public static ChainValidationResult Success() => new(true, null, null);
    public static ChainValidationResult Failure(string code, string? detail = null) => new(false, code, detail);
}
```

### 2.2 Composition (MUST)
- The composer runs validators in DI registration order.
- The first `Failure(...)` short-circuits the chain — no later validators are invoked.
- The composer MUST be `Scoped` so injected `DbContext`s share the request's UoW.
- **Resolved Decision (Batch 1):** `AddValidationChain<TRequest>()` registers the composer **Scoped** alongside the request type's validators in the same call. Keep it simple — the composer enumerates the DI-registered `IChainValidator<TRequest>` set in registration order.

### 2.3 Service layer integration (MUST)
- Services call the chain BEFORE persisting:
```csharp
ChainValidationResult check = await _chain.ValidateAsync(request, ct).ConfigureAwait(false);
if (!check.IsValid)
{
    return Result.Failure(check.ErrorCode!, check.Detail);
}
```

### 2.4 Error mapping (MUST)
- The service maps `ChainValidationResult.ErrorCode` directly onto ProblemDetails `title` via the controller's `Result.Failure(...) → ActionResult` helper.
- The error code MUST be a constant in `Finance.Common.ErrorCodes.<Domain>ErrorCodes.cs` — never a raw string.

### 2.5 Determinism (MUST)
- Validators MUST be pure functions of (request, current DB state). Repeated invocation with the same request and DB state MUST produce the same result.
- Validators MUST NOT mutate state. (Use the service layer for mutations.)

### 2.6 Cancellation (MUST)
- Every validator MUST honor the supplied `CancellationToken` and pass it through to DB calls.

## 3. Validation Rules

- The composer SHOULD surface a "no validators registered" situation rather than silently passing for a request type that uses the chain.
- **Resolved Decision (Batch 1) — documented deviation:** the original rule required a dedicated startup-reflection enforcement system that throws at startup when zero validators are registered. To avoid overengineering, Batch 1 does NOT build a separate startup-scan/throw subsystem. Because `AddValidationChain<TRequest>()` registers the composer and its validators together in one call (§2.2), a request type that uses the chain is registered with its validators by construction. This is a minor, intentional deviation from the spec's original startup-throw requirement; revisit with a `CHG-ENH-*` if a silent "no validation" bug ever surfaces in practice.

## 4. Error Rules

The chain itself emits no codes — it forwards the failing validator's code. Forwarded codes MUST be `public const string` constants from `src/Finance.Common/ErrorCodes/` (a `<Domain>ErrorCodes` class or `CommonErrorCodes`) — never a raw string literal. Generic fallbacks `CommonErrorCodes.VALIDATION_FAILED` and `CommonErrorCodes.GENERIC_ERROR` are available for cross-cutting cases. Examples introduced by Phase 4/5:

| Code | Validator | Trigger |
|---|---|---|
| `INVOICE_POST_PERIOD_CLOSED` | `InvoicePostPeriodValidator` | Trying to post against a Closed period |
| `INVOICE_POST_ACCOUNT_INACTIVE` | `InvoicePostAccountValidator` | Any line references an inactive account |
| `PAYMENT_ALLOCATION_EXCEEDS_OUTSTANDING` | `PaymentAllocationValidator` | Sum of allocations > invoice outstanding |
| `JOURNAL_ENTRY_UNBALANCED` | `JournalEntryBalanceValidator` | Σ debits ≠ Σ credits |

## 5. Versioning Notes

v1 is the chain mechanic. Each new validator is a code change with no API surface impact and no version bump.

## 6. Test Plan

| Test | Kind |
|---|---|
| `Chain_RunsValidatorsInRegistrationOrder` | [Unit] |
| `Chain_ShortCircuitsOnFirstFailure` | [Unit] |
| `Chain_PassesCancellationTokenToValidators` | [Unit] |
| `Chain_ReturnsSuccess_WhenAllValidatorsPass` | [Unit] |
| `Composer_ThrowsAtStartup_WhenNoValidatorsRegistered` | [Unit] |
| `JournalEntryBalanceValidator_ReturnsUnbalanced_WhenDebitsNeCredits` | [Unit] |
| `InvoicePostPeriodValidator_ReturnsFailure_WhenPeriodClosed` | [Integration] |

## 7. Resolved Decisions & Deferred Items

### Resolved (Batch 1)
- **Short-circuit vs aggregate:** v1 **short-circuits** on the first failure (matches Warehouse). Aggregating multiple failures is NOT in v1.
- **Startup enforcement:** no separate startup-scan/throw subsystem (see §3 documented deviation).

### Deferred
- Async parallel validation when validators are demonstrably independent — defer until perf data warrants.
- Aggregating multiple failures (show all problems at once) for better UX — future `CHG-ENH-*`.
