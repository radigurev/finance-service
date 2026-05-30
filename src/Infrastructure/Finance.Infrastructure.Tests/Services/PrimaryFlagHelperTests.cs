using Finance.Infrastructure.Services;
using NUnit.Framework;

namespace Finance.Infrastructure.Tests.Services;

/// <summary>
/// Unit tests for <see cref="PrimaryFlagHelper"/> covering the three cases of SDD-INFRA-009 §2.3.
/// </summary>
[TestFixture]
[Category("SDD-INFRA-009")]
public sealed class PrimaryFlagHelperTests
{
    /// <summary>When none are flagged primary, the first item is flagged.</summary>
    [Test]
    public void PrimaryFlagHelper_FlagsFirst_WhenNoneFlagged()
    {
        // Arrange
        List<Flagged> items =
        [
            new Flagged { IsPrimary = false },
            new Flagged { IsPrimary = false }
        ];

        // Act
        PrimaryFlagHelper.EnsureSinglePrimary(items, item => item.IsPrimary, (item, value) => item.IsPrimary = value);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(items[0].IsPrimary, Is.True);
            Assert.That(items[1].IsPrimary, Is.False);
        });
    }

    /// <summary>When several are flagged primary, only the first flagged item remains primary.</summary>
    [Test]
    public void PrimaryFlagHelper_KeepsOnlyFirstPrimary_WhenMultipleFlagged()
    {
        // Arrange
        List<Flagged> items =
        [
            new Flagged { IsPrimary = false },
            new Flagged { IsPrimary = true },
            new Flagged { IsPrimary = true }
        ];

        // Act
        PrimaryFlagHelper.EnsureSinglePrimary(items, item => item.IsPrimary, (item, value) => item.IsPrimary = value);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(items[0].IsPrimary, Is.False);
            Assert.That(items[1].IsPrimary, Is.True);
            Assert.That(items[2].IsPrimary, Is.False);
        });
    }

    /// <summary>An empty collection is a no-op.</summary>
    [Test]
    public void PrimaryFlagHelper_IsNoOp_OnEmptyList()
    {
        // Arrange
        List<Flagged> items = [];

        // Act
        TestDelegate act = () => PrimaryFlagHelper.EnsureSinglePrimary(
            items, item => item.IsPrimary, (item, value) => item.IsPrimary = value);

        // Assert
        Assert.That(act, Throws.Nothing);
        Assert.That(items, Is.Empty);
    }

    private sealed class Flagged
    {
        public bool IsPrimary { get; set; }
    }
}
