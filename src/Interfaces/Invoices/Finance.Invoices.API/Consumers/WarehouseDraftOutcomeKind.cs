namespace Finance.Invoices.API.Consumers;

/// <summary>
/// The terminal outcome of materializing a draft invoice from a Warehouse inbound event
/// (SDD-INT-WH-001 §2.1, §2.4).
/// </summary>
public enum WarehouseDraftOutcomeKind
{
    /// <summary>A new draft invoice was created from the event.</summary>
    Created = 0,

    /// <summary>A draft already existed for the source document; the dedupe short-circuited creation.</summary>
    AlreadyExists = 1,

    /// <summary>A contract-check or create-path business failure — log and acknowledge, do not retry.</summary>
    PermanentFailure = 2
}
