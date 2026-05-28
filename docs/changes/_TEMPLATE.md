# CHG-<PREFIX>-<NNN> — <Title>

> Created: YYYY-MM-DD
> Author: <name>
> Status: Proposed | Approved | In Progress | Implemented | Archived
> Related specs: <SDD-IDs>
> Originating ticket: <link>

---

## 1. Summary

One-paragraph statement of what changes and why.

## 2. Motivation

What problem this solves, what user/stakeholder need it serves, what incident or measurement triggered it.

## 3. Scope

### In scope
- …

### Out of scope (explicit)
- …

## 4. Proposed Behavior

MUST / SHOULD / MAY rules, written so each is independently testable.

## 5. Affected Specs

| Spec ID | Section | Change |
|---|---|---|
| SDD-XYZ-NNN | §X | Add / Update / Remove rule … |

## 6. Database Changes

- New tables, columns, indexes, constraints
- Migration script file name
- Backfill plan
- Rollback plan

## 7. API Changes

- New endpoints / breaking changes to existing endpoints
- Versioning strategy (new `/api/v2/...` vs additive to `/api/v1/...`)
- Error codes added to `Finance.Common/ErrorCodes/<Domain>ErrorCodes.cs`
- i18n keys added to `frontend/src/shared/i18n/locales/{en,bg}.ts`

## 8. Event Contract Changes

- New events published / new events consumed
- Breaking changes to existing event schemas (handled via versioned topics)
- Outbox configuration

## 9. Frontend Impact

- New routes / dialogs / pages
- New hooks / stores
- i18n keys added (EN + BG)
- Modal vs page mode considerations

## 10. Testing

- Unit test names to add
- Integration test names to add (`Integration_` prefix)
- UI tests (Chrome DevTools MCP) — golden path + edge cases

## 11. Rollout

- Feature flag name (if any)
- Migration ordering
- Downstream coordination (Warehouse-side changes, accountant review)

## 12. Risks

- …

## 13. Open Questions

- …
