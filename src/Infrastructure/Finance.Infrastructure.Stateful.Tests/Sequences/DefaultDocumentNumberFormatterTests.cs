using Finance.Common.ErrorCodes;
using Finance.Infrastructure.Sequences;
using NUnit.Framework;

namespace Finance.Infrastructure.Stateful.Tests.Sequences;

/// <summary>
/// Unit tests for <see cref="DefaultDocumentNumberFormatter"/> covering the BG-style output patterns
/// and padding behaviour (SDD-INFRA-003 §2.1, §2.6): <c>{prefix}-{period}-{zero-padded counter}</c>
/// for period-reset sequences, the bare <c>{prefix}-{counter}</c> for Never, and
/// <see cref="SequenceErrorCodes.UNKNOWN_SEQUENCE_KEY"/> for an unregistered key.
/// </summary>
[TestFixture]
[Category("SDD-INFRA-003")]
public sealed class DefaultDocumentNumberFormatterTests
{
    private DefaultDocumentNumberFormatter _formatter = null!;

    /// <summary>Builds the formatter over the built-in sequence definitions before each test.</summary>
    [SetUp]
    public void SetUp()
    {
        _formatter = new DefaultDocumentNumberFormatter(SequenceDefinitions.BuiltIn);
    }

    /// <summary>Each built-in key formats to its documented BG-style pattern with six-digit padding.</summary>
    [TestCase("JE", "2026", 1L, "JE-2026-000001")]
    [TestCase("PINV", "2026", 1L, "ФПок-2026-000001")]
    [TestCase("SINV", "2026", 1L, "ФПр-2026-000001")]
    [TestCase("CN", "2026", 1L, "КИ-2026-000001")]
    [TestCase("DN", "2026", 1L, "ДИ-2026-000001")]
    [TestCase("PAY", "2026", 1L, "PAY-2026-000001")]
    [TestCase("RCT", "2026", 1L, "RCT-2026-000001")]
    public void Format_BuiltInKey_ProducesBgPattern(string key, string period, long counter, string expected)
    {
        // Arrange & Act
        string formatted = _formatter.Format(key, period, counter);

        // Assert
        Assert.That(formatted, Is.EqualTo(expected));
    }

    /// <summary>A larger counter is zero-padded to the definition width without truncation.</summary>
    [Test]
    public void Format_LargeCounter_PadsToDefinitionWidth()
    {
        // Arrange & Act
        string formatted = _formatter.Format("JE", "2026", 123_456L);

        // Assert
        Assert.That(formatted, Is.EqualTo("JE-2026-123456"));
    }

    /// <summary>A counter wider than the padding width is not truncated.</summary>
    [Test]
    public void Format_CounterWiderThanPadding_IsNotTruncated()
    {
        // Arrange & Act
        string formatted = _formatter.Format("JE", "2026", 12_345_678L);

        // Assert
        Assert.That(formatted, Is.EqualTo("JE-2026-12345678"));
    }

    /// <summary>An empty period segment yields the prefix-counter form without a period component.</summary>
    [Test]
    public void Format_EmptyPeriodSegment_OmitsPeriodComponent()
    {
        // Arrange & Act
        string formatted = _formatter.Format("PAY", string.Empty, 7L);

        // Assert
        Assert.That(formatted, Is.EqualTo("PAY-000007"));
    }

    /// <summary>An unregistered key is rejected with the UNKNOWN_SEQUENCE_KEY error code.</summary>
    [Test]
    public void Format_UnregisteredKey_ThrowsArgumentExceptionWithUnknownKeyCode()
    {
        // Arrange & Act & Assert
        Assert.That(
            () => _formatter.Format("NOPE", "2026", 1L),
            Throws.TypeOf<ArgumentException>()
                .With.Message.Contains(SequenceErrorCodes.UNKNOWN_SEQUENCE_KEY));
    }
}
