using System.Diagnostics;
using Finance.Common.Abstractions;
using Finance.Infrastructure.Web.Observability;
using NUnit.Framework;

namespace Finance.Infrastructure.Tests.Web;

/// <summary>
/// Unit test for <see cref="CorrelationIdSpanProcessor"/> verifying it stamps the ambient correlation id
/// onto a started <see cref="Activity"/> as the <c>correlation_id</c> tag (SDD-OBS-001 §2.4, Batch 2).
/// </summary>
[TestFixture]
[Category("SDD-OBS-001")]
public sealed class CorrelationIdSpanProcessorTests
{
    /// <summary>A started activity receives the ambient correlation id as the <c>correlation_id</c> tag.</summary>
    [Test]
    public void Observability_StampsCorrelationIdAsActivityTag()
    {
        // Arrange
        const string correlationId = "11111111-1111-1111-1111-111111111111";
        CorrelationIdSpanProcessor processor = new(new FixedCorrelationIdAccessor(correlationId));

        using ActivitySource source = new("Finance.Infrastructure.Tests");
        using ActivityListener listener = new()
        {
            ShouldListenTo = activitySource => activitySource.Name == source.Name,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData
        };
        ActivitySource.AddActivityListener(listener);

        using Activity? activity = source.StartActivity("test-span");
        Assert.That(activity, Is.Not.Null);

        // Act
        processor.OnStart(activity!);

        // Assert
        Assert.That(
            activity!.GetTagItem(CorrelationIdSpanProcessor.CorrelationIdTagName),
            Is.EqualTo(correlationId));
    }

    private sealed class FixedCorrelationIdAccessor : ICorrelationIdAccessor
    {
        private readonly string _correlationId;

        public FixedCorrelationIdAccessor(string correlationId)
        {
            _correlationId = correlationId;
        }

        public string Get() => _correlationId;
    }
}
