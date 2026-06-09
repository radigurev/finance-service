using Finance.Common.Enums;
using Finance.Common.ErrorCodes;
using Finance.Common.Results;
using Finance.Country.Abstractions;
using Finance.Invoices.API.Consumers;
using Finance.Invoices.API.Interfaces;
using Finance.ServiceModel.Integration.Warehouse.Events;
using Finance.ServiceModel.Invoices;

namespace Finance.Invoices.API.Services;

/// <summary>
/// Default <see cref="IWarehouseInvoiceDraftFactory"/> (SDD-INT-WH-001 §2.1-§2.3). Contract-checks the
/// inbound event, dedupes on the source document, maps to an SDD-INV-001 <see cref="CreateInvoiceRequest"/>,
/// and delegates to <see cref="IInvoiceService.CreateDraftAsync"/> — the SAME create path the manual
/// endpoint uses. It never constructs an invoice directly and never confirms/posts. A missing per-line tax
/// rate defaults to the country's standard rate via <see cref="ICountryStrategy"/>. Business failures are
/// returned as permanent-failure outcomes; transient infrastructure failures propagate as exceptions.
/// </summary>
public sealed class WarehouseInvoiceDraftFactory : IWarehouseInvoiceDraftFactory
{
    private const int IsoCurrencyLength = 3;

    private readonly IInvoiceService _invoices;
    private readonly ICountryStrategy _country;

    /// <summary>Creates a new <see cref="WarehouseInvoiceDraftFactory"/>.</summary>
    /// <param name="invoices">The invoice application service (the shared create path).</param>
    /// <param name="country">The country strategy supplying the default tax rate (SDD-CTRY-001).</param>
    public WarehouseInvoiceDraftFactory(IInvoiceService invoices, ICountryStrategy country)
    {
        ArgumentNullException.ThrowIfNull(invoices);
        ArgumentNullException.ThrowIfNull(country);

        _invoices = invoices;
        _country = country;
    }

    /// <inheritdoc />
    public async Task<WarehouseDraftOutcome> CreateDraftAsync(
        IWarehouseDocumentEvent @event,
        InvoiceDocumentType documentType,
        string sourceDocumentType,
        Guid? correctsInvoiceId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(@event);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceDocumentType);

        WarehouseDraftOutcome? contractFailure = ValidateContract(@event);
        if (contractFailure is not null)
        {
            return contractFailure;
        }

        InvoiceDto? existing = await _invoices
            .FindBySourceDocumentAsync(sourceDocumentType, @event.SourceDocumentId, cancellationToken)
            .ConfigureAwait(false);
        if (existing is not null)
        {
            return WarehouseDraftOutcome.AlreadyExists(existing);
        }

        CreateInvoiceRequest request = BuildRequest(@event, documentType, sourceDocumentType, correctsInvoiceId);

        Result<InvoiceDto> created = await _invoices
            .CreateDraftAsync(request, allowEmptyLines: false, cancellationToken)
            .ConfigureAwait(false);

        return created.IsSuccess
            ? WarehouseDraftOutcome.Created(created.Value!)
            : WarehouseDraftOutcome.PermanentFailure(created.ErrorCode!, created.Detail);
    }

    private static WarehouseDraftOutcome? ValidateContract(IWarehouseDocumentEvent @event)
    {
        if (@event.SourceDocumentId == Guid.Empty)
        {
            return WarehouseDraftOutcome.PermanentFailure(
                InvoiceErrorCodes.INVOICE_NOT_FOUND, "The Warehouse event carried an empty SourceDocumentId.");
        }

        if (@event.CounterpartyId == Guid.Empty)
        {
            return WarehouseDraftOutcome.PermanentFailure(
                InvoiceErrorCodes.INVOICE_COUNTERPARTY_REQUIRED, "The Warehouse event carried an empty CounterpartyId.");
        }

        if (!IsValidCurrency(@event.CurrencyCode))
        {
            return WarehouseDraftOutcome.PermanentFailure(
                InvoiceErrorCodes.INVALID_INVOICE_CURRENCY, "The Warehouse event carried an invalid currency code.");
        }

        if (CountUsableLines(@event.Lines) == 0)
        {
            return WarehouseDraftOutcome.PermanentFailure(
                InvoiceErrorCodes.INVOICE_LINES_REQUIRED, "The Warehouse event carried no usable lines.");
        }

        return null;
    }

    private static bool IsValidCurrency(string? currencyCode)
    {
        return !string.IsNullOrWhiteSpace(currencyCode) && currencyCode.Trim().Length == IsoCurrencyLength;
    }

    private static int CountUsableLines(IReadOnlyList<WarehouseDocumentLine> lines)
    {
        int usable = 0;
        foreach (WarehouseDocumentLine line in lines)
        {
            if (line.Quantity > 0m && line.UnitPrice >= 0m)
            {
                usable++;
            }
        }

        return usable;
    }

    private CreateInvoiceRequest BuildRequest(
        IWarehouseDocumentEvent @event,
        InvoiceDocumentType documentType,
        string sourceDocumentType,
        Guid? correctsInvoiceId)
    {
        return new CreateInvoiceRequest
        {
            DocumentType = documentType,
            CounterpartyId = @event.CounterpartyId,
            CurrencyCode = @event.CurrencyCode,
            IssueDate = @event.OccurredAt,
            DueDate = @event.OccurredAt,
            Lines = MapLines(@event.Lines),
            CorrectsInvoiceId = correctsInvoiceId,
            SourceDocumentId = @event.SourceDocumentId,
            SourceDocumentType = sourceDocumentType,
            CorrelationId = @event.CorrelationId
        };
    }

    private IReadOnlyList<InvoiceLineRequest> MapLines(IReadOnlyList<WarehouseDocumentLine> lines)
    {
        List<InvoiceLineRequest> mapped = new(lines.Count);
        foreach (WarehouseDocumentLine line in lines)
        {
            if (line.Quantity <= 0m || line.UnitPrice < 0m)
            {
                continue;
            }

            mapped.Add(new InvoiceLineRequest
            {
                Description = ResolveDescription(line),
                Quantity = line.Quantity,
                UnitPrice = line.UnitPrice,
                TaxRate = line.TaxRate ?? _country.StandardTaxRate
            });
        }

        return mapped;
    }

    private static string ResolveDescription(WarehouseDocumentLine line)
    {
        return string.IsNullOrWhiteSpace(line.Description)
            ? $"Product {line.ProductId}"
            : line.Description;
    }
}
