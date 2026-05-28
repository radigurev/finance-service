# Cross-Reference Map

Every SDD must map to its tests, implementation files, and (if applicable) frontend features. Update this file in the same PR as the spec.

## Format

| Spec | Status | Tests | Implementation | Frontend |
|---|---|---|---|---|
| `SDD-FIN-001` Double-Entry Engine | Planned | — | — | — |
| `SDD-FIN-002` Journal Entry Lifecycle | Planned | — | — | — |
| `SDD-FIN-003` General Ledger & Trial Balance | Planned | — | — | — |
| `SDD-FIN-004` Fiscal Period Management | Planned | — | — | — |
| `SDD-FIN-005` Multi-Currency Engine | Planned | — | — | — |
| `SDD-FIN-006` Posting Engine + Posting Rules | Planned | — | — | — |
| `SDD-ACCT-001` Chart of Accounts | Draft (shell) | (planned) | `src/Interfaces/Accounts/Finance.Accounts.API/Controllers/AccountsController.cs`, `Services/AccountService.cs`, `AccountRepository.cs`, `Validators/CreateAccountRequestValidator.cs`; `Databases/Finance.Accounts.DBModel/Models/Account.cs`, `Configurations/AccountConfiguration.cs` | `frontend/src/features/accounts/AccountsListPage.tsx` |
| `SDD-INV-001` Invoice Lifecycle | Planned | — | — | — |
| `SDD-PAY-001` Payment Recording & Matching | Planned | — | — | — |
| `SDD-PAY-002` Settlement & Allocation | Planned | — | — | — |
| `SDD-RPT-001` Trial Balance | Planned | — | — | — |
| `SDD-RPT-002` Balance Sheet + Income Statement | Planned | — | — | — |
| `SDD-RPT-003` VAT Journals | Planned | — | — | — |
| `SDD-CTRY-001` Country Strategy Interface | Planned | — | — | — |
| `SDD-CTRY-BG-001` Bulgaria Strategy | Planned | — | — | — |
| `SDD-INT-WH-001` Warehouse Event Subscriptions | Planned | — | — | — |
| `SDD-INT-WH-002` Finance → Warehouse Refit Client | Planned | — | — | — |
| `SDD-INT-AUTH-001` Shared JWT Authentication | Draft (shell) | (planned) | `src/Interfaces/Accounts/Finance.Accounts.API/Program.cs` (AddWarehouseAuthentication); `[RequirePermission(...)]` on `AccountsController` | `frontend/src/features/auth/LoginPage.tsx`, `RequireAuth.tsx`, `shared/api/axios.ts`, `shared/stores/auth.ts` |
| `SDD-INT-BNB-001` BNB Exchange-Rate Provider | Planned | — | — | — |
| `SDD-INT-NAP-001` НАП Regulatory Export | Planned | — | — | — |
| `SDD-INFRA-001` Cross-Cutting Foundations | Draft (shell) | (planned) | `src/Interfaces/Accounts/Finance.Accounts.API/Program.cs`, `nlog.config`, `appsettings.json.template`; `src/Finance.Common/ErrorCodes/AccountErrorCodes.cs` | `frontend/src/shared/api/axios.ts`, `shared/utils/getApiErrorMessage.ts` |
| `SDD-INFRA-002` Finance Gateway (YARP) | Draft (shell) | (planned) | `src/Infrastructure/Gateway/Finance.Gateway/Program.cs`, `CorrelationIdRequestTransform.cs`, `appsettings.json.template` | — |
| `SDD-INFRA-003` Sequence Generation (gapless) | Planned | — | — | — |
| `SDD-INFRA-004` Transactional Outbox + Idempotency | Planned | — | — | — |
| `SDD-INFRA-005` Feature Flags | Planned | — | — | — |
| `SDD-OBS-001` NLog → Loki, OpenTelemetry → Jaeger | Planned | — | — | — |
| `SDD-AUDIT-001` Immutable Audit Trail | Planned | — | — | — |
| `SDD-EVTLOG-001` Centralized Event Log | Planned | — | — | — |
| `SDD-UI-001` Frontend Shell (React + MUI + i18n + Density) | Draft (shell) | (planned) | — | `frontend/src/main.tsx`, `app/{App,AppShell}.tsx`, `shared/stores/{auth,layout,theme}.ts`, `shared/i18n/locales/{en,bg}.ts`, `shared/api/axios.ts`, `shared/utils/getApiErrorMessage.ts`, `features/auth/{LoginPage,RequireAuth}.tsx`, `features/accounts/AccountsListPage.tsx` |
| `SDD-UI-002` Modal vs Page Form Mode + `useGoBack` | Planned | — | — | — |
