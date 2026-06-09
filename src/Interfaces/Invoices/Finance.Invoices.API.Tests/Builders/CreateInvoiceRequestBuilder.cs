using Finance.Common.Enums;
using Finance.ServiceModel.Invoices;

namespace Finance.Invoices.API.Tests.Builders;

/// <summary>
/// Builds <see cref="CreateInvoiceRequest"/> instances for the Invoices unit tests. Defaults to a valid
/// single-line sale invoice so a test overrides only the field it exercises (SDD-INV-001 §2.3).
/// </summary>
public sealed class CreateInvoiceRequestBuilder
{
    private InvoiceDocumentType _documentType = InvoiceDocumentType.SaleInvoice;
    private Guid _counterpartyId = new("11111111-1111-1111-1111-111111111111");
    private string _currencyCode = "BGN";
    private DateTimeOffset _issueDate = new(2026, 1, 15, 0, 0, 0, TimeSpan.Zero);
    private DateTimeOffset _dueDate = new(2026, 2, 15, 0, 0, 0, TimeSpan.Zero);
    private Guid? _correctsInvoiceId;
    private Guid? _sourceDocumentId;
    private string? _sourceDocumentType;
    private string? _correlationId;
    private IReadOnlyList<InvoiceLineRequest> _lines =
    [
        InvoiceLineRequestBuilder.Create().Build()
    ];

    /// <summary>Starts a new builder with valid defaults.</summary>
    /// <returns>A fresh builder.</returns>
    public static CreateInvoiceRequestBuilder Create() => new();

    /// <summary>Sets the document type.</summary>
    /// <param name="documentType">The document type.</param>
    /// <returns>This builder.</returns>
    public CreateInvoiceRequestBuilder WithDocumentType(InvoiceDocumentType documentType)
    {
        _documentType = documentType;
        return this;
    }

    /// <summary>Sets the counterparty reference.</summary>
    /// <param name="counterpartyId">The counterparty id.</param>
    /// <returns>This builder.</returns>
    public CreateInvoiceRequestBuilder WithCounterpartyId(Guid counterpartyId)
    {
        _counterpartyId = counterpartyId;
        return this;
    }

    /// <summary>Sets the transactional currency code.</summary>
    /// <param name="currencyCode">The ISO 4217 code.</param>
    /// <returns>This builder.</returns>
    public CreateInvoiceRequestBuilder WithCurrencyCode(string currencyCode)
    {
        _currencyCode = currencyCode;
        return this;
    }

    /// <summary>Sets the issue date.</summary>
    /// <param name="issueDate">The issue date.</param>
    /// <returns>This builder.</returns>
    public CreateInvoiceRequestBuilder WithIssueDate(DateTimeOffset issueDate)
    {
        _issueDate = issueDate;
        return this;
    }

    /// <summary>Sets the due date.</summary>
    /// <param name="dueDate">The due date.</param>
    /// <returns>This builder.</returns>
    public CreateInvoiceRequestBuilder WithDueDate(DateTimeOffset dueDate)
    {
        _dueDate = dueDate;
        return this;
    }

    /// <summary>Sets the original-invoice linkage for a credit/debit note.</summary>
    /// <param name="correctsInvoiceId">The original invoice id.</param>
    /// <returns>This builder.</returns>
    public CreateInvoiceRequestBuilder WithCorrectsInvoiceId(Guid? correctsInvoiceId)
    {
        _correctsInvoiceId = correctsInvoiceId;
        return this;
    }

    /// <summary>Sets the Warehouse source-document linkage.</summary>
    /// <param name="sourceDocumentType">The source-document type tag.</param>
    /// <param name="sourceDocumentId">The source-document id.</param>
    /// <returns>This builder.</returns>
    public CreateInvoiceRequestBuilder WithSourceDocument(string sourceDocumentType, Guid sourceDocumentId)
    {
        _sourceDocumentType = sourceDocumentType;
        _sourceDocumentId = sourceDocumentId;
        return this;
    }

    /// <summary>Sets an explicit correlation id to stamp on the created draft.</summary>
    /// <param name="correlationId">The correlation id.</param>
    /// <returns>This builder.</returns>
    public CreateInvoiceRequestBuilder WithCorrelationId(string correlationId)
    {
        _correlationId = correlationId;
        return this;
    }

    /// <summary>Replaces the lines with the supplied set.</summary>
    /// <param name="lines">The invoice lines.</param>
    /// <returns>This builder.</returns>
    public CreateInvoiceRequestBuilder WithLines(params InvoiceLineRequest[] lines)
    {
        _lines = lines;
        return this;
    }

    /// <summary>Removes all lines (an empty draft) for the system-create path.</summary>
    /// <returns>This builder.</returns>
    public CreateInvoiceRequestBuilder WithNoLines()
    {
        _lines = [];
        return this;
    }

    /// <summary>Materializes the configured create request.</summary>
    /// <returns>The built <see cref="CreateInvoiceRequest"/>.</returns>
    public CreateInvoiceRequest Build() => new()
    {
        DocumentType = _documentType,
        CounterpartyId = _counterpartyId,
        CurrencyCode = _currencyCode,
        IssueDate = _issueDate,
        DueDate = _dueDate,
        Lines = _lines,
        CorrectsInvoiceId = _correctsInvoiceId,
        SourceDocumentId = _sourceDocumentId,
        SourceDocumentType = _sourceDocumentType,
        CorrelationId = _correlationId
    };
}
