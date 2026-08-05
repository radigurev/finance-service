using Finance.Common.ErrorCodes;
using Finance.Common.Results;
using Finance.Payments.API.Services;
using NUnit.Framework;

namespace Finance.Payments.API.Tests.Unit.Services;

/// <summary>
/// Unit tests for the PURE <see cref="AgingBucketCalculator"/> (SDD-PAY-003 §2.4, §6.2). It touches no database, no
/// clock, and no configuration, so bucket assignment is testable on its own: the documented default boundaries, the
/// custom boundary set, the exhaustive and mutually exclusive assignment, the boundary days, the date-parts-only
/// days-past-due arithmetic, and the three rejection rules.
/// </summary>
[TestFixture]
[Category("SDD-PAY-003")]
public sealed class AgingBucketCalculatorTests
{
    private AgingBucketCalculator _sut = null!;

    /// <summary>Creates a fresh calculator before each test.</summary>
    [SetUp]
    public void SetUp()
    {
        _sut = new AgingBucketCalculator();
    }

    [Test]
    public void Buckets_DefaultBoundaries_ProduceCurrent1To30_31To60_61To90_90Plus()
    {
        // Arrange
        IReadOnlyList<int>? noBoundaries = null;

        // Act
        Result<IReadOnlyList<AgingBucketDefinition>> result = _sut.Build(noBoundaries);

        // Assert
        Assert.That(result.IsSuccess, Is.True, result.ErrorCode);
        IReadOnlyList<AgingBucketDefinition> buckets = result.Value!;
        Assert.Multiple(() =>
        {
            Assert.That(
                buckets.Select(bucket => bucket.Label),
                Is.EqualTo(new[] { "Current", "1-30", "31-60", "61-90", "90+" }));
            Assert.That(buckets[0].FromDaysPastDue, Is.Null);
            Assert.That(buckets[0].ToDaysPastDue, Is.EqualTo(0));
            Assert.That(buckets[4].FromDaysPastDue, Is.EqualTo(91));
            Assert.That(buckets[4].ToDaysPastDue, Is.Null);
            Assert.That(_sut.ResolveBoundaries(noBoundaries), Is.EqualTo(new[] { 30, 60, 90 }));
        });
    }

    [Test]
    public void Buckets_DueDateEqualsAsOfDate_AssignedToCurrent()
    {
        // Arrange
        IReadOnlyList<AgingBucketDefinition> buckets = DefaultBuckets();
        DateTimeOffset dueDate = new(2026, 6, 15, 23, 0, 0, TimeSpan.Zero);
        DateTimeOffset asOfDate = new(2026, 6, 15, 1, 0, 0, TimeSpan.Zero);

        // Act
        int daysPastDue = AgingBucketCalculator.ComputeDaysPastDue(dueDate, asOfDate);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(daysPastDue, Is.Zero);
            Assert.That(buckets[AgingBucketCalculator.Assign(buckets, daysPastDue)].Label, Is.EqualTo("Current"));
        });
    }

    [Test]
    public void Buckets_NotYetDueItem_AssignedToCurrent_NegativeDaysPastDue()
    {
        // Arrange
        IReadOnlyList<AgingBucketDefinition> buckets = DefaultBuckets();
        DateTimeOffset dueDate = new(2026, 7, 15, 0, 0, 0, TimeSpan.Zero);
        DateTimeOffset asOfDate = new(2026, 6, 15, 0, 0, 0, TimeSpan.Zero);

        // Act
        int daysPastDue = AgingBucketCalculator.ComputeDaysPastDue(dueDate, asOfDate);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(daysPastDue, Is.EqualTo(-30));
            Assert.That(buckets[AgingBucketCalculator.Assign(buckets, daysPastDue)].Label, Is.EqualTo("Current"));
        });
    }

    [Test]
    public void Buckets_OneDayPastDue_AssignedTo1To30()
    {
        // Arrange
        IReadOnlyList<AgingBucketDefinition> buckets = DefaultBuckets();

        // Act
        int index = AgingBucketCalculator.Assign(buckets, 1);

        // Assert
        Assert.That(buckets[index].Label, Is.EqualTo("1-30"));
    }

    [Test]
    public void Buckets_ThirtyDaysPastDue_AssignedTo1To30_ThirtyOne_AssignedTo31To60()
    {
        // Arrange
        IReadOnlyList<AgingBucketDefinition> buckets = DefaultBuckets();

        // Act
        int thirty = AgingBucketCalculator.Assign(buckets, 30);
        int thirtyOne = AgingBucketCalculator.Assign(buckets, 31);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(buckets[thirty].Label, Is.EqualTo("1-30"));
            Assert.That(buckets[thirtyOne].Label, Is.EqualTo("31-60"));
        });
    }

    [Test]
    public void Buckets_NinetyDaysPastDue_AssignedTo61To90_NinetyOne_AssignedTo90Plus()
    {
        // Arrange
        IReadOnlyList<AgingBucketDefinition> buckets = DefaultBuckets();

        // Act
        int ninety = AgingBucketCalculator.Assign(buckets, 90);
        int ninetyOne = AgingBucketCalculator.Assign(buckets, 91);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(buckets[ninety].Label, Is.EqualTo("61-90"));
            Assert.That(buckets[ninetyOne].Label, Is.EqualTo("90+"));
        });
    }

    [Test]
    public void Buckets_CustomBoundaries_ProduceRequestedBucketSet()
    {
        // Arrange
        int[] boundaries = [15, 30, 60];

        // Act
        Result<IReadOnlyList<AgingBucketDefinition>> result = _sut.Build(boundaries);

        // Assert
        Assert.That(result.IsSuccess, Is.True, result.ErrorCode);
        Assert.Multiple(() =>
        {
            Assert.That(
                result.Value!.Select(bucket => bucket.Label),
                Is.EqualTo(new[] { "Current", "1-15", "16-30", "31-60", "60+" }));
            Assert.That(_sut.ResolveBoundaries(boundaries), Is.EqualTo(boundaries));
        });
    }

    [Test]
    public void Buckets_AreExhaustiveAndMutuallyExclusive_EveryItemInExactlyOne()
    {
        // Arrange
        IReadOnlyList<AgingBucketDefinition> buckets = DefaultBuckets();

        // Act
        IReadOnlyList<int> assignments =
            [.. Enumerable.Range(-40, 240).Select(days => AgingBucketCalculator.Assign(buckets, days))];

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(assignments, Has.All.GreaterThanOrEqualTo(0));
            Assert.That(assignments, Has.All.LessThan(buckets.Count));
            Assert.That(assignments.Distinct().Count(), Is.EqualTo(buckets.Count), "every bucket is reachable");
        });
    }

    [Test]
    public void Buckets_NonAscendingBoundaries_ReturnsInvalidAgingBuckets()
    {
        // Arrange
        int[] boundaries = [30, 30, 90];

        // Act
        Result<IReadOnlyList<AgingBucketDefinition>> result = _sut.Build(boundaries);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo(PaymentErrorCodes.INVALID_AGING_BUCKETS));
        });
    }

    [TestCase(0)]
    [TestCase(-15)]
    public void Buckets_NonPositiveBoundary_ReturnsInvalidAgingBuckets(int boundary)
    {
        // Arrange
        int[] boundaries = [boundary, 60];

        // Act
        Result<IReadOnlyList<AgingBucketDefinition>> result = _sut.Build(boundaries);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo(PaymentErrorCodes.INVALID_AGING_BUCKETS));
        });
    }

    [Test]
    public void Buckets_MoreThanSixBoundaries_ReturnsInvalidAgingBuckets()
    {
        // Arrange
        int[] boundaries = [10, 20, 30, 40, 50, 60, 70];

        // Act
        Result<IReadOnlyList<AgingBucketDefinition>> result = _sut.Build(boundaries);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(boundaries, Has.Length.GreaterThan(AgingBucketCalculator.MaxDayBoundaryCount));
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo(PaymentErrorCodes.INVALID_AGING_BUCKETS));
        });
    }

    [Test]
    public void Buckets_DaysPastDue_ComputedOnDatePartsOnly_IgnoresTimeOfDay()
    {
        // Arrange
        DateTimeOffset dueDateLateInTheDay = new(2026, 6, 1, 23, 59, 59, TimeSpan.Zero);
        DateTimeOffset asOfDateEarlyInTheDay = new(2026, 6, 2, 0, 0, 1, TimeSpan.Zero);

        // Act
        int daysPastDue = AgingBucketCalculator.ComputeDaysPastDue(dueDateLateInTheDay, asOfDateEarlyInTheDay);

        // Assert
        Assert.That(
            daysPastDue,
            Is.EqualTo(1),
            "one whole calendar day apart regardless of the two DATETIMEOFFSET time components");
    }

    /// <summary>Builds the documented default bucket set for the assignment assertions.</summary>
    /// <returns>The effective default buckets in bucket order.</returns>
    private IReadOnlyList<AgingBucketDefinition> DefaultBuckets()
    {
        Result<IReadOnlyList<AgingBucketDefinition>> result = _sut.Build(dayBoundaries: null);
        Assert.That(result.IsSuccess, Is.True, result.ErrorCode);
        return result.Value!;
    }
}
