using Finance.Infrastructure.Sequences;
using Finance.Infrastructure.Stateful.Tests.Sequences.Fixtures;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace Finance.Infrastructure.Stateful.Tests.Sequences;

/// <summary>
/// Unit tests for the registration-time padding guard of <see cref="SequenceDefinitions.ValidatePadding(SequenceDefinition)"/>
/// and <see cref="SequenceGeneratorServiceCollectionExtensions.AddSequenceGenerator{TDbContext}"/>
/// (SDD-INFRA-003 §3). A definition whose <see cref="SequenceDefinition.Padding"/> falls outside the
/// inclusive 1..12 bound MUST fail fast with <see cref="ArgumentOutOfRangeException"/>; an in-range value
/// MUST pass. The built-in registry feeds <c>AddSequenceGenerator</c>, which validates before wiring.
/// </summary>
[TestFixture]
[Category("SDD-INFRA-003")]
public sealed class SequenceGeneratorServiceCollectionExtensionsTests
{
    /// <summary>A single definition with padding below the minimum throws ArgumentOutOfRangeException.</summary>
    [Test]
    public void ValidatePadding_SingleDefinitionBelowMinimum_ThrowsArgumentOutOfRangeException()
    {
        // Arrange
        SequenceDefinition definition = new()
        {
            Key = "JE",
            Prefix = "JE",
            Padding = SequenceDefinitions.MinPadding - 1,
            ResetPolicy = SequenceResetPolicy.Yearly
        };

        // Act & Assert
        Assert.That(
            () => SequenceDefinitions.ValidatePadding(definition),
            Throws.TypeOf<ArgumentOutOfRangeException>());
    }

    /// <summary>A single definition with padding above the maximum throws ArgumentOutOfRangeException.</summary>
    [Test]
    public void ValidatePadding_SingleDefinitionAboveMaximum_ThrowsArgumentOutOfRangeException()
    {
        // Arrange
        SequenceDefinition definition = new()
        {
            Key = "JE",
            Prefix = "JE",
            Padding = SequenceDefinitions.MaxPadding + 1,
            ResetPolicy = SequenceResetPolicy.Yearly
        };

        // Act & Assert
        Assert.That(
            () => SequenceDefinitions.ValidatePadding(definition),
            Throws.TypeOf<ArgumentOutOfRangeException>());
    }

    /// <summary>The boundary padding values 1 and 12 are accepted without throwing.</summary>
    [TestCase(SequenceDefinitions.MinPadding)]
    [TestCase(SequenceDefinitions.MaxPadding)]
    [TestCase(6)]
    public void ValidatePadding_SingleDefinitionWithinBounds_DoesNotThrow(int padding)
    {
        // Arrange
        SequenceDefinition definition = new()
        {
            Key = "JE",
            Prefix = "JE",
            Padding = padding,
            ResetPolicy = SequenceResetPolicy.Yearly
        };

        // Act & Assert
        Assert.That(() => SequenceDefinitions.ValidatePadding(definition), Throws.Nothing);
    }

    /// <summary>A dictionary containing one out-of-range definition throws ArgumentOutOfRangeException.</summary>
    [Test]
    public void ValidatePadding_DictionaryWithOutOfRangeDefinition_ThrowsArgumentOutOfRangeException()
    {
        // Arrange
        IReadOnlyDictionary<string, SequenceDefinition> definitions = new Dictionary<string, SequenceDefinition>
        {
            ["JE"] = new()
            {
                Key = "JE",
                Prefix = "JE",
                Padding = 6,
                ResetPolicy = SequenceResetPolicy.Yearly
            },
            ["BAD"] = new()
            {
                Key = "BAD",
                Prefix = "BAD",
                Padding = 0,
                ResetPolicy = SequenceResetPolicy.Yearly
            }
        };

        // Act & Assert
        Assert.That(
            () => SequenceDefinitions.ValidatePadding(definitions),
            Throws.TypeOf<ArgumentOutOfRangeException>());
    }

    /// <summary>A null definitions dictionary throws ArgumentNullException.</summary>
    [Test]
    public void ValidatePadding_NullDictionary_ThrowsArgumentNullException()
    {
        // Arrange
        IReadOnlyDictionary<string, SequenceDefinition> definitions = null!;

        // Act & Assert
        Assert.That(
            () => SequenceDefinitions.ValidatePadding(definitions),
            Throws.TypeOf<ArgumentNullException>());
    }

    /// <summary>The built-in registry of padding-6 definitions passes registration-time validation.</summary>
    [Test]
    public void AddSequenceGenerator_BuiltInDefinitions_DoesNotThrow()
    {
        // Arrange
        ServiceCollection services = new();

        // Act & Assert
        Assert.That(
            () => services.AddSequenceGenerator<TestSequenceDbContext>(),
            Throws.Nothing);
    }
}
