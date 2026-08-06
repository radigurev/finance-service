# CHG-FIX-007 — A malformed `RowVersion` returns 400 on allocate but 409 everywhere else, under the same error code

> Created: 2026-08-06
> Author: Phase-3 tester (Batch 17 follow-up — closing the SDD-PAY-001 §3.1 validator gap)
> Status: Proposed
> Related specs: SDD-PAY-001 (§3.1, §3.3, §4 — authoritative for the payment write paths), SDD-PAY-002 (§3.1 — the allocate path), SDD-INFRA-001 (ProblemDetails + error-code-to-status mapping), SDD-INFRA-009 (`Result<T>` for business outcomes)
> Originating ticket: found while writing `UpdatePaymentRequestValidatorTests`, which exposed that SDD-PAY-001 §3.1's "valid base64" rule was never implemented in a validator

---

## 1. Summary

`CommonErrorCodes.CONCURRENT_MODIFICATION` resolves to **409** through the shipped `DefaultErrorCodeToStatusMap` conflict family (`StartsWith("CONCURRENT_")`). Two Payments write paths disagree about which layer rejects a malformed base64 `RowVersion`, and therefore about which HTTP status the client sees for the identical error code:

| Path | Where the malformed token is caught | Status | Code returned |
|---|---|---|---|
| `PUT /payments/{id}`, cancel, reverse | `PaymentService.TryDecodeRowVersion` (`PaymentService.cs:987-1018`) → `Result.Failure` | **409** | `CONCURRENT_MODIFICATION` |
| `POST /payments/{id}/allocations` | `AllocatePaymentRequestValidator` (`:26-32`) via `RowVersionTokenRule.IsBase64` | **400** | `CONCURRENT_MODIFICATION` |

A client cannot distinguish these by code, only by status, and the 400 case carries a code the platform documents as a conflict.

## 2. Motivation

Neither behavior is broken on its own — both reject the token and both return the documented code — but the pair is incoherent, and the frontend's `getApiErrorMessage` plus any retry logic keyed on 409-vs-400 will treat one path differently from the other for the same client mistake. It also means SDD-PAY-001 §3.1 and SDD-PAY-002 §3.1 describe the same field with different enforcement layers without either spec acknowledging the other.

This was invisible until Batch 17's follow-up test pass: `UpdatePaymentRequestValidator` had **zero** tests, so the missing base64 rule on the payment paths had never been observed, and the allocate path's rule had never been compared against it.

## 3. Scope

One validator rule and its test, or one spec sentence — see §4. No schema, no event contract, no migration.

## 4. Proposed Behavior

**Option A (recommended) — make everything 409, matching the code's documented status.** Remove the `.Must(RowVersionTokenRule.IsBase64)` rule from `AllocatePaymentRequestValidator`, letting the allocate path fall through to the same service-layer decode the other paths use. Update `Validate_MalformedRowVersion_ReturnsConcurrentModification` (`AllocatePaymentRequestValidatorTests.cs:106`) to assert the service-layer outcome instead of the validator outcome, mirroring `Update_MalformedBase64RowVersion_ReturnsConcurrentModification`. Amend SDD-PAY-002 §3.1 to match SDD-PAY-001 §3.1's note. `RowVersionTokenRule.IsBase64` stays, because the service guard still uses the same predicate.

**Option B — make everything 400.** Add the rule to the four SDD-PAY-001 validators and amend §4 so a malformed token maps to 400 while a *stale* token stays 409. This splits one error code across two statuses by cause, which is arguably more precise but requires either a second error code or a documented exception to the conflict-family mapping.

Option A is recommended: it removes a special case rather than adding one, it keeps one code to one status, and a malformed token is already indistinguishable from a stale one at the point the client sees it.

## 5. Affected Specs

| Spec | Change under Option A |
|---|---|
| SDD-PAY-002 §3.1 | Record that the base64 decode is a service-layer guard, matching SDD-PAY-001 §3.1's note |
| SDD-PAY-001 §3.1 | Already amended in Batch 17 to describe the shipped service-layer guard and to cite this change spec |

## 6. Database Changes

None.

## 7. API Changes

Under Option A, `POST /api/v1/payments/{id}/allocations` with a malformed `rowVersion` changes from **400** to **409**. The response body's `title` (`CONCURRENT_MODIFICATION`) is unchanged, so a client keying on the code is unaffected; only a client keying on the status sees a difference. No frontend change is expected — `getApiErrorMessage` maps by code.

## 8. Event Contract Changes

None.

## 9. Frontend Impact

None expected. The `errors.CONCURRENT_MODIFICATION` key already exists in both locales and is reached by code, not status.

## 10. Testing

`Validate_MalformedRowVersion_ReturnsConcurrentModification` moves from the validator fixture to a service-level assertion. The existing `Update_MalformedBase64RowVersion_ReturnsConcurrentModification` (which also asserts nothing is persisted and no audit row is written) is the pattern to mirror. An integration test asserting the status itself belongs with the deferred SDD-PAY-002 §6.6 suite.

## 11. Rollout

Low urgency — it only affects a malformed-token client bug. Bundle it with the next Payments batch, or with the deferred integration suites, where the status can be asserted end-to-end for the first time.

## 12. Risks

Very low. The only behavioral change is a status code on an input-error path that no shipped frontend branches on. The risk of *not* doing it is that the inconsistency gets copied into the next service that follows the allocate validator as its template.

## 13. Open Questions

- Should a malformed token and a stale token be distinguishable at all? Today both return `CONCURRENT_MODIFICATION`, which conflates a client bug with a genuine optimistic-concurrency conflict. A separate `INVALID_ROW_VERSION` code (400) would be cleaner than either option above, but it is a wider change touching every aggregate in the platform, not just Payments.
