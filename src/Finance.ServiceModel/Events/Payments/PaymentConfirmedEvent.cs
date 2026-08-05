using Finance.Common.Enums;

namespace Finance.ServiceModel.Events.Payments;

/// <summary>
/// Domain event published through the transactional outbox when a payment is confirmed
/// (SDD-PAY-001 §2.4, §2.5, §2.14; SDD-INFRA-006 §2.2). It carries everything the Journal service needs to
/// materialize a balanced entry with no callback: the Journal-side consumer applies
/// <see cref="PostingRuleKey"/> with <c>Amounts[Gross] = Amount</c> in <see cref="CurrencyCode"/> — the
/// TRANSACTIONAL amount in the payment's own currency, never <see cref="BaseAmount"/> — then publishes back
/// <see cref="PaymentPostedEvent"/>.
/// <para>The same event is re-enqueued (rebuilt from the persisted row, with a FRESH
/// <see cref="MessageId"/> and the payment's STORED <see cref="CorrelationId"/>) by the operator post
/// recovery path (SDD-PAY-001 §2.5).</para>
/// <para>Implements <see cref="IFinanceEvent"/>: <see cref="CorrelationId"/> is sourced from
/// <c>ICorrelationIdAccessor.Get()</c> (or the payment's stored value on the recovery path),
/// <see cref="MessageId"/> is a new GUID at construction, and <see cref="OccurredAt"/> is
/// <c>DateTimeOffset.UtcNow</c>.</para>
/// </summary>
public sealed record PaymentConfirmedEvent : IFinanceEvent
{
    /// <inheritdoc />
    public required Guid MessageId { get; init; }

    /// <inheritdoc />
    public required string CorrelationId { get; init; }

    /// <inheritdoc />
    public required DateTimeOffset OccurredAt { get; init; }

    /// <summary>The sequential-GUID identifier of the confirmed payment.</summary>
    public required Guid PaymentId { get; init; }

    /// <summary>The payment's own gapless country-formatted document number assigned at confirm.</summary>
    public required string DocumentNumber { get; init; }

    /// <summary>The document type discriminating the two cash documents.</summary>
    public required PaymentDocumentType DocumentType { get; init; }

    /// <summary>The ledger direction (<c>AP</c>/<c>AR</c>).</summary>
    public required PaymentDirection Direction { get; init; }

    /// <summary>How the cash moved (<c>Cash</c>/<c>BankTransfer</c>/<c>Card</c>).</summary>
    public required PaymentMethod Method { get; init; }

    /// <summary>The Warehouse-owned counterparty reference.</summary>
    public required Guid CounterpartyId { get; init; }

    /// <summary>The cash/bank GL account the movement is recorded against.</summary>
    public required int SettlementAccountId { get; init; }

    /// <summary>The transactional currency code — the currency the journal entry is booked in.</summary>
    public required string CurrencyCode { get; init; }

    /// <summary>The base currency the payment reports in.</summary>
    public required string BaseCurrencyCode { get; init; }

    /// <summary>The transactional cash amount — the amount the journal entry is booked for.</summary>
    public required decimal Amount { get; init; }

    /// <summary>The rate at <see cref="PaymentDate"/>.</summary>
    public required decimal ExchangeRate { get; init; }

    /// <summary>The base-currency value of <see cref="Amount"/> (reporting only; NOT what is posted in v1).</summary>
    public required decimal BaseAmount { get; init; }

    /// <summary>The date the cash moved; the Journal side uses it as the entry date.</summary>
    public required DateTimeOffset PaymentDate { get; init; }

    /// <summary>The posting-rule key (derived from <see cref="DocumentType"/>) the Journal posting applies.</summary>
    public required string PostingRuleKey { get; init; }
}
