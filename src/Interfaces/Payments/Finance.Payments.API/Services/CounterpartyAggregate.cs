namespace Finance.Payments.API.Services;

/// <summary>
/// The accumulated roll-up for ONE (counterparty, currency) pair (SDD-PAY-003 §2.2, §2.6, §2.7). Summing
/// transactional amounts across currencies is meaningless, so the currency is part of the grouping key and a
/// multi-currency counterparty produces one aggregate per currency.
/// <para>It is the SINGLE shared aggregation output behind both <c>/aging</c> and
/// <c>/counterparty-balances</c>, which is why the overdue total is accumulated here rather than re-derived
/// per endpoint: the two surfaces cannot report different totals for the same pair.</para>
/// </summary>
public sealed class CounterpartyAggregate
{
    /// <summary>The Warehouse-owned counterparty reference; half of the grouping key.</summary>
    public required Guid CounterpartyId { get; init; }

    /// <summary>The transactional currency; the other half of the grouping key.</summary>
    public required string CurrencyCode { get; init; }

    /// <summary>The base currency the items book in, echoed unchanged from the projection.</summary>
    public required string BaseCurrencyCode { get; set; }

    /// <summary>The per-bucket transactional outstanding totals, indexed in bucket order.</summary>
    public required decimal[] BucketOutstanding { get; init; }

    /// <summary>The per-bucket base-currency outstanding totals, indexed in bucket order.</summary>
    public required decimal[] BucketBaseOutstanding { get; init; }

    /// <summary>The per-bucket item counts, indexed in bucket order.</summary>
    public required int[] BucketItemCount { get; init; }

    /// <summary>The number of in-scope open items behind the pair.</summary>
    public int OpenItemCount { get; set; }

    /// <summary>The transactional outstanding total; equals the sum of <see cref="BucketOutstanding"/>.</summary>
    public decimal TotalOutstanding { get; set; }

    /// <summary>The base-currency outstanding total; equals the sum of <see cref="BucketBaseOutstanding"/>.</summary>
    public decimal TotalBaseOutstanding { get; set; }

    /// <summary>The transactional outstanding of items at least one day past due — every non-<c>Current</c> bucket.</summary>
    public decimal OverdueOutstanding { get; set; }

    /// <summary>The base-currency counterpart of <see cref="OverdueOutstanding"/>.</summary>
    public decimal BaseOverdueOutstanding { get; set; }

    /// <summary>The earliest due date among the pair's in-scope items, or <c>null</c> when there are none.</summary>
    public DateTimeOffset? OldestDueDate { get; set; }

    /// <summary>
    /// Folds one valued open item into the roll-up: it lands in exactly one bucket, contributes to the row totals,
    /// contributes to the overdue subtotal only when it is past due, and pulls the oldest due date back when it is
    /// earlier than every item seen so far.
    /// </summary>
    /// <param name="valuation">The valued open item to fold in.</param>
    public void Apply(OpenItemValuation valuation)
    {
        ArgumentNullException.ThrowIfNull(valuation);

        BucketOutstanding[valuation.BucketIndex] += valuation.Outstanding;
        BucketBaseOutstanding[valuation.BucketIndex] += valuation.BaseOutstanding;
        BucketItemCount[valuation.BucketIndex]++;

        OpenItemCount++;
        TotalOutstanding += valuation.Outstanding;
        TotalBaseOutstanding += valuation.BaseOutstanding;

        if (valuation.IsOverdue)
        {
            OverdueOutstanding += valuation.Outstanding;
            BaseOverdueOutstanding += valuation.BaseOutstanding;
        }

        DateTimeOffset dueDate = valuation.Row.Item.DueDate;
        if (OldestDueDate is null || dueDate < OldestDueDate.Value)
        {
            OldestDueDate = dueDate;
        }
    }
}
