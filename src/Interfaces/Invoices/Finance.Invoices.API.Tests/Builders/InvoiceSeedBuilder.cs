using Finance.Common.Enums;
using Finance.Invoices.DBModel.Models;

namespace Finance.Invoices.API.Tests.Builders;

/// <summary>
/// Builds <see cref="Invoice"/> entities for DIRECT insertion into the SQLite-backed context by the settlement
/// mirror tests (SDD-INV-001 §6.7). The lifecycle create path is covered by the service tests; these tests need
/// an invoice already in a chosen lifecycle and settlement state, so seeding it directly keeps the Arrange
/// section deterministic and free of lifecycle side effects.
/// <para>Defaults produce a <c>Posted</c>, base-currency, fully-unsettled sale invoice with
/// <c>GrossTotal = 1000.00</c> and a <c>null</c> ordering token, so the first allocation event always applies.</para>
/// </summary>
public sealed class InvoiceSeedBuilder
{
    private static readonly DateTimeOffset SeedIssueDate = new(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);

    private Guid _id = Guid.NewGuid();
    private string? _documentNumber;
    private InvoiceStatus _status = InvoiceStatus.Posted;
    private string _currencyCode = "BGN";
    private decimal _exchangeRate = 1.000000m;
    private decimal _grossTotal = 1000.00m;
    private decimal _settledAmount;
    private SettlementStatus _settlementStatus = SettlementStatus.Unsettled;
    private DateTimeOffset? _lastSettlementAppliedAt;

    /// <summary>Starts a new builder with valid defaults.</summary>
    /// <returns>A fresh builder.</returns>
    public static InvoiceSeedBuilder Create() => new();

    /// <summary>Sets the invoice identifier the allocation events reference.</summary>
    /// <param name="id">The invoice id.</param>
    /// <returns>This builder.</returns>
    public InvoiceSeedBuilder WithId(Guid id)
    {
        _id = id;
        return this;
    }

    /// <summary>
    /// Overrides the gapless document number, so a test may seed SEVERAL non-draft invoices without colliding on
    /// the UNIQUE filtered index <c>IX_Invoices_DocumentNumber</c>.
    /// </summary>
    /// <param name="documentNumber">The document number to assign.</param>
    /// <returns>This builder.</returns>
    public InvoiceSeedBuilder WithDocumentNumber(string documentNumber)
    {
        _documentNumber = documentNumber;
        return this;
    }

    /// <summary>Sets the lifecycle status (settlement is orthogonal to it).</summary>
    /// <param name="status">The lifecycle status.</param>
    /// <returns>This builder.</returns>
    public InvoiceSeedBuilder WithStatus(InvoiceStatus status)
    {
        _status = status;
        return this;
    }

    /// <summary>Sets the transactional currency and the frozen booking rate.</summary>
    /// <param name="currencyCode">The ISO 4217 code.</param>
    /// <param name="exchangeRate">The frozen booking rate.</param>
    /// <returns>This builder.</returns>
    public InvoiceSeedBuilder WithCurrency(string currencyCode, decimal exchangeRate)
    {
        _currencyCode = currencyCode;
        _exchangeRate = exchangeRate;
        return this;
    }

    /// <summary>Sets the gross total that is the settlement ceiling.</summary>
    /// <param name="grossTotal">The document gross total.</param>
    /// <returns>This builder.</returns>
    public InvoiceSeedBuilder WithGrossTotal(decimal grossTotal)
    {
        _grossTotal = grossTotal;
        return this;
    }

    /// <summary>Sets the already-mirrored settlement state and the ordering token that produced it.</summary>
    /// <param name="settledAmount">The mirrored settled amount.</param>
    /// <param name="settlementStatus">The derived settlement status.</param>
    /// <param name="lastSettlementAppliedAt">The ordering token of the event that last applied.</param>
    /// <returns>This builder.</returns>
    public InvoiceSeedBuilder WithSettlement(
        decimal settledAmount,
        SettlementStatus settlementStatus,
        DateTimeOffset? lastSettlementAppliedAt)
    {
        _settledAmount = settledAmount;
        _settlementStatus = settlementStatus;
        _lastSettlementAppliedAt = lastSettlementAppliedAt;
        return this;
    }

    /// <summary>Materializes the configured invoice entity.</summary>
    /// <returns>The built <see cref="Invoice"/>.</returns>
    public Invoice Build()
    {
        decimal net = decimal.Round(_grossTotal / 1.20m, 2);

        return new Invoice
        {
            Id = _id,
            DocumentNumber = _documentNumber ?? (_status == InvoiceStatus.Draft ? null : "SINV-2026-000001"),
            DocumentType = InvoiceDocumentType.SaleInvoice,
            Direction = InvoiceDirection.AR,
            Status = _status,
            CounterpartyId = new Guid("11111111-1111-1111-1111-111111111111"),
            CurrencyCode = _currencyCode,
            BaseCurrencyCode = "BGN",
            ExchangeRate = _exchangeRate,
            IssueDate = SeedIssueDate,
            DueDate = SeedIssueDate.AddDays(30),
            NetTotal = net,
            TaxTotal = _grossTotal - net,
            GrossTotal = _grossTotal,
            SettledAmount = _settledAmount,
            SettlementStatus = _settlementStatus,
            LastSettlementAppliedAt = _lastSettlementAppliedAt,
            CorrelationId = "seed-correlation-id",
            CreatedAt = SeedIssueDate,
            CreatedBy = new Guid("33333333-3333-3333-3333-333333333333"),
            Lines = []
        };
    }
}
