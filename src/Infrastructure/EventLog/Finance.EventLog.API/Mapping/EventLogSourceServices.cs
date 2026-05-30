namespace Finance.EventLog.API.Mapping;

/// <summary>
/// Canonical originating-service names recorded in <c>EventLogEntry.SourceService</c> for the consumed
/// Finance events (SDD-EVTLOG-001 §2.2). The values match each publisher's OTLP <c>service.name</c> so the
/// archive can be correlated with Loki / Jaeger telemetry.
/// </summary>
public static class EventLogSourceServices
{
    /// <summary>The Accounts microservice that publishes the account lifecycle events.</summary>
    public const string Accounts = "finance-accounts-api";

    /// <summary>The Nomenclature microservice that publishes the currency lifecycle events.</summary>
    public const string Nomenclature = "finance-nomenclature-api";
}
