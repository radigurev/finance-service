using Finance.ServiceModel.Invoices;

namespace Finance.Invoices.API.Consumers;

/// <summary>
/// The result of attempting to materialize a draft invoice from a Warehouse inbound event
/// (SDD-INT-WH-001 §2.1, §2.4). Distinguishes the three terminal outcomes the consumer acts on:
/// <list type="bullet">
/// <item><description><see cref="Created"/> — a new draft was created.</description></item>
/// <item><description><see cref="AlreadyExists"/> — a draft already exists for the source document
/// (source-document dedupe, §2.1.2); the message is acknowledged with no second draft.</description></item>
/// <item><description><see cref="PermanentFailure"/> — a contract-check or create-path business failure
/// (§2.4); the message is logged at error and acknowledged (NOT thrown) so it does not poison the
/// queue.</description></item>
/// </list>
/// A transient infrastructure failure (DB/Redis down) is NOT represented here — it propagates as an
/// exception from the factory so MassTransit retries (§2.4).
/// </summary>
public sealed record WarehouseDraftOutcome
{
    private WarehouseDraftOutcome(
        WarehouseDraftOutcomeKind kind,
        InvoiceDto? invoice,
        string? errorCode,
        string? detail)
    {
        Kind = kind;
        Invoice = invoice;
        ErrorCode = errorCode;
        Detail = detail;
    }

    /// <summary>The terminal outcome kind.</summary>
    public WarehouseDraftOutcomeKind Kind { get; }

    /// <summary>The created or pre-existing draft, when one is associated with the outcome; otherwise <c>null</c>.</summary>
    public InvoiceDto? Invoice { get; }

    /// <summary>The business error code on a permanent failure; otherwise <c>null</c>.</summary>
    public string? ErrorCode { get; }

    /// <summary>Optional developer-facing detail on a permanent failure; otherwise <c>null</c>.</summary>
    public string? Detail { get; }

    /// <summary>Creates a <see cref="WarehouseDraftOutcomeKind.Created"/> outcome for a newly created draft.</summary>
    /// <param name="invoice">The created draft.</param>
    /// <returns>A created outcome.</returns>
    public static WarehouseDraftOutcome Created(InvoiceDto invoice) =>
        new(WarehouseDraftOutcomeKind.Created, invoice, null, null);

    /// <summary>Creates an <see cref="WarehouseDraftOutcomeKind.AlreadyExists"/> dedupe outcome.</summary>
    /// <param name="invoice">The pre-existing draft for the source document.</param>
    /// <returns>An already-exists outcome.</returns>
    public static WarehouseDraftOutcome AlreadyExists(InvoiceDto invoice) =>
        new(WarehouseDraftOutcomeKind.AlreadyExists, invoice, null, null);

    /// <summary>Creates a <see cref="WarehouseDraftOutcomeKind.PermanentFailure"/> outcome.</summary>
    /// <param name="errorCode">The business error code that classifies the failure.</param>
    /// <param name="detail">Optional developer-facing detail.</param>
    /// <returns>A permanent-failure outcome.</returns>
    public static WarehouseDraftOutcome PermanentFailure(string errorCode, string? detail = null) =>
        new(WarehouseDraftOutcomeKind.PermanentFailure, null, errorCode, detail);
}
