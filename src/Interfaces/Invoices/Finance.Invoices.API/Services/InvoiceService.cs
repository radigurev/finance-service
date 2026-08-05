using System.Text.Json;
using AutoMapper;
using Finance.Common.Abstractions;
using Finance.Common.Enums;
using Finance.Common.ErrorCodes;
using Finance.Common.Results;
using Finance.Common.Workflow;
using Finance.Country.Abstractions;
using Finance.GenericFiltering.Models;
using Finance.Infrastructure.Audit.Interfaces;
using Finance.Infrastructure.Audit.Models;
using Finance.Infrastructure.Sequences.Interfaces;
using Finance.Infrastructure.Services;
using Finance.Invoices.API.Auditing;
using Finance.Invoices.API.Interfaces;
using Finance.Invoices.DBModel;
using Finance.Invoices.DBModel.Models;
using Finance.ServiceModel.Events.Invoices;
using Finance.ServiceModel.Invoices;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Finance.Invoices.API.Services;

/// <summary>
/// Default <see cref="IInvoiceService"/> built on <see cref="SearchableServiceBase{TEntity, TDto, TContext}"/>
/// (SDD-INV-001, SDD-INFRA-009). Confirm and cancel run through <see cref="IWorkflowEngine{TAggregate}"/>,
/// compute totals via <see cref="ICountryStrategy"/>, allocate a gapless country-formatted document number,
/// write an audit row, and publish a domain event via the transactional outbox — all inside one
/// transaction. Confirmed/posted invoices are immutable; corrections are made via credit/debit notes.
/// Invoices are never cached.
/// </summary>
public sealed class InvoiceService
    : SearchableServiceBase<Invoice, InvoiceDto, InvoicesDbContext>, IInvoiceService
{
    private const string IssueDateSortField = nameof(Invoice.IssueDate);
    private const decimal BaseCurrencyBookingRate = 1.000000m;

    private readonly IWorkflowEngine<Invoice> _workflow;
    private readonly ISequenceGenerator _sequence;
    private readonly ICountryStrategy _country;
    private readonly InvoiceTotalsCalculator _totals;
    private readonly IInvoicePeriodGuard _periodGuard;
    private readonly IAuditService _audit;
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly ICurrentUserAccessor _currentUser;

    /// <summary>Creates a new <see cref="InvoiceService"/>.</summary>
    /// <param name="db">The invoices database context.</param>
    /// <param name="mapper">The AutoMapper instance.</param>
    /// <param name="correlation">The ambient correlation-id accessor.</param>
    /// <param name="workflow">The invoice workflow engine (SDD-INFRA-008).</param>
    /// <param name="sequence">The gapless sequence generator (SDD-INFRA-003).</param>
    /// <param name="country">The country strategy for totals, tax rounding, and numbering (SDD-CTRY-001).</param>
    /// <param name="totals">The invoice totals calculator (SDD-INV-001 §2.8).</param>
    /// <param name="periodGuard">The fiscal-period guard seam (SDD-INV-001 §2.2).</param>
    /// <param name="audit">The write-path audit service (SDD-AUDIT-001).</param>
    /// <param name="publishEndpoint">The transactional-outbox publish endpoint (SDD-INFRA-006).</param>
    /// <param name="currentUser">The authenticated-user accessor.</param>
    public InvoiceService(
        InvoicesDbContext db,
        IMapper mapper,
        ICorrelationIdAccessor correlation,
        IWorkflowEngine<Invoice> workflow,
        ISequenceGenerator sequence,
        ICountryStrategy country,
        InvoiceTotalsCalculator totals,
        IInvoicePeriodGuard periodGuard,
        IAuditService audit,
        IPublishEndpoint publishEndpoint,
        ICurrentUserAccessor currentUser)
        : base(db, mapper, correlation)
    {
        _workflow = workflow;
        _sequence = sequence;
        _country = country;
        _totals = totals;
        _periodGuard = periodGuard;
        _audit = audit;
        _publishEndpoint = publishEndpoint;
        _currentUser = currentUser;
    }

    /// <inheritdoc cref="IInvoiceService.SearchAsync" />
    public override Task<Result<PagedResult<InvoiceDto>>> SearchAsync(
        FilterRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return base.SearchAsync(ApplyDefaultSort(request), cancellationToken);
    }

    /// <inheritdoc />
    public async Task<Result<InvoiceDto>> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        Invoice? invoice = await LoadWithLinesAsync(id, tracking: false, cancellationToken).ConfigureAwait(false);
        if (invoice is null)
        {
            return Result<InvoiceDto>.Failure(InvoiceErrorCodes.INVOICE_NOT_FOUND);
        }

        return Result<InvoiceDto>.Success(Mapper.Map<InvoiceDto>(invoice));
    }

    /// <inheritdoc />
    public async Task<InvoiceDto?> FindBySourceDocumentAsync(
        string sourceDocumentType,
        Guid sourceDocumentId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceDocumentType);

        Invoice? invoice = await Db.Invoices
            .AsNoTracking()
            .Include(invoice => invoice.Lines)
            .Where(invoice =>
                invoice.SourceDocumentType == sourceDocumentType
                && invoice.SourceDocumentId == sourceDocumentId)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return invoice is null ? null : Mapper.Map<InvoiceDto>(invoice);
    }

    /// <inheritdoc />
    public async Task<Result<InvoiceDto>> CreateDraftAsync(
        CreateInvoiceRequest request,
        bool allowEmptyLines,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!allowEmptyLines && request.Lines.Count == 0)
        {
            return Result<InvoiceDto>.Failure(InvoiceErrorCodes.INVOICE_LINES_REQUIRED);
        }

        Invoice invoice = BuildDraft(request);
        _totals.Recompute(invoice);

        Result reconciled = ReconcileTotals(invoice);
        if (!reconciled.IsSuccess)
        {
            return Result<InvoiceDto>.Failure(reconciled.ErrorCode!, reconciled.Detail);
        }

        return await PersistDraftAsync(invoice, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<Result<InvoiceDto>> UpdateDraftAsync(
        Guid id,
        UpdateInvoiceRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Invoice? invoice = await LoadWithLinesAsync(id, tracking: true, cancellationToken).ConfigureAwait(false);
        if (invoice is null)
        {
            return Result<InvoiceDto>.Failure(InvoiceErrorCodes.INVOICE_NOT_FOUND);
        }

        if (invoice.Status != InvoiceStatus.Draft)
        {
            return Result<InvoiceDto>.Failure(InvoiceErrorCodes.INVOICE_POSTED_IMMUTABLE);
        }

        Result tokenResult = ApplyConcurrencyToken(invoice, request.RowVersion);
        if (!tokenResult.IsSuccess)
        {
            return Result<InvoiceDto>.Failure(tokenResult.ErrorCode!, tokenResult.Detail);
        }

        return await PersistDraftUpdateAsync(invoice, request, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<Result> DeleteDraftAsync(Guid id, CancellationToken cancellationToken)
    {
        Invoice? invoice = await LoadWithLinesAsync(id, tracking: true, cancellationToken).ConfigureAwait(false);
        if (invoice is null)
        {
            return Result.Failure(InvoiceErrorCodes.INVOICE_NOT_FOUND);
        }

        if (invoice.Status != InvoiceStatus.Draft)
        {
            return Result.Failure(InvoiceErrorCodes.INVOICE_POSTED_IMMUTABLE);
        }

        return await PersistDraftDeleteAsync(invoice, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<Result<InvoiceDto>> ConfirmAsync(
        Guid id,
        ConfirmInvoiceRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Invoice? invoice = await LoadWithLinesAsync(id, tracking: true, cancellationToken).ConfigureAwait(false);
        if (invoice is null)
        {
            return Result<InvoiceDto>.Failure(InvoiceErrorCodes.INVOICE_NOT_FOUND);
        }

        if (invoice.Status != InvoiceStatus.Draft)
        {
            return Result<InvoiceDto>.Failure(InvoiceErrorCodes.INVOICE_NOT_DRAFT);
        }

        if (invoice.DocumentNumber is not null)
        {
            return Result<InvoiceDto>.Failure(InvoiceErrorCodes.INVOICE_DUPLICATE_DOCUMENT_NUMBER);
        }

        Result tokenResult = ApplyConcurrencyToken(invoice, request.RowVersion);
        if (!tokenResult.IsSuccess)
        {
            return Result<InvoiceDto>.Failure(tokenResult.ErrorCode!, tokenResult.Detail);
        }

        Result guardResult = await RunConfirmGuardsAsync(invoice, cancellationToken).ConfigureAwait(false);
        if (!guardResult.IsSuccess)
        {
            return Result<InvoiceDto>.Failure(guardResult.ErrorCode!, guardResult.Detail);
        }

        return await ConfirmInTransactionAsync(invoice, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<Result<InvoiceDto>> PostAsync(
        Guid id,
        PostInvoiceRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Invoice? invoice = await LoadWithLinesAsync(id, tracking: true, cancellationToken).ConfigureAwait(false);
        if (invoice is null)
        {
            return Result<InvoiceDto>.Failure(InvoiceErrorCodes.INVOICE_NOT_FOUND);
        }

        if (invoice.Status == InvoiceStatus.Posted)
        {
            return Result<InvoiceDto>.Success(Mapper.Map<InvoiceDto>(invoice));
        }

        if (invoice.Status != InvoiceStatus.Confirmed || invoice.JournalEntryId is null)
        {
            return Result<InvoiceDto>.Failure(InvoiceErrorCodes.INVOICE_NOT_CONFIRMED);
        }

        Result tokenResult = ApplyConcurrencyToken(invoice, request.RowVersion);
        if (!tokenResult.IsSuccess)
        {
            return Result<InvoiceDto>.Failure(tokenResult.ErrorCode!, tokenResult.Detail);
        }

        Result posted = await TransitionToPostedAsync(invoice, cancellationToken).ConfigureAwait(false);
        if (!posted.IsSuccess)
        {
            return Result<InvoiceDto>.Failure(posted.ErrorCode!, posted.Detail);
        }

        return Result<InvoiceDto>.Success(Mapper.Map<InvoiceDto>(invoice));
    }

    /// <inheritdoc />
    public async Task<Result<InvoiceDto>> CancelAsync(
        Guid id,
        CancelInvoiceRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            return Result<InvoiceDto>.Failure(InvoiceErrorCodes.INVOICE_CANCEL_REASON_REQUIRED);
        }

        Invoice? invoice = await LoadWithLinesAsync(id, tracking: true, cancellationToken).ConfigureAwait(false);
        if (invoice is null)
        {
            return Result<InvoiceDto>.Failure(InvoiceErrorCodes.INVOICE_NOT_FOUND);
        }

        if (invoice.Status is not (InvoiceStatus.Draft or InvoiceStatus.Confirmed))
        {
            return Result<InvoiceDto>.Failure(InvoiceErrorCodes.INVALID_INVOICE_STATE_TRANSITION);
        }

        Result tokenResult = ApplyConcurrencyToken(invoice, request.RowVersion);
        if (!tokenResult.IsSuccess)
        {
            return Result<InvoiceDto>.Failure(tokenResult.ErrorCode!, tokenResult.Detail);
        }

        return await CancelInTransactionAsync(invoice, request.Reason, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<Result<InvoiceDto>> MarkReversedAsync(
        InvoiceReversalRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result validated = ValidateReversal(request);
        if (!validated.IsSuccess)
        {
            return Result<InvoiceDto>.Failure(validated.ErrorCode!, validated.Detail);
        }

        Invoice? invoice = await LoadWithLinesAsync(request.InvoiceId, tracking: true, cancellationToken)
            .ConfigureAwait(false);
        if (invoice is null)
        {
            return Result<InvoiceDto>.Failure(InvoiceErrorCodes.INVOICE_NOT_FOUND);
        }

        return await ReverseInTransactionAsync(invoice, request, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<Result> LinkPostedJournalEntryAsync(
        Guid invoiceId,
        Guid journalEntryId,
        CancellationToken cancellationToken)
    {
        Invoice? invoice = await LoadWithLinesAsync(invoiceId, tracking: true, cancellationToken).ConfigureAwait(false);
        if (invoice is null)
        {
            return Result.Failure(InvoiceErrorCodes.INVOICE_NOT_FOUND);
        }

        if (invoice.Status == InvoiceStatus.Posted)
        {
            return Result.Success();
        }

        if (invoice.Status != InvoiceStatus.Confirmed)
        {
            return Result.Failure(InvoiceErrorCodes.INVOICE_NOT_CONFIRMED);
        }

        invoice.JournalEntryId = journalEntryId;
        return await TransitionToPostedAsync(invoice, cancellationToken).ConfigureAwait(false);
    }

    private async Task<Result> RunConfirmGuardsAsync(Invoice invoice, CancellationToken cancellationToken)
    {
        if (invoice.Lines.Count == 0)
        {
            return Result.Failure(InvoiceErrorCodes.INVOICE_LINES_REQUIRED);
        }

        _totals.Recompute(invoice);

        Result reconciled = ReconcileTotals(invoice);
        if (!reconciled.IsSuccess)
        {
            return reconciled;
        }

        return await _periodGuard.EnsureOpenAsync(invoice.IssueDate, cancellationToken).ConfigureAwait(false);
    }

    private Invoice BuildDraft(CreateInvoiceRequest request)
    {
        Guid userId = _currentUser.GetUserId();
        string correlationId = ResolveCorrelationId(request.CorrelationId);
        DateTimeOffset now = DateTimeOffset.UtcNow;

        Invoice invoice = new()
        {
            DocumentType = request.DocumentType,
            Direction = InvoiceDocumentTypeMap.DirectionFor(request.DocumentType),
            Status = InvoiceStatus.Draft,
            CounterpartyId = request.CounterpartyId,
            CurrencyCode = request.CurrencyCode,
            BaseCurrencyCode = _country.BaseCurrencyCode,
            ExchangeRate = ResolveBookingRate(request.CurrencyCode, _country.BaseCurrencyCode, request.ExchangeRate),
            IssueDate = request.IssueDate,
            DueDate = request.DueDate,
            CorrectsInvoiceId = request.CorrectsInvoiceId,
            SourceDocumentId = request.SourceDocumentId,
            SourceDocumentType = request.SourceDocumentType,
            CorrelationId = correlationId,
            CreatedAt = now,
            CreatedBy = userId,
            Lines = MapLines(request.Lines)
        };

        return invoice;
    }

    /// <summary>
    /// Resolves the booking rate FROZEN on the document at creation (SDD-INV-001 §2.14): <c>1.000000</c>
    /// whenever the transactional currency equals the base currency, otherwise the caller-supplied rate
    /// (automatic resolution from a rate table is deferred to SDD-FIN-005). It is the only source of
    /// <c>InvoiceConfirmedEvent.BookingExchangeRate</c>, so it is never fabricated for a non-base-currency
    /// document.
    /// </summary>
    /// <param name="currencyCode">The invoice transactional currency.</param>
    /// <param name="baseCurrencyCode">The base currency resolved from the country strategy.</param>
    /// <param name="requestedRate">The caller-supplied rate, or <c>null</c> when omitted.</param>
    /// <returns>The rate to freeze on the invoice.</returns>
    private static decimal ResolveBookingRate(
        string currencyCode,
        string baseCurrencyCode,
        decimal? requestedRate)
    {
        if (string.Equals(currencyCode, baseCurrencyCode, StringComparison.OrdinalIgnoreCase))
        {
            return BaseCurrencyBookingRate;
        }

        return requestedRate is decimal rate && rate > 0m ? rate : BaseCurrencyBookingRate;
    }

    private string ResolveCorrelationId(string? requestedCorrelationId)
    {
        return string.IsNullOrWhiteSpace(requestedCorrelationId)
            ? Correlation.Get()
            : requestedCorrelationId;
    }

    private static List<InvoiceLine> MapLines(IReadOnlyList<InvoiceLineRequest> requests)
    {
        List<InvoiceLine> lines = new(requests.Count);
        int lineNumber = 1;
        foreach (InvoiceLineRequest request in requests)
        {
            lines.Add(new InvoiceLine
            {
                LineNumber = lineNumber++,
                Description = request.Description,
                Quantity = request.Quantity,
                UnitPrice = request.UnitPrice,
                TaxRate = request.TaxRate
            });
        }

        return lines;
    }

    private static Result ReconcileTotals(Invoice invoice)
    {
        decimal lineNet = invoice.Lines.Sum(line => line.LineNet);
        decimal lineTax = invoice.Lines.Sum(line => line.LineTax);
        decimal lineGross = invoice.Lines.Sum(line => line.LineGross);

        bool headerMatches = invoice.NetTotal == lineNet
            && invoice.TaxTotal == lineTax
            && invoice.GrossTotal == lineGross;
        bool grossBalances = invoice.GrossTotal == invoice.NetTotal + invoice.TaxTotal;

        if (!headerMatches || !grossBalances)
        {
            return Result.Failure(InvoiceErrorCodes.INVOICE_TOTALS_MISMATCH);
        }

        return Result.Success();
    }

    private async Task<Result<InvoiceDto>> PersistDraftAsync(Invoice invoice, CancellationToken cancellationToken)
    {
        await using IDbContextTransaction transaction =
            await Db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        Db.Invoices.Add(invoice);
        Result inserted = await SaveWithConcurrencyCheckAsync(cancellationToken).ConfigureAwait(false);
        if (!inserted.IsSuccess)
        {
            return Result<InvoiceDto>.Failure(inserted.ErrorCode!, inserted.Detail);
        }

        Result audited = await RecordAuditAsync(
            InvoiceAuditEventTypes.InvoiceCreated,
            AuditOperation.Create,
            invoice,
            beforeJson: null,
            SerializeInvoice(invoice),
            reason: null,
            cancellationToken).ConfigureAwait(false);
        if (!audited.IsSuccess)
        {
            return Result<InvoiceDto>.Failure(audited.ErrorCode!, audited.Detail);
        }

        Result flushed = await SaveWithConcurrencyCheckAsync(cancellationToken).ConfigureAwait(false);
        if (!flushed.IsSuccess)
        {
            return Result<InvoiceDto>.Failure(flushed.ErrorCode!, flushed.Detail);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return Result<InvoiceDto>.Success(Mapper.Map<InvoiceDto>(invoice));
    }

    private async Task<Result<InvoiceDto>> PersistDraftUpdateAsync(
        Invoice invoice,
        UpdateInvoiceRequest request,
        CancellationToken cancellationToken)
    {
        await using IDbContextTransaction transaction =
            await Db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        string beforeJson = SerializeInvoice(invoice);

        invoice.CounterpartyId = request.CounterpartyId;
        invoice.CurrencyCode = request.CurrencyCode;
        invoice.IssueDate = request.IssueDate;
        invoice.DueDate = request.DueDate;
        Db.InvoiceLines.RemoveRange(invoice.Lines);
        invoice.Lines = MapLines(request.Lines);
        _totals.Recompute(invoice);

        Result reconciled = ReconcileTotals(invoice);
        if (!reconciled.IsSuccess)
        {
            return Result<InvoiceDto>.Failure(reconciled.ErrorCode!, reconciled.Detail);
        }

        Result audited = await RecordAuditAsync(
            InvoiceAuditEventTypes.InvoiceUpdated,
            AuditOperation.Update,
            invoice,
            beforeJson,
            SerializeInvoice(invoice),
            reason: null,
            cancellationToken).ConfigureAwait(false);
        if (!audited.IsSuccess)
        {
            return Result<InvoiceDto>.Failure(audited.ErrorCode!, audited.Detail);
        }

        Result saved = await SaveWithConcurrencyCheckAsync(cancellationToken).ConfigureAwait(false);
        if (!saved.IsSuccess)
        {
            return Result<InvoiceDto>.Failure(saved.ErrorCode!, saved.Detail);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return Result<InvoiceDto>.Success(Mapper.Map<InvoiceDto>(invoice));
    }

    private async Task<Result> PersistDraftDeleteAsync(Invoice invoice, CancellationToken cancellationToken)
    {
        await using IDbContextTransaction transaction =
            await Db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        string beforeJson = SerializeInvoice(invoice);

        Result audited = await RecordAuditAsync(
            InvoiceAuditEventTypes.InvoiceDeleted,
            AuditOperation.Delete,
            invoice,
            beforeJson,
            afterJson: "{\"deleted\":true}",
            reason: null,
            cancellationToken).ConfigureAwait(false);
        if (!audited.IsSuccess)
        {
            return Result.Failure(audited.ErrorCode!, audited.Detail);
        }

        Db.Invoices.Remove(invoice);

        Result saved = await SaveWithConcurrencyCheckAsync(cancellationToken).ConfigureAwait(false);
        if (!saved.IsSuccess)
        {
            return saved;
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }

    private async Task<Result<InvoiceDto>> ConfirmInTransactionAsync(
        Invoice invoice,
        CancellationToken cancellationToken)
    {
        await using IDbContextTransaction transaction =
            await Db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        string beforeJson = SerializeInvoice(invoice);

        Result transition = await TransitionAsync(
            invoice, InvoiceStatus.Confirmed, reason: null, cancellationToken).ConfigureAwait(false);
        if (!transition.IsSuccess)
        {
            return Result<InvoiceDto>.Failure(transition.ErrorCode!, transition.Detail);
        }

        await AssignDocumentNumberAsync(invoice, cancellationToken).ConfigureAwait(false);
        StampConfirmed(invoice);
        AppendStatusHistory(invoice, InvoiceStatus.Draft, InvoiceStatus.Confirmed, reason: null);

        Result audited = await RecordAuditAsync(
            InvoiceAuditEventTypes.InvoiceConfirmed,
            AuditOperation.StateChange,
            invoice,
            beforeJson,
            SerializeInvoice(invoice),
            reason: null,
            cancellationToken).ConfigureAwait(false);
        if (!audited.IsSuccess)
        {
            return Result<InvoiceDto>.Failure(audited.ErrorCode!, audited.Detail);
        }

        await _publishEndpoint.Publish(BuildConfirmedEvent(invoice), cancellationToken).ConfigureAwait(false);

        Result saved = await SaveWithConcurrencyCheckAsync(cancellationToken).ConfigureAwait(false);
        if (!saved.IsSuccess)
        {
            return Result<InvoiceDto>.Failure(saved.ErrorCode!, saved.Detail);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return Result<InvoiceDto>.Success(Mapper.Map<InvoiceDto>(invoice));
    }

    private async Task<Result<InvoiceDto>> CancelInTransactionAsync(
        Invoice invoice,
        string reason,
        CancellationToken cancellationToken)
    {
        await using IDbContextTransaction transaction =
            await Db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        InvoiceStatus fromStatus = invoice.Status;
        string beforeJson = SerializeInvoice(invoice);

        Result transition = await TransitionAsync(
            invoice, InvoiceStatus.Cancelled, reason, cancellationToken).ConfigureAwait(false);
        if (!transition.IsSuccess)
        {
            return Result<InvoiceDto>.Failure(transition.ErrorCode!, transition.Detail);
        }

        AppendStatusHistory(invoice, fromStatus, InvoiceStatus.Cancelled, reason);

        Result audited = await RecordAuditAsync(
            InvoiceAuditEventTypes.InvoiceCancelled,
            AuditOperation.StateChange,
            invoice,
            beforeJson,
            SerializeInvoice(invoice),
            reason,
            cancellationToken).ConfigureAwait(false);
        if (!audited.IsSuccess)
        {
            return Result<InvoiceDto>.Failure(audited.ErrorCode!, audited.Detail);
        }

        await _publishEndpoint.Publish(BuildCancelledEvent(invoice, reason), cancellationToken).ConfigureAwait(false);

        Result committed = await CommitAsync(transaction, cancellationToken).ConfigureAwait(false);
        if (!committed.IsSuccess)
        {
            return Result<InvoiceDto>.Failure(committed.ErrorCode!, committed.Detail);
        }

        return Result<InvoiceDto>.Success(Mapper.Map<InvoiceDto>(invoice));
    }

    /// <summary>
    /// Shape-validates the reversal input (SDD-INV-001 §2.7): the correcting note must be identified and the
    /// reason must be non-empty, because both are recorded on the audit row and carried on the published event.
    /// The state legality of <c>Posted → Reversed</c> is left to the workflow engine, which surfaces
    /// <c>INVALID_INVOICE_STATE_TRANSITION</c>.
    /// </summary>
    /// <param name="request">The reversal input.</param>
    /// <returns>A success result, or a validation failure.</returns>
    private static Result ValidateReversal(InvoiceReversalRequest request)
    {
        if (request.CorrectingInvoiceId == Guid.Empty)
        {
            return Result.Failure(
                CommonErrorCodes.VALIDATION_FAILED,
                "A reversal must identify the correcting credit/debit note.");
        }

        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            return Result.Failure(
                CommonErrorCodes.VALIDATION_FAILED,
                "A reversal must carry a non-empty reason.");
        }

        return Result.Success();
    }

    private async Task<Result<InvoiceDto>> ReverseInTransactionAsync(
        Invoice invoice,
        InvoiceReversalRequest request,
        CancellationToken cancellationToken)
    {
        await using IDbContextTransaction transaction =
            await Db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        InvoiceStatus fromStatus = invoice.Status;
        string beforeJson = SerializeInvoice(invoice);

        Result transition = await TransitionAsync(
            invoice, InvoiceStatus.Reversed, request.Reason, cancellationToken).ConfigureAwait(false);
        if (!transition.IsSuccess)
        {
            return Result<InvoiceDto>.Failure(transition.ErrorCode!, transition.Detail);
        }

        AppendStatusHistory(invoice, fromStatus, InvoiceStatus.Reversed, request.Reason);

        Result audited = await RecordAuditAsync(
            InvoiceAuditEventTypes.InvoiceReversed,
            AuditOperation.StateChange,
            invoice,
            beforeJson,
            SerializeReversal(invoice, request),
            request.Reason,
            cancellationToken).ConfigureAwait(false);
        if (!audited.IsSuccess)
        {
            return Result<InvoiceDto>.Failure(audited.ErrorCode!, audited.Detail);
        }

        await _publishEndpoint.Publish(BuildReversedEvent(invoice, request), cancellationToken)
            .ConfigureAwait(false);

        Result committed = await CommitAsync(transaction, cancellationToken).ConfigureAwait(false);
        if (!committed.IsSuccess)
        {
            return Result<InvoiceDto>.Failure(committed.ErrorCode!, committed.Detail);
        }

        return Result<InvoiceDto>.Success(Mapper.Map<InvoiceDto>(invoice));
    }

    private async Task<Result> TransitionToPostedAsync(Invoice invoice, CancellationToken cancellationToken)
    {
        await using IDbContextTransaction transaction =
            await Db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        string beforeJson = SerializeInvoice(invoice);

        Result transition = await TransitionAsync(
            invoice, InvoiceStatus.Posted, reason: null, cancellationToken).ConfigureAwait(false);
        if (!transition.IsSuccess)
        {
            return transition;
        }

        invoice.PostedAt = DateTimeOffset.UtcNow;
        AppendStatusHistory(invoice, InvoiceStatus.Confirmed, InvoiceStatus.Posted, reason: null);

        Result audited = await RecordAuditAsync(
            InvoiceAuditEventTypes.InvoicePosted,
            AuditOperation.StateChange,
            invoice,
            beforeJson,
            SerializeInvoice(invoice),
            reason: null,
            cancellationToken).ConfigureAwait(false);
        if (!audited.IsSuccess)
        {
            return audited;
        }

        return await CommitAsync(transaction, cancellationToken).ConfigureAwait(false);
    }

    private async Task<Result> CommitAsync(IDbContextTransaction transaction, CancellationToken cancellationToken)
    {
        Result saved = await SaveWithConcurrencyCheckAsync(cancellationToken).ConfigureAwait(false);
        if (!saved.IsSuccess)
        {
            return saved;
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }

    private async Task AssignDocumentNumberAsync(Invoice invoice, CancellationToken cancellationToken)
    {
        string sequenceKey = InvoiceDocumentTypeMap.SequenceKeyFor(invoice.DocumentType);
        long sequenceValue = await _sequence.NextValueAsync(sequenceKey, cancellationToken).ConfigureAwait(false);
        invoice.DocumentNumber = _country.GenerateDocumentNumber(invoice.DocumentType, sequenceValue);
    }

    private void StampConfirmed(Invoice invoice)
    {
        invoice.ConfirmedAt = DateTimeOffset.UtcNow;
        invoice.ConfirmedBy = _currentUser.GetUserId();
        invoice.Status = InvoiceStatus.Confirmed;
    }

    private async Task<Result> TransitionAsync(
        Invoice invoice,
        InvoiceStatus target,
        string? reason,
        CancellationToken cancellationToken)
    {
        WorkflowContext<Invoice> context = new()
        {
            Aggregate = invoice,
            CurrentState = invoice.Status.ToString(),
            TargetState = target.ToString(),
            Reason = reason,
            CorrelationId = Correlation.Get()
        };

        Result transition = await _workflow.TransitionAsync(context, cancellationToken).ConfigureAwait(false);
        if (!transition.IsSuccess)
        {
            return Result.Failure(TranslateTransitionCode(transition), transition.Detail);
        }

        invoice.Status = target;
        return Result.Success();
    }

    private static string TranslateTransitionCode(Result transition)
    {
        if (transition.ErrorCode == WorkflowErrorCodes.WORKFLOW_GUARD_FAILED && transition.Detail is not null)
        {
            return transition.Detail;
        }

        if (transition.ErrorCode == WorkflowErrorCodes.INVALID_STATE_TRANSITION ||
            transition.ErrorCode == WorkflowErrorCodes.STATE_NOT_REGISTERED)
        {
            return InvoiceErrorCodes.INVALID_INVOICE_STATE_TRANSITION;
        }

        return transition.ErrorCode!;
    }

    private void AppendStatusHistory(
        Invoice invoice,
        InvoiceStatus fromStatus,
        InvoiceStatus toStatus,
        string? reason)
    {
        invoice.StatusHistory.Add(new InvoiceStatusHistory
        {
            FromStatus = fromStatus.ToString(),
            ToStatus = toStatus.ToString(),
            ChangedBy = _currentUser.GetUserId(),
            ChangedAt = DateTimeOffset.UtcNow,
            CorrelationId = Correlation.Get(),
            Reason = reason
        });
    }

    private Task<Result> RecordAuditAsync(
        string eventType,
        AuditOperation operation,
        Invoice invoice,
        string? beforeJson,
        string afterJson,
        string? reason,
        CancellationToken cancellationToken)
    {
        AuditEntry audit = new()
        {
            EventType = eventType,
            Operation = operation,
            EntityType = InvoiceAuditEventTypes.EntityType,
            EntityId = invoice.Id.ToString(),
            UserId = _currentUser.GetUserId(),
            Username = _currentUser.GetUsername(),
            OccurredAt = DateTimeOffset.UtcNow,
            CorrelationId = Correlation.Get(),
            BeforeJson = beforeJson,
            AfterJson = afterJson,
            Reason = reason
        };

        return _audit.RecordAsync(audit, cancellationToken);
    }

    private InvoiceConfirmedEvent BuildConfirmedEvent(Invoice invoice) => new()
    {
        MessageId = Guid.NewGuid(),
        CorrelationId = Correlation.Get(),
        OccurredAt = DateTimeOffset.UtcNow,
        InvoiceId = invoice.Id,
        DocumentNumber = invoice.DocumentNumber!,
        DocumentType = invoice.DocumentType,
        Direction = invoice.Direction,
        CounterpartyId = invoice.CounterpartyId,
        CurrencyCode = invoice.CurrencyCode,
        BaseCurrencyCode = invoice.BaseCurrencyCode,
        IssueDate = invoice.IssueDate,
        PostingRuleKey = InvoiceDocumentTypeMap.PostingRuleKeyFor(invoice.DocumentType),
        NetTotal = invoice.NetTotal,
        TaxTotal = invoice.TaxTotal,
        GrossTotal = invoice.GrossTotal,
        DueDate = invoice.DueDate,
        BookingExchangeRate = invoice.ExchangeRate
    };

    /// <summary>
    /// Builds the reversal event published from the <c>Posted → Reversed</c> transition (SDD-INV-001
    /// §2.7/§2.11). <c>DocumentNumber</c> is dereferenced unconditionally because <c>Reversed</c> is reachable
    /// only from <c>Posted</c> and every posted invoice was numbered at confirm.
    /// </summary>
    /// <param name="invoice">The reversed original invoice.</param>
    /// <param name="request">The reversal input carrying the correcting note and the reason.</param>
    /// <returns>The event to enqueue to the transactional outbox.</returns>
    private InvoiceReversedEvent BuildReversedEvent(Invoice invoice, InvoiceReversalRequest request) => new()
    {
        MessageId = Guid.NewGuid(),
        CorrelationId = Correlation.Get(),
        OccurredAt = DateTimeOffset.UtcNow,
        InvoiceId = invoice.Id,
        DocumentNumber = invoice.DocumentNumber!,
        CorrectingInvoiceId = request.CorrectingInvoiceId,
        Reason = request.Reason
    };

    private InvoiceCancelledEvent BuildCancelledEvent(Invoice invoice, string reason) => new()
    {
        MessageId = Guid.NewGuid(),
        CorrelationId = Correlation.Get(),
        OccurredAt = DateTimeOffset.UtcNow,
        InvoiceId = invoice.Id,
        DocumentNumber = invoice.DocumentNumber,
        Reason = reason
    };

    private Task<Invoice?> LoadWithLinesAsync(Guid id, bool tracking, CancellationToken cancellationToken)
    {
        IQueryable<Invoice> query = Db.Invoices
            .Include(invoice => invoice.Lines)
            .Where(invoice => invoice.Id == id);

        if (!tracking)
        {
            query = query.AsNoTracking();
        }

        return query.FirstOrDefaultAsync(cancellationToken);
    }

    private Result ApplyConcurrencyToken(Invoice invoice, string rowVersion)
    {
        if (!TryDecodeRowVersion(rowVersion, out byte[] originalRowVersion))
        {
            return Result.Failure(
                CommonErrorCodes.CONCURRENT_MODIFICATION,
                "The supplied row version is not a valid base64 token.");
        }

        Db.Entry(invoice).Property(e => e.RowVersion).OriginalValue = originalRowVersion;
        return Result.Success();
    }

    private static bool TryDecodeRowVersion(string rowVersion, out byte[] decoded)
    {
        if (string.IsNullOrWhiteSpace(rowVersion))
        {
            decoded = [];
            return false;
        }

        try
        {
            decoded = Convert.FromBase64String(rowVersion);
            return true;
        }
        catch (FormatException)
        {
            decoded = [];
            return false;
        }
    }

    private static string SerializeInvoice(Invoice invoice)
    {
        return JsonSerializer.Serialize(BuildSnapshot(invoice));
    }

    /// <summary>
    /// Builds the audit snapshot projection of the invoice, including the frozen booking rate and the settlement
    /// figures the document carries at that moment (SDD-INV-001 §2.14 — a terminal transition carries them
    /// forward and rewrites no history).
    /// </summary>
    /// <param name="invoice">The invoice to project.</param>
    /// <returns>The snapshot projection serialized by the audit writers.</returns>
    private static object BuildSnapshot(Invoice invoice)
    {
        return new
        {
            invoice.Id,
            invoice.DocumentNumber,
            DocumentType = invoice.DocumentType.ToString(),
            Direction = invoice.Direction.ToString(),
            Status = invoice.Status.ToString(),
            invoice.CounterpartyId,
            invoice.CurrencyCode,
            invoice.BaseCurrencyCode,
            invoice.ExchangeRate,
            invoice.IssueDate,
            invoice.DueDate,
            invoice.NetTotal,
            invoice.TaxTotal,
            invoice.GrossTotal,
            invoice.SettledAmount,
            SettlementStatus = invoice.SettlementStatus.ToString(),
            invoice.CorrectsInvoiceId,
            invoice.JournalEntryId,
            invoice.SourceDocumentId,
            invoice.SourceDocumentType,
            invoice.PostedAt,
            Lines = invoice.Lines.OrderBy(line => line.LineNumber).Select(line => new
            {
                line.LineNumber,
                line.Description,
                line.Quantity,
                line.UnitPrice,
                line.TaxRate,
                line.LineNet,
                line.LineTax,
                line.LineGross
            })
        };
    }

    /// <summary>
    /// Serializes the post-reversal audit snapshot: the invoice as reversed, plus the linking note and reason
    /// that justified it (SDD-INV-001 §2.7). The original's own <c>CorrectsInvoiceId</c> is untouched — the link
    /// belongs to the note — so the note id is recorded here rather than on the row.
    /// </summary>
    /// <param name="invoice">The reversed original invoice.</param>
    /// <param name="request">The reversal input.</param>
    /// <returns>The audit snapshot as JSON.</returns>
    private static string SerializeReversal(Invoice invoice, InvoiceReversalRequest request)
    {
        return JsonSerializer.Serialize(new
        {
            Invoice = BuildSnapshot(invoice),
            request.CorrectingInvoiceId,
            request.Reason
        });
    }

    private static FilterRequest ApplyDefaultSort(FilterRequest request)
    {
        if (request.Sort.Count > 0)
        {
            return request;
        }

        return request with
        {
            Sort = [new SortCriterion { Field = IssueDateSortField, Direction = "desc" }]
        };
    }
}
