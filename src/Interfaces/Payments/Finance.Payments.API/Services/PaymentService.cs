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
using Finance.Payments.API.Auditing;
using Finance.Payments.API.Interfaces;
using Finance.Payments.DBModel;
using Finance.Payments.DBModel.Models;
using Finance.ServiceModel.Events.Payments;
using Finance.ServiceModel.Payments;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Finance.Payments.API.Services;

/// <summary>
/// Default <see cref="IPaymentService"/> built on <see cref="SearchableServiceBase{TEntity, TDto, TContext}"/>
/// (SDD-PAY-001, SDD-INFRA-009). Confirm, cancel, reverse, and the back-event post link all run through
/// <see cref="IWorkflowEngine{TAggregate}"/>, compute the base amount via <see cref="ICountryStrategy"/>,
/// allocate a gapless country-formatted document number at confirm only, write an audit row BEFORE the outbox
/// row, and publish a domain event through the transactional outbox — all inside one transaction. Confirmed and
/// later payments are immutable; a posted payment is corrected only by reversal. Payments are transactional
/// data and are NEVER cached (SDD-INFRA-004).
/// <para>Because <c>BaseEntityService.FindOrNotFoundAsync</c> is <c>int</c>-keyed while
/// <see cref="Payment.Id"/> is a GUID, the GUID loader is hand-rolled here (SDD-PAY-001 §7).</para>
/// </summary>
public sealed class PaymentService
    : SearchableServiceBase<Payment, PaymentDto, PaymentsDbContext>, IPaymentService
{
    private const string PaymentDateSortField = nameof(Payment.PaymentDate);
    private const string DeletedSnapshot = "{\"deleted\":true}";

    private readonly IWorkflowEngine<Payment> _workflow;
    private readonly ISequenceGenerator _sequence;
    private readonly ICountryStrategy _country;
    private readonly PaymentAmountCalculator _amounts;
    private readonly IPaymentPeriodGuard _periodGuard;
    private readonly ISettlementAccountReader _settlementAccounts;
    private readonly IAuditService _audit;
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly TimeProvider _timeProvider;

    /// <summary>Creates a new <see cref="PaymentService"/>.</summary>
    /// <param name="db">The payments database context.</param>
    /// <param name="mapper">The AutoMapper instance.</param>
    /// <param name="correlation">The ambient correlation-id accessor.</param>
    /// <param name="workflow">The payment workflow engine (SDD-INFRA-008).</param>
    /// <param name="sequence">The gapless sequence generator (SDD-INFRA-003).</param>
    /// <param name="country">The country strategy for rounding and numbering (SDD-CTRY-001).</param>
    /// <param name="amounts">The payment amount/FX calculator (SDD-PAY-001 §2.8).</param>
    /// <param name="periodGuard">The fiscal-period guard seam (SDD-PAY-001 §2.9).</param>
    /// <param name="settlementAccounts">The settlement-account read seam (SDD-PAY-001 §2.8).</param>
    /// <param name="audit">The write-path audit service (SDD-AUDIT-001).</param>
    /// <param name="publishEndpoint">The transactional-outbox publish endpoint (SDD-INFRA-006).</param>
    /// <param name="currentUser">The authenticated-user accessor.</param>
    /// <param name="timeProvider">The clock the confirm-year guard and lifecycle stamps read.</param>
    public PaymentService(
        PaymentsDbContext db,
        IMapper mapper,
        ICorrelationIdAccessor correlation,
        IWorkflowEngine<Payment> workflow,
        ISequenceGenerator sequence,
        ICountryStrategy country,
        PaymentAmountCalculator amounts,
        IPaymentPeriodGuard periodGuard,
        ISettlementAccountReader settlementAccounts,
        IAuditService audit,
        IPublishEndpoint publishEndpoint,
        ICurrentUserAccessor currentUser,
        TimeProvider timeProvider)
        : base(db, mapper, correlation)
    {
        _workflow = workflow;
        _sequence = sequence;
        _country = country;
        _amounts = amounts;
        _periodGuard = periodGuard;
        _settlementAccounts = settlementAccounts;
        _audit = audit;
        _publishEndpoint = publishEndpoint;
        _currentUser = currentUser;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc cref="IPaymentService.SearchAsync" />
    public override Task<Result<PagedResult<PaymentDto>>> SearchAsync(
        FilterRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return base.SearchAsync(ApplyDefaultSort(request), cancellationToken);
    }

    /// <inheritdoc />
    public async Task<Result<PaymentDto>> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        Payment? payment = await LoadAsync(id, tracking: false, cancellationToken).ConfigureAwait(false);
        if (payment is null)
        {
            return Result<PaymentDto>.Failure(PaymentErrorCodes.PAYMENT_NOT_FOUND);
        }

        return Result<PaymentDto>.Success(Mapper.Map<PaymentDto>(payment));
    }

    /// <inheritdoc />
    public async Task<Result<PaymentDto>> CreateDraftAsync(
        CreatePaymentRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!PaymentDocumentTypeMap.IsSupported(request.DocumentType))
        {
            return Result<PaymentDto>.Failure(
                PaymentErrorCodes.INVALID_PAYMENT_DOCUMENT_TYPE,
                $"'{request.DocumentType}' is not a supported payment document type.");
        }

        Payment payment = BuildDraft(request);
        _amounts.Recompute(payment);

        Result validated = await ValidateDraftAsync(payment, cancellationToken).ConfigureAwait(false);
        if (!validated.IsSuccess)
        {
            return Result<PaymentDto>.Failure(validated.ErrorCode!, validated.Detail);
        }

        return await PersistDraftAsync(payment, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<Result<PaymentDto>> UpdateDraftAsync(
        Guid id,
        UpdatePaymentRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Payment? payment = await LoadAsync(id, tracking: true, cancellationToken).ConfigureAwait(false);
        if (payment is null)
        {
            return Result<PaymentDto>.Failure(PaymentErrorCodes.PAYMENT_NOT_FOUND);
        }

        if (payment.Status != PaymentStatus.Draft)
        {
            return Result<PaymentDto>.Failure(PaymentErrorCodes.PAYMENT_POSTED_IMMUTABLE);
        }

        if (payment.DocumentType != request.DocumentType)
        {
            return Result<PaymentDto>.Failure(
                PaymentErrorCodes.INVALID_PAYMENT_DOCUMENT_TYPE,
                "The document type of a payment cannot be changed after creation.");
        }

        Result tokenResult = ApplyConcurrencyToken(payment, request.RowVersion);
        if (!tokenResult.IsSuccess)
        {
            return Result<PaymentDto>.Failure(tokenResult.ErrorCode!, tokenResult.Detail);
        }

        return await PersistDraftUpdateAsync(payment, request, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<Result> DeleteDraftAsync(Guid id, CancellationToken cancellationToken)
    {
        Payment? payment = await LoadAsync(id, tracking: true, cancellationToken).ConfigureAwait(false);
        if (payment is null)
        {
            return Result.Failure(PaymentErrorCodes.PAYMENT_NOT_FOUND);
        }

        if (payment.Status != PaymentStatus.Draft)
        {
            return Result.Failure(PaymentErrorCodes.PAYMENT_POSTED_IMMUTABLE);
        }

        return await PersistDraftDeleteAsync(payment, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<Result<PaymentDto>> ConfirmAsync(
        Guid id,
        ConfirmPaymentRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Payment? payment = await LoadAsync(id, tracking: true, cancellationToken).ConfigureAwait(false);
        if (payment is null)
        {
            return Result<PaymentDto>.Failure(PaymentErrorCodes.PAYMENT_NOT_FOUND);
        }

        if (payment.Status != PaymentStatus.Draft)
        {
            return Result<PaymentDto>.Failure(PaymentErrorCodes.PAYMENT_NOT_DRAFT);
        }

        if (payment.DocumentNumber is not null)
        {
            return Result<PaymentDto>.Failure(PaymentErrorCodes.PAYMENT_DUPLICATE_DOCUMENT_NUMBER);
        }

        Result tokenResult = ApplyConcurrencyToken(payment, request.RowVersion);
        if (!tokenResult.IsSuccess)
        {
            return Result<PaymentDto>.Failure(tokenResult.ErrorCode!, tokenResult.Detail);
        }

        Result guardResult = await RunConfirmGuardsAsync(payment, cancellationToken).ConfigureAwait(false);
        if (!guardResult.IsSuccess)
        {
            return Result<PaymentDto>.Failure(guardResult.ErrorCode!, guardResult.Detail);
        }

        return await ConfirmInTransactionAsync(payment, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<Result<PaymentDto>> PostAsync(
        Guid id,
        PostPaymentRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Payment? payment = await LoadAsync(id, tracking: true, cancellationToken).ConfigureAwait(false);
        if (payment is null)
        {
            return Result<PaymentDto>.Failure(PaymentErrorCodes.PAYMENT_NOT_FOUND);
        }

        if (payment.Status == PaymentStatus.Posted)
        {
            return Result<PaymentDto>.Success(Mapper.Map<PaymentDto>(payment));
        }

        if (payment.Status != PaymentStatus.Confirmed)
        {
            return Result<PaymentDto>.Failure(PaymentErrorCodes.PAYMENT_NOT_CONFIRMED);
        }

        Result tokenResult = ApplyConcurrencyToken(payment, request.RowVersion);
        if (!tokenResult.IsSuccess)
        {
            return Result<PaymentDto>.Failure(tokenResult.ErrorCode!, tokenResult.Detail);
        }

        if (payment.JournalEntryId is null)
        {
            return await ReEnqueueConfirmedEventAsync(payment, cancellationToken).ConfigureAwait(false);
        }

        return await CompleteLinkedPostAsync(payment, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<Result<PaymentDto>> CancelAsync(
        Guid id,
        CancelPaymentRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            return Result<PaymentDto>.Failure(PaymentErrorCodes.PAYMENT_CANCEL_REASON_REQUIRED);
        }

        Payment? payment = await LoadAsync(id, tracking: true, cancellationToken).ConfigureAwait(false);
        if (payment is null)
        {
            return Result<PaymentDto>.Failure(PaymentErrorCodes.PAYMENT_NOT_FOUND);
        }

        if (payment.Status != PaymentStatus.Draft)
        {
            return Result<PaymentDto>.Failure(PaymentErrorCodes.INVALID_PAYMENT_STATE_TRANSITION);
        }

        if (payment.AllocatedAmount > 0m)
        {
            return Result<PaymentDto>.Failure(PaymentErrorCodes.PAYMENT_HAS_ALLOCATIONS);
        }

        Result tokenResult = ApplyConcurrencyToken(payment, request.RowVersion);
        if (!tokenResult.IsSuccess)
        {
            return Result<PaymentDto>.Failure(tokenResult.ErrorCode!, tokenResult.Detail);
        }

        return await CancelInTransactionAsync(payment, request.Reason, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<Result<PaymentDto>> ReverseAsync(
        Guid id,
        ReversePaymentRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            return Result<PaymentDto>.Failure(PaymentErrorCodes.PAYMENT_REVERSE_REASON_REQUIRED);
        }

        Payment? payment = await LoadAsync(id, tracking: true, cancellationToken).ConfigureAwait(false);
        if (payment is null)
        {
            return Result<PaymentDto>.Failure(PaymentErrorCodes.PAYMENT_NOT_FOUND);
        }

        Result eligible = await AssertReversibleAsync(payment, cancellationToken).ConfigureAwait(false);
        if (!eligible.IsSuccess)
        {
            return Result<PaymentDto>.Failure(eligible.ErrorCode!, eligible.Detail);
        }

        Result tokenResult = ApplyConcurrencyToken(payment, request.RowVersion);
        if (!tokenResult.IsSuccess)
        {
            return Result<PaymentDto>.Failure(tokenResult.ErrorCode!, tokenResult.Detail);
        }

        return await ReverseInTransactionAsync(payment, request.Reason, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<Result> LinkPostedJournalEntryAsync(
        Guid paymentId,
        Guid journalEntryId,
        CancellationToken cancellationToken)
    {
        Payment? payment = await LoadAsync(paymentId, tracking: true, cancellationToken).ConfigureAwait(false);
        if (payment is null)
        {
            return Result.Failure(PaymentErrorCodes.PAYMENT_NOT_FOUND);
        }

        if (payment.Status == PaymentStatus.Posted)
        {
            return Result.Success();
        }

        if (payment.Status != PaymentStatus.Confirmed)
        {
            return Result.Failure(PaymentErrorCodes.PAYMENT_NOT_CONFIRMED);
        }

        payment.JournalEntryId = journalEntryId;
        return await TransitionToPostedAsync(payment, cancellationToken).ConfigureAwait(false);
    }

    private Payment BuildDraft(CreatePaymentRequest request)
    {
        DateTimeOffset now = _timeProvider.GetUtcNow();

        return new Payment
        {
            DocumentType = request.DocumentType,
            Direction = PaymentDocumentTypeMap.DirectionFor(request.DocumentType),
            Method = request.Method,
            Status = PaymentStatus.Draft,
            CounterpartyId = request.CounterpartyId,
            CurrencyCode = request.CurrencyCode,
            BaseCurrencyCode = _country.BaseCurrencyCode,
            Amount = request.Amount,
            ExchangeRate = request.ExchangeRate,
            AllocatedAmount = 0m,
            SettlementAccountId = request.SettlementAccountId,
            PaymentDate = request.PaymentDate,
            BankReference = request.BankReference,
            CorrelationId = Correlation.Get(),
            CreatedAt = now,
            CreatedBy = _currentUser.GetUserId()
        };
    }

    private async Task<Result> ValidateDraftAsync(Payment payment, CancellationToken cancellationToken)
    {
        Result amounts = ValidateAmounts(payment);
        if (!amounts.IsSuccess)
        {
            return amounts;
        }

        return await _settlementAccounts
            .EnsureUsableAsync(payment.SettlementAccountId, cancellationToken)
            .ConfigureAwait(false);
    }

    private Result ValidateAmounts(Payment payment)
    {
        if (payment.Amount <= 0m)
        {
            return Result.Failure(
                PaymentErrorCodes.INVALID_PAYMENT_AMOUNT,
                "The payment amount must be strictly greater than zero.");
        }

        if (payment.ExchangeRate <= 0m)
        {
            return Result.Failure(
                PaymentErrorCodes.INVALID_PAYMENT_EXCHANGE_RATE,
                "The exchange rate must be strictly greater than zero.");
        }

        bool isBaseCurrency = string.Equals(
            payment.CurrencyCode,
            payment.BaseCurrencyCode,
            StringComparison.Ordinal);
        if (isBaseCurrency && payment.ExchangeRate != 1.000000m)
        {
            return Result.Failure(
                PaymentErrorCodes.INVALID_PAYMENT_EXCHANGE_RATE,
                "A base-currency payment must carry an exchange rate of exactly 1.000000.");
        }

        if (!_amounts.Reconciles(payment))
        {
            return Result.Failure(
                PaymentErrorCodes.PAYMENT_BASE_AMOUNT_MISMATCH,
                "The base amount does not reconcile to the rounded amount times the exchange rate.");
        }

        return Result.Success();
    }

    private async Task<Result> RunConfirmGuardsAsync(Payment payment, CancellationToken cancellationToken)
    {
        _amounts.Recompute(payment);

        Result validated = await ValidateDraftAsync(payment, cancellationToken).ConfigureAwait(false);
        if (!validated.IsSuccess)
        {
            return validated;
        }

        Result period = await _periodGuard
            .EnsureOpenAsync(payment.PaymentDate, cancellationToken)
            .ConfigureAwait(false);
        if (!period.IsSuccess)
        {
            return period;
        }

        return AssertConfirmClockYear(payment);
    }

    private Result AssertConfirmClockYear(Payment payment)
    {
        int confirmClockYear = _timeProvider.GetUtcNow().Year;
        if (payment.PaymentDate.Year == confirmClockYear)
        {
            return Result.Success();
        }

        return Result.Failure(
            PaymentErrorCodes.PAYMENT_DATE_YEAR_MISMATCH,
            "The payment date year must equal the confirm-clock year, which pins the document number series.");
    }

    private async Task<Result> AssertReversibleAsync(Payment payment, CancellationToken cancellationToken)
    {
        if (payment.Status != PaymentStatus.Posted)
        {
            return Result.Failure(PaymentErrorCodes.INVALID_PAYMENT_STATE_TRANSITION);
        }

        if (payment.AllocatedAmount > 0m)
        {
            return Result.Failure(PaymentErrorCodes.PAYMENT_HAS_ALLOCATIONS);
        }

        return await _periodGuard
            .EnsureOpenAsync(payment.PaymentDate, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<Result<PaymentDto>> PersistDraftAsync(
        Payment payment,
        CancellationToken cancellationToken)
    {
        await using IDbContextTransaction transaction =
            await Db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        Db.Payments.Add(payment);
        Result inserted = await SaveWithConcurrencyCheckAsync(cancellationToken).ConfigureAwait(false);
        if (!inserted.IsSuccess)
        {
            return Result<PaymentDto>.Failure(inserted.ErrorCode!, inserted.Detail);
        }

        Result audited = await RecordAuditAsync(
            PaymentAuditEventTypes.PaymentCreated,
            AuditOperation.Create,
            payment,
            beforeJson: null,
            SerializePayment(payment),
            reason: null,
            cancellationToken).ConfigureAwait(false);
        if (!audited.IsSuccess)
        {
            return Result<PaymentDto>.Failure(audited.ErrorCode!, audited.Detail);
        }

        Result committed = await CommitAsync(transaction, cancellationToken).ConfigureAwait(false);
        if (!committed.IsSuccess)
        {
            return Result<PaymentDto>.Failure(committed.ErrorCode!, committed.Detail);
        }

        return Result<PaymentDto>.Success(Mapper.Map<PaymentDto>(payment));
    }

    private async Task<Result<PaymentDto>> PersistDraftUpdateAsync(
        Payment payment,
        UpdatePaymentRequest request,
        CancellationToken cancellationToken)
    {
        await using IDbContextTransaction transaction =
            await Db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        string beforeJson = SerializePayment(payment);
        ApplyUpdate(payment, request);
        _amounts.Recompute(payment);

        Result validated = await ValidateDraftAsync(payment, cancellationToken).ConfigureAwait(false);
        if (!validated.IsSuccess)
        {
            return Result<PaymentDto>.Failure(validated.ErrorCode!, validated.Detail);
        }

        Result audited = await RecordAuditAsync(
            PaymentAuditEventTypes.PaymentUpdated,
            AuditOperation.Update,
            payment,
            beforeJson,
            SerializePayment(payment),
            reason: null,
            cancellationToken).ConfigureAwait(false);
        if (!audited.IsSuccess)
        {
            return Result<PaymentDto>.Failure(audited.ErrorCode!, audited.Detail);
        }

        Result committed = await CommitAsync(transaction, cancellationToken).ConfigureAwait(false);
        if (!committed.IsSuccess)
        {
            return Result<PaymentDto>.Failure(committed.ErrorCode!, committed.Detail);
        }

        return Result<PaymentDto>.Success(Mapper.Map<PaymentDto>(payment));
    }

    private static void ApplyUpdate(Payment payment, UpdatePaymentRequest request)
    {
        payment.Method = request.Method;
        payment.CounterpartyId = request.CounterpartyId;
        payment.CurrencyCode = request.CurrencyCode;
        payment.Amount = request.Amount;
        payment.ExchangeRate = request.ExchangeRate;
        payment.SettlementAccountId = request.SettlementAccountId;
        payment.PaymentDate = request.PaymentDate;
        payment.BankReference = request.BankReference;
    }

    private async Task<Result> PersistDraftDeleteAsync(Payment payment, CancellationToken cancellationToken)
    {
        await using IDbContextTransaction transaction =
            await Db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        Result audited = await RecordAuditAsync(
            PaymentAuditEventTypes.PaymentDeleted,
            AuditOperation.Delete,
            payment,
            SerializePayment(payment),
            DeletedSnapshot,
            reason: null,
            cancellationToken).ConfigureAwait(false);
        if (!audited.IsSuccess)
        {
            return audited;
        }

        Db.Payments.Remove(payment);
        return await CommitAsync(transaction, cancellationToken).ConfigureAwait(false);
    }

    private async Task<Result<PaymentDto>> ConfirmInTransactionAsync(
        Payment payment,
        CancellationToken cancellationToken)
    {
        await using IDbContextTransaction transaction =
            await Db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        string beforeJson = SerializePayment(payment);

        Result transition = await TransitionAsync(
            payment, PaymentStatus.Confirmed, reason: null, cancellationToken).ConfigureAwait(false);
        if (!transition.IsSuccess)
        {
            return Result<PaymentDto>.Failure(transition.ErrorCode!, transition.Detail);
        }

        await AssignDocumentNumberAsync(payment, cancellationToken).ConfigureAwait(false);
        StampConfirmed(payment);
        AppendStatusHistory(payment, PaymentStatus.Draft, PaymentStatus.Confirmed, reason: null);

        Result audited = await RecordAuditAsync(
            PaymentAuditEventTypes.PaymentConfirmed,
            AuditOperation.StateChange,
            payment,
            beforeJson,
            SerializePayment(payment),
            reason: null,
            cancellationToken).ConfigureAwait(false);
        if (!audited.IsSuccess)
        {
            return Result<PaymentDto>.Failure(audited.ErrorCode!, audited.Detail);
        }

        await _publishEndpoint
            .Publish(BuildConfirmedEvent(payment, payment.CorrelationId), cancellationToken)
            .ConfigureAwait(false);

        Result committed = await CommitAsync(transaction, cancellationToken).ConfigureAwait(false);
        if (!committed.IsSuccess)
        {
            return Result<PaymentDto>.Failure(committed.ErrorCode!, committed.Detail);
        }

        return Result<PaymentDto>.Success(Mapper.Map<PaymentDto>(payment));
    }

    private async Task<Result<PaymentDto>> ReEnqueueConfirmedEventAsync(
        Payment payment,
        CancellationToken cancellationToken)
    {
        await using IDbContextTransaction transaction =
            await Db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        await _publishEndpoint
            .Publish(BuildConfirmedEvent(payment, payment.CorrelationId), cancellationToken)
            .ConfigureAwait(false);

        Result committed = await CommitAsync(transaction, cancellationToken).ConfigureAwait(false);
        if (!committed.IsSuccess)
        {
            return Result<PaymentDto>.Failure(committed.ErrorCode!, committed.Detail);
        }

        return Result<PaymentDto>.Failure(
            PaymentErrorCodes.PAYMENT_POSTING_PENDING,
            "The Journal posting handshake has not linked a journal entry yet; the confirm event was re-enqueued.");
    }

    private async Task<Result<PaymentDto>> CompleteLinkedPostAsync(
        Payment payment,
        CancellationToken cancellationToken)
    {
        Result period = await _periodGuard
            .EnsureOpenAsync(payment.PaymentDate, cancellationToken)
            .ConfigureAwait(false);
        if (!period.IsSuccess)
        {
            return Result<PaymentDto>.Failure(period.ErrorCode!, period.Detail);
        }

        Result posted = await TransitionToPostedAsync(payment, cancellationToken).ConfigureAwait(false);
        if (!posted.IsSuccess)
        {
            return Result<PaymentDto>.Failure(posted.ErrorCode!, posted.Detail);
        }

        return Result<PaymentDto>.Success(Mapper.Map<PaymentDto>(payment));
    }

    private async Task<Result<PaymentDto>> CancelInTransactionAsync(
        Payment payment,
        string reason,
        CancellationToken cancellationToken)
    {
        await using IDbContextTransaction transaction =
            await Db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        string beforeJson = SerializePayment(payment);

        Result transition = await TransitionAsync(
            payment, PaymentStatus.Cancelled, reason, cancellationToken).ConfigureAwait(false);
        if (!transition.IsSuccess)
        {
            return Result<PaymentDto>.Failure(transition.ErrorCode!, transition.Detail);
        }

        payment.CancellationReason = reason;
        AppendStatusHistory(payment, PaymentStatus.Draft, PaymentStatus.Cancelled, reason);

        Result audited = await RecordAuditAsync(
            PaymentAuditEventTypes.PaymentCancelled,
            AuditOperation.StateChange,
            payment,
            beforeJson,
            SerializePayment(payment),
            reason,
            cancellationToken).ConfigureAwait(false);
        if (!audited.IsSuccess)
        {
            return Result<PaymentDto>.Failure(audited.ErrorCode!, audited.Detail);
        }

        await _publishEndpoint
            .Publish(BuildCancelledEvent(payment, reason), cancellationToken)
            .ConfigureAwait(false);

        Result committed = await CommitAsync(transaction, cancellationToken).ConfigureAwait(false);
        if (!committed.IsSuccess)
        {
            return Result<PaymentDto>.Failure(committed.ErrorCode!, committed.Detail);
        }

        return Result<PaymentDto>.Success(Mapper.Map<PaymentDto>(payment));
    }

    private async Task<Result<PaymentDto>> ReverseInTransactionAsync(
        Payment payment,
        string reason,
        CancellationToken cancellationToken)
    {
        await using IDbContextTransaction transaction =
            await Db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        string beforeJson = SerializePayment(payment);

        Result transition = await TransitionAsync(
            payment, PaymentStatus.Reversed, reason, cancellationToken).ConfigureAwait(false);
        if (!transition.IsSuccess)
        {
            return Result<PaymentDto>.Failure(transition.ErrorCode!, transition.Detail);
        }

        payment.ReversedAt = _timeProvider.GetUtcNow();
        AppendStatusHistory(payment, PaymentStatus.Posted, PaymentStatus.Reversed, reason);

        Result audited = await RecordAuditAsync(
            PaymentAuditEventTypes.PaymentReversed,
            AuditOperation.StateChange,
            payment,
            beforeJson,
            SerializePayment(payment),
            reason,
            cancellationToken).ConfigureAwait(false);
        if (!audited.IsSuccess)
        {
            return Result<PaymentDto>.Failure(audited.ErrorCode!, audited.Detail);
        }

        await _publishEndpoint
            .Publish(BuildReversedEvent(payment, reason), cancellationToken)
            .ConfigureAwait(false);

        Result committed = await CommitAsync(transaction, cancellationToken).ConfigureAwait(false);
        if (!committed.IsSuccess)
        {
            return Result<PaymentDto>.Failure(committed.ErrorCode!, committed.Detail);
        }

        return Result<PaymentDto>.Success(Mapper.Map<PaymentDto>(payment));
    }

    private async Task<Result> TransitionToPostedAsync(Payment payment, CancellationToken cancellationToken)
    {
        await using IDbContextTransaction transaction =
            await Db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        string beforeJson = SerializePayment(payment);

        Result transition = await TransitionAsync(
            payment, PaymentStatus.Posted, reason: null, cancellationToken).ConfigureAwait(false);
        if (!transition.IsSuccess)
        {
            return transition;
        }

        payment.PostedAt = _timeProvider.GetUtcNow();
        AppendStatusHistory(payment, PaymentStatus.Confirmed, PaymentStatus.Posted, reason: null);

        Result audited = await RecordAuditAsync(
            PaymentAuditEventTypes.PaymentPosted,
            AuditOperation.StateChange,
            payment,
            beforeJson,
            SerializePayment(payment),
            reason: null,
            cancellationToken).ConfigureAwait(false);
        if (!audited.IsSuccess)
        {
            return audited;
        }

        return await CommitAsync(transaction, cancellationToken).ConfigureAwait(false);
    }

    private async Task<Result> CommitAsync(
        IDbContextTransaction transaction,
        CancellationToken cancellationToken)
    {
        Result saved = await SaveWithConcurrencyCheckAsync(cancellationToken).ConfigureAwait(false);
        if (!saved.IsSuccess)
        {
            return saved;
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }

    private async Task AssignDocumentNumberAsync(Payment payment, CancellationToken cancellationToken)
    {
        string sequenceKey = PaymentDocumentTypeMap.SequenceKeyFor(payment.DocumentType);
        long sequenceValue = await _sequence.NextValueAsync(sequenceKey, cancellationToken).ConfigureAwait(false);
        payment.DocumentNumber = _country.GenerateDocumentNumber(payment.DocumentType, sequenceValue);
    }

    private void StampConfirmed(Payment payment)
    {
        payment.ConfirmedAt = _timeProvider.GetUtcNow();
        payment.ConfirmedBy = _currentUser.GetUserId();
        payment.Status = PaymentStatus.Confirmed;
    }

    private async Task<Result> TransitionAsync(
        Payment payment,
        PaymentStatus target,
        string? reason,
        CancellationToken cancellationToken)
    {
        WorkflowContext<Payment> context = new()
        {
            Aggregate = payment,
            CurrentState = payment.Status.ToString(),
            TargetState = target.ToString(),
            Reason = reason,
            CorrelationId = Correlation.Get()
        };

        Result transition = await _workflow.TransitionAsync(context, cancellationToken).ConfigureAwait(false);
        if (!transition.IsSuccess)
        {
            return Result.Failure(TranslateTransitionCode(transition), transition.Detail);
        }

        payment.Status = target;
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
            return PaymentErrorCodes.INVALID_PAYMENT_STATE_TRANSITION;
        }

        return transition.ErrorCode!;
    }

    private void AppendStatusHistory(
        Payment payment,
        PaymentStatus fromStatus,
        PaymentStatus toStatus,
        string? reason)
    {
        payment.StatusHistory.Add(new PaymentStatusHistory
        {
            FromStatus = fromStatus.ToString(),
            ToStatus = toStatus.ToString(),
            ChangedBy = _currentUser.GetUserId(),
            ChangedAt = _timeProvider.GetUtcNow(),
            CorrelationId = Correlation.Get(),
            Reason = reason
        });
    }

    private Task<Result> RecordAuditAsync(
        string eventType,
        AuditOperation operation,
        Payment payment,
        string? beforeJson,
        string afterJson,
        string? reason,
        CancellationToken cancellationToken)
    {
        AuditEntry audit = new()
        {
            EventType = eventType,
            Operation = operation,
            EntityType = PaymentAuditEventTypes.EntityType,
            EntityId = payment.Id.ToString(),
            UserId = _currentUser.GetUserId(),
            Username = _currentUser.GetUsername(),
            OccurredAt = _timeProvider.GetUtcNow(),
            CorrelationId = Correlation.Get(),
            BeforeJson = beforeJson,
            AfterJson = afterJson,
            Reason = reason
        };

        return _audit.RecordAsync(audit, cancellationToken);
    }

    private PaymentConfirmedEvent BuildConfirmedEvent(Payment payment, string correlationId) => new()
    {
        MessageId = Guid.NewGuid(),
        CorrelationId = correlationId,
        OccurredAt = _timeProvider.GetUtcNow(),
        PaymentId = payment.Id,
        DocumentNumber = payment.DocumentNumber!,
        DocumentType = payment.DocumentType,
        Direction = payment.Direction,
        Method = payment.Method,
        CounterpartyId = payment.CounterpartyId,
        SettlementAccountId = payment.SettlementAccountId,
        CurrencyCode = payment.CurrencyCode,
        BaseCurrencyCode = payment.BaseCurrencyCode,
        Amount = payment.Amount,
        ExchangeRate = payment.ExchangeRate,
        BaseAmount = payment.BaseAmount,
        PaymentDate = payment.PaymentDate,
        PostingRuleKey = PaymentDocumentTypeMap.PostingRuleKeyFor(payment.DocumentType)
    };

    private PaymentCancelledEvent BuildCancelledEvent(Payment payment, string reason) => new()
    {
        MessageId = Guid.NewGuid(),
        CorrelationId = Correlation.Get(),
        OccurredAt = _timeProvider.GetUtcNow(),
        PaymentId = payment.Id,
        DocumentNumber = payment.DocumentNumber,
        Reason = reason
    };

    private PaymentReversedEvent BuildReversedEvent(Payment payment, string reason) => new()
    {
        MessageId = Guid.NewGuid(),
        CorrelationId = Correlation.Get(),
        OccurredAt = _timeProvider.GetUtcNow(),
        PaymentId = payment.Id,
        DocumentNumber = payment.DocumentNumber!,
        JournalEntryId = payment.JournalEntryId!.Value,
        Reason = reason
    };

    private Task<Payment?> LoadAsync(Guid id, bool tracking, CancellationToken cancellationToken)
    {
        IQueryable<Payment> query = Db.Payments.Where(payment => payment.Id == id);

        if (!tracking)
        {
            query = query.AsNoTracking();
        }

        return query.FirstOrDefaultAsync(cancellationToken);
    }

    private Result ApplyConcurrencyToken(Payment payment, string rowVersion)
    {
        if (!TryDecodeRowVersion(rowVersion, out byte[] originalRowVersion))
        {
            return Result.Failure(
                CommonErrorCodes.CONCURRENT_MODIFICATION,
                "The supplied row version is not a valid base64 token.");
        }

        Db.Entry(payment).Property(entity => entity.RowVersion).OriginalValue = originalRowVersion;
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

    private static string SerializePayment(Payment payment)
    {
        return JsonSerializer.Serialize(new
        {
            payment.Id,
            payment.DocumentNumber,
            DocumentType = payment.DocumentType.ToString(),
            Direction = payment.Direction.ToString(),
            Method = payment.Method.ToString(),
            Status = payment.Status.ToString(),
            payment.CounterpartyId,
            payment.CurrencyCode,
            payment.BaseCurrencyCode,
            payment.Amount,
            payment.ExchangeRate,
            payment.BaseAmount,
            payment.AllocatedAmount,
            payment.SettlementAccountId,
            payment.PaymentDate,
            payment.BankReference,
            payment.JournalEntryId,
            payment.CancellationReason,
            payment.ConfirmedAt,
            payment.PostedAt,
            payment.ReversedAt
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
            Sort = [new SortCriterion { Field = PaymentDateSortField, Direction = "desc" }]
        };
    }
}
