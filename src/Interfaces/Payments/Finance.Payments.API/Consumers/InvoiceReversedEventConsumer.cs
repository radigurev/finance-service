using Finance.Common.Results;
using Finance.Payments.API.Interfaces;
using Finance.ServiceModel.Events.Invoices;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace Finance.Payments.API.Consumers;

/// <summary>
/// MassTransit consumer mirroring <see cref="InvoiceReversedEvent"/> onto the LOCAL invoice open-item projection
/// (SDD-PAY-002 §2.3). Wrapped transparently by the shared <c>UseFinanceIdempotency()</c> filter
/// (SDD-INFRA-006).
/// <para>The mirror is what makes the allocation eligibility rule enforceable for a reversed invoice: without
/// it the projection would keep reading <c>Posted</c> forever, so a real receipt could be matched to a document
/// whose ledger effect is fully offset while the genuinely open invoice stayed outstanding. It never creates a
/// row (a reversal presupposes a posted invoice the projection has already seen), never deletes the row, and
/// never removes or releases existing allocation rows — history stays auditable.</para>
/// <para>A MISSING row is rethrown so MassTransit retries and finally dead-letters, the same contract the posting
/// consumer follows (and the same CHG-FIX-006 prerequisite).</para>
/// </summary>
public sealed class InvoiceReversedEventConsumer : IConsumer<InvoiceReversedEvent>
{
    private readonly IInvoiceOpenItemProjection _projection;
    private readonly ILogger<InvoiceReversedEventConsumer> _logger;

    /// <summary>Creates a new <see cref="InvoiceReversedEventConsumer"/>.</summary>
    /// <param name="projection">The local open-item projection writer.</param>
    /// <param name="logger">The consumer logger.</param>
    public InvoiceReversedEventConsumer(
        IInvoiceOpenItemProjection projection,
        ILogger<InvoiceReversedEventConsumer> logger)
    {
        ArgumentNullException.ThrowIfNull(projection);
        ArgumentNullException.ThrowIfNull(logger);

        _projection = projection;
        _logger = logger;
    }

    /// <summary>Mirrors the reversed status onto the existing open item.</summary>
    /// <param name="context">The consume context carrying the reversal event.</param>
    /// <returns>A task that completes when the status has been mirrored.</returns>
    public async Task Consume(ConsumeContext<InvoiceReversedEvent> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        InvoiceReversedEvent message = context.Message;

        _logger.LogInformation(
            "Mirroring reversed status of invoice {InvoiceId} onto the open-item projection; the item becomes "
            + "ineligible for further allocation",
            message.InvoiceId);

        Result result = await _projection
            .ApplyReversedAsync(message.InvoiceId, context.CancellationToken)
            .ConfigureAwait(false);

        if (!result.IsSuccess)
        {
            _logger.LogError(
                "Failed to mirror reversed status of invoice {InvoiceId}. Code={ErrorCode}",
                message.InvoiceId,
                result.ErrorCode);
            throw new InvalidOperationException(
                $"Mirroring the reversed status of invoice {message.InvoiceId} failed with code {result.ErrorCode}.");
        }
    }
}
