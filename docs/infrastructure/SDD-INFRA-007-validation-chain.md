# SDD-INFRA-007 — Validation Chain

> Status: Planned
> Owner: Platform
> Last updated: 2026-05-28
> Category: Infrastructure
> Related: SDD-INFRA-001, SDD-INV-001 (future), SDD-PAY-001 (future)
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

- The composer MUST throw at startup (not at request time) if zero `IChainValidator<TRequest>` are registered for a request type that uses the chain — preventing silent "no validation" bugs.

## 4. Error Rules

The chain itself emits no codes — it forwards the failing validator's code. Examples introduced by Phase 4/5:

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

## 7. Open Items

- Async parallel validation when validators are demonstrably independent — defer until perf data warrants.
- Aggregating multiple failures vs short-circuiting. v1 short-circuits (matches Warehouse). Aggregating may be added later for UX (show all problems at once).
