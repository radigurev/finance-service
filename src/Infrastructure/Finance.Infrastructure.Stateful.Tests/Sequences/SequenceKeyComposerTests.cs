using Finance.Infrastructure.Sequences;
using NUnit.Framework;

namespace Finance.Infrastructure.Stateful.Tests.Sequences;

/// <summary>
/// Unit tests for <see cref="SequenceKeyComposer"/> covering the composite-key computation per reset
/// policy (SDD-INFRA-003 §2.2): <c>{key}:{yyyy}</c> Yearly, <c>{key}:{yyyyMM}</c> Monthly,
/// <c>{key}:{yyyyMMdd}</c> Daily, and the bare <c>{key}</c> for Never.
/// </summary>
[TestFixture]
[Category("SDD-INFRA-003")]
public sealed class SequenceKeyComposerTests
{
    private static readonly DateTimeOffset Moment = new(2026, 3, 7, 13, 45, 0, TimeSpan.Zero);

    /// <summary>The Yearly composite key appends the four-digit fiscal year.</summary>
    [Test]
    public void CompositeKey_Yearly_AppendsFourDigitYear()
    {
        // Arrange & Act
        string composite = SequenceKeyComposer.CompositeKey("JE", SequenceResetPolicy.Yearly, Moment);

        // Assert
        Assert.That(composite, Is.EqualTo("JE:2026"));
    }

    /// <summary>The Monthly composite key appends the six-digit year-month.</summary>
    [Test]
    public void CompositeKey_Monthly_AppendsYearMonth()
    {
        // Arrange & Act
        string composite = SequenceKeyComposer.CompositeKey("PAY", SequenceResetPolicy.Monthly, Moment);

        // Assert
        Assert.That(composite, Is.EqualTo("PAY:202603"));
    }

    /// <summary>The Daily composite key appends the eight-digit year-month-day.</summary>
    [Test]
    public void CompositeKey_Daily_AppendsYearMonthDay()
    {
        // Arrange & Act
        string composite = SequenceKeyComposer.CompositeKey("RCT", SequenceResetPolicy.Daily, Moment);

        // Assert
        Assert.That(composite, Is.EqualTo("RCT:20260307"));
    }

    /// <summary>The Never composite key is the bare sequence key with no period segment.</summary>
    [Test]
    public void CompositeKey_Never_IsBareKey()
    {
        // Arrange & Act
        string composite = SequenceKeyComposer.CompositeKey("JE", SequenceResetPolicy.Never, Moment);

        // Assert
        Assert.That(composite, Is.EqualTo("JE"));
    }

    /// <summary>The period segment matches the documented format per reset policy.</summary>
    [TestCase(SequenceResetPolicy.Yearly, "2026")]
    [TestCase(SequenceResetPolicy.Monthly, "202603")]
    [TestCase(SequenceResetPolicy.Daily, "20260307")]
    [TestCase(SequenceResetPolicy.Never, "")]
    public void PeriodSegment_PerResetPolicy_MatchesDocumentedFormat(SequenceResetPolicy policy, string expected)
    {
        // Arrange & Act
        string segment = SequenceKeyComposer.PeriodSegment(policy, Moment);

        // Assert
        Assert.That(segment, Is.EqualTo(expected));
    }
}
