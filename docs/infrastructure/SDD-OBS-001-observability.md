# SDD-OBS-001 — Observability (Logs, Traces, Metrics)

> Status: Planned
> Owner: Platform
> Last updated: 2026-05-28
> Category: Infrastructure
> Related: SDD-INFRA-001, SDD-INFRA-006
> Mirrors: Warehouse `SDD-OBS-001`

---

## 1. Context & Scope

Finance reuses the Warehouse observability stack on the shared `platform_net` Docker network: **NLog → Loki** for logs, **OpenTelemetry → Jaeger** for distributed traces, and Prometheus + Grafana for metrics + dashboards. One Grafana, one Loki, one Jaeger across both systems so a Finance support engineer can follow a customer's purchase from Warehouse Sales Order through Fulfillment shipment through Finance invoice posting in a single trace.

**In scope:**
- NLog configuration: console + file + Loki sink, with `service`, `level`, `correlation_id` labels
- Structured-logging conventions: no string interpolation; `_logger.LogInformation("Posted journal {EntryNumber} for {Amount}", number, amount)`
- OpenTelemetry auto-instrumentation: ASP.NET Core, HttpClient, EF Core, MassTransit, StackExchange.Redis
- W3C TraceContext propagation across services (HTTP headers + MassTransit message headers)
- OTLP exporter → Jaeger (gRPC port 4317)
- Metrics exposure on `/metrics` (Prometheus scrape)
- Standard log fields: `CorrelationId`, `ServiceName`, `UserId`, `RequestPath`, `RequestMethod`, `StatusCode`, `Elapsed`, `Environment`
- Grafana dashboards: per-service request rate, error rate, p50/p95/p99 latency, DLQ depth, outbox row count

**Out of scope:**
- Log aggregation across cloud regions (single deployment)
- APM-grade profiler integration
- Per-user log retention / GDPR redaction (TODO before production rollout)

## 2. Behavior

### 2.1 NLog → Loki (MUST)
Every microservice MUST register the NLog target set (`console`, `file`, `loki`) with three labels:
- `service` — kebab-case service name (`finance-accounts-api`, `finance-gateway`, `finance-journal-api`)
- `level` — lowercase log level
- `correlation_id` — from the ambient scope

The log layout MUST be: `${longdate}|${level:uppercase=true}|${logger}|${scopeproperty:CorrelationId}|${message}${onexception:...}`.

### 2.2 Structured logging (MUST)
- NO string interpolation in log calls (`$"..."`). Use templated parameters: `_logger.LogInformation("Created account {AccountCode}", code)`.
- Log levels: `Trace` (off in prod), `Debug` (off in prod), `Information` (default), `Warning`, `Error`, `Critical`.
- Sensitive fields (passwords, tokens, full credit card numbers, ID numbers in some jurisdictions) MUST NEVER be logged.

### 2.3 OpenTelemetry tracing (MUST)
- `services.AddOpenTelemetry().WithTracing(...)` MUST register:
  - `AddAspNetCoreInstrumentation()`
  - `AddHttpClientInstrumentation()`
  - `AddEntityFrameworkCoreInstrumentation()`
  - `AddSource("MassTransit")`
  - `AddSource("StackExchange.Redis")`
- The OTLP exporter MUST point to `OpenTelemetry:OtlpEndpoint` (default `http://platform-jaeger:4317`).
- The resource MUST set `service.name` to the kebab-case service identifier.
- The W3C `traceparent` header MUST be propagated automatically (default behavior) on outbound HTTP and MassTransit messages.

### 2.4 Correlation ID ↔ Trace ID (MUST)
- The correlation ID is the **business** identifier (RFC 4122 GUID) propagated via `X-Correlation-ID`.
- The trace ID is the **technical** identifier (W3C TraceContext) propagated via `traceparent`.
- Both MUST be logged on every record so an operator can search by either.
- The `CorrelationIdMiddleware` MUST also stamp the correlation ID onto the current `Activity.Current?.SetTag("correlation_id", id)` so it appears in Jaeger.

### 2.5 Metrics (SHOULD — Phase 7)
- Every microservice SHOULD expose `/metrics` for Prometheus scrape via `OpenTelemetry.Exporter.Prometheus.AspNetCore`.
- Built-in counters: HTTP request rate, latency histogram, exception rate.
- Domain counters (Phase 7): journal entries posted, invoices issued, payments matched, DLQ depth, outbox lag.

### 2.6 Dashboards (SHOULD)
Phase 7 ships these Grafana dashboards:
- **Finance overview** — request rate + error rate per service, p95 latency, DLQ depth, outbox lag
- **Posting flow** — count of `JournalEntryPostedEvent` published vs consumed by EventLog (gap = outbox stuck)
- **НАП compliance** — gapless sequence audit (no missing numbers per fiscal year per document type)

## 3. Validation Rules

- Startup MUST fail if `OpenTelemetry:OtlpEndpoint` is unset.
- Startup MUST fail if NLog targets fail to register (typo in `nlog.config`).

## 4. Error Rules

Observability layers MUST NEVER throw a request-path exception. A Loki outage logs to console + file; a Jaeger outage drops spans silently after the OTLP client's internal buffer.

## 5. Versioning Notes

v1: NLog + OpenTelemetry as described. Adding new log labels is additive. Removing a label requires updating Grafana queries and is a `CHG-ENH-*`.

## 6. Test Plan

| Test | Kind |
|---|---|
| `Logging_StampsCorrelationIdOnEveryRecord` | [Integration] |
| `Logging_DoesNotAcceptStringInterpolation_AnalyzerRule` | [Static analysis] |
| `Tracing_PropagatesTraceparent_AcrossHttpAndMassTransit` | [Integration] |
| `Tracing_StampsCorrelationIdAsActivityTag` | [Integration] |
| `Health_OtlpEndpointReachable` | [Integration] |
| `MetricsEndpoint_Returns200_WithDefaultCounters` | [Integration] |

## 7. Open Items

- Log retention policy in Loki (currently 30 days). Finance regulators may require 5–10 years for **audit** logs — likely a separate, longer-retention pipeline using `IAuditService` (SDD-AUDIT-001) rather than Loki.
- PII redaction filter in NLog (counterparty tax ID, personal name in certain reports).
