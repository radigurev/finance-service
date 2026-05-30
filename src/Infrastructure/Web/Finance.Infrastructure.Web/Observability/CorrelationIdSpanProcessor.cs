using System.Diagnostics;
using Finance.Common.Abstractions;
using OpenTelemetry;

namespace Finance.Infrastructure.Web.Observability;

/// <summary>
/// OpenTelemetry span processor that copies the ambient correlation id onto each started
/// <see cref="Activity"/> as the <c>correlation_id</c> tag, so a trace in Jaeger can be searched by the
/// business correlation id as well as the W3C trace id (SDD-OBS-001 §2.4).
/// </summary>
public sealed class CorrelationIdSpanProcessor : BaseProcessor<Activity>
{
    /// <summary>The activity tag name carrying the business correlation id.</summary>
    public const string CorrelationIdTagName = "correlation_id";

    private readonly ICorrelationIdAccessor _correlationIdAccessor;

    /// <summary>Initializes the processor with the ambient correlation accessor.</summary>
    /// <param name="correlationIdAccessor">The accessor for the current correlation id.</param>
    public CorrelationIdSpanProcessor(ICorrelationIdAccessor correlationIdAccessor)
    {
        _correlationIdAccessor = correlationIdAccessor;
    }

    /// <inheritdoc />
    public override void OnStart(Activity data)
    {
        ArgumentNullException.ThrowIfNull(data);

        string correlationId = _correlationIdAccessor.Get();
        if (!string.IsNullOrEmpty(correlationId))
        {
            data.SetTag(CorrelationIdTagName, correlationId);
        }
    }
}
