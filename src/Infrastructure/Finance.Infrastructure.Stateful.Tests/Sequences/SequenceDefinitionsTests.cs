using Finance.Infrastructure.Sequences;
using NUnit.Framework;

namespace Finance.Infrastructure.Stateful.Tests.Sequences;

/// <summary>
/// Unit tests for the built-in <see cref="SequenceDefinitions"/> registry (SDD-INFRA-003 §2.1):
/// the seven finance keys are present, unique, default to <see cref="SequenceResetPolicy.Yearly"/>
/// with padding 6, and carry the BG-default prefixes within the documented padding bounds.
/// </summary>
[TestFixture]
[Category("SDD-INFRA-003")]
public sealed class SequenceDefinitionsTests
{
    /// <summary>All seven built-in keys from §2.1 are registered.</summary>
    [Test]
    public void BuiltIn_RegistersAllSevenKeys()
    {
        // Arrange & Act
        IReadOnlyDictionary<string, SequenceDefinition> definitions = SequenceDefinitions.BuiltIn;

        // Assert
        Assert.That(definitions.Keys, Is.EquivalentTo(new[] { "JE", "PINV", "SINV", "CN", "DN", "PAY", "RCT" }));
    }

    /// <summary>The built-in keys are unique (no duplicate registration).</summary>
    [Test]
    public void BuiltIn_AllKeysAreUnique()
    {
        // Arrange & Act
        IEnumerable<string> keys = SequenceDefinitions.BuiltIn.Values.Select(definition => definition.Key);

        // Assert
        Assert.That(keys, Is.Unique);
    }

    /// <summary>Every built-in definition uses the НАП-required Yearly reset policy.</summary>
    [Test]
    public void BuiltIn_AllUseYearlyResetPolicy()
    {
        // Arrange & Act
        bool allYearly = SequenceDefinitions.BuiltIn.Values
            .All(definition => definition.ResetPolicy == SequenceResetPolicy.Yearly);

        // Assert
        Assert.That(allYearly, Is.True);
    }

    /// <summary>Every built-in definition padding is within the documented [1, 12] bounds.</summary>
    [Test]
    public void BuiltIn_AllPaddingWithinBounds()
    {
        // Arrange & Act
        bool allWithinBounds = SequenceDefinitions.BuiltIn.Values
            .All(definition => definition.Padding is >= 1 and <= 12);

        // Assert
        Assert.That(allWithinBounds, Is.True);
    }

    /// <summary>Each key maps to the BG-default prefix from §2.1.</summary>
    [TestCase("JE", "JE")]
    [TestCase("PINV", "ФПок")]
    [TestCase("SINV", "ФПр")]
    [TestCase("CN", "КИ")]
    [TestCase("DN", "ДИ")]
    [TestCase("PAY", "PAY")]
    [TestCase("RCT", "RCT")]
    public void BuiltIn_KeyMapsToBgDefaultPrefix(string key, string expectedPrefix)
    {
        // Arrange & Act
        SequenceDefinition definition = SequenceDefinitions.BuiltIn[key];

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(definition.Prefix, Is.EqualTo(expectedPrefix));
            Assert.That(definition.Padding, Is.EqualTo(6));
        });
    }

    /// <summary>The <see cref="SequenceKeys"/> constants correspond to the registered keys.</summary>
    [Test]
    public void SequenceKeys_Constants_MatchRegisteredKeys()
    {
        // Arrange
        string[] constants =
        [
            SequenceKeys.JournalEntry,
            SequenceKeys.PurchaseInvoice,
            SequenceKeys.SaleInvoice,
            SequenceKeys.CreditNote,
            SequenceKeys.DebitNote,
            SequenceKeys.Payment,
            SequenceKeys.Receipt
        ];

        // Act & Assert
        Assert.That(constants, Is.EquivalentTo(SequenceDefinitions.BuiltIn.Keys));
    }
}
