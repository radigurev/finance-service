using Finance.Common.ErrorCodes;
using Finance.Common.Results;

namespace Finance.Payments.API.Services;

/// <summary>
/// The PURE aging bucket calculator (SDD-PAY-003 §2.4): it builds the effective bucket set from ascending day
/// boundaries, computes days past due on DATE parts only, and assigns an item to exactly one bucket. It touches no
/// database, no clock, and no configuration, so it is unit-testable on its own — which is why bucket assignment
/// lives here and is never inlined into a query or a service method.
/// <para>The documented default boundaries are <c>30, 60, 90</c>, yielding the five buckets <c>Current</c>,
/// <c>1-30</c>, <c>31-60</c>, <c>61-90</c>, <c>90+</c>. A caller may supply up to six strictly ascending positive
/// boundaries instead; anything else is rejected with <c>INVALID_AGING_BUCKETS</c> before any query runs.</para>
/// <para>The buckets are EXHAUSTIVE and MUTUALLY EXCLUSIVE by construction: <c>Current</c> absorbs every
/// days-past-due value of <c>0</c> or less, each intermediate bucket spans the half-open gap to its boundary, and
/// the final bucket absorbs everything strictly beyond the last boundary. Consequently the sum of the bucket
/// amounts always equals the row total to the cent.</para>
/// </summary>
public sealed class AgingBucketCalculator
{
    /// <summary>The maximum number of day boundaries a caller may supply (seven buckets including <c>Current</c>).</summary>
    public const int MaxDayBoundaryCount = 6;

    /// <summary>The label of the first, open-ended-below bucket holding every not-yet-due item.</summary>
    public const string CurrentBucketLabel = "Current";

    private static readonly int[] DefaultDayBoundaries = [30, 60, 90];

    /// <summary>
    /// Builds the effective bucket set from the supplied ascending day boundaries, falling back to the documented
    /// default <c>30, 60, 90</c> when the caller supplies none.
    /// </summary>
    /// <param name="dayBoundaries">The caller's ascending positive day boundaries, or <c>null</c>/empty for the default.</param>
    /// <returns>
    /// The effective buckets in bucket order, or <c>INVALID_AGING_BUCKETS</c> when the boundaries are not strictly
    /// ascending, contain a non-positive value, or exceed <see cref="MaxDayBoundaryCount"/>.
    /// </returns>
    public Result<IReadOnlyList<AgingBucketDefinition>> Build(IReadOnlyList<int>? dayBoundaries)
    {
        IReadOnlyList<int> effective = ResolveBoundaries(dayBoundaries);

        Result validated = Validate(effective);
        if (!validated.IsSuccess)
        {
            return Result<IReadOnlyList<AgingBucketDefinition>>.Failure(validated.ErrorCode!, validated.Detail);
        }

        return Result<IReadOnlyList<AgingBucketDefinition>>.Success(Compose(effective));
    }

    /// <summary>
    /// Returns the effective day boundaries the supplied request resolves to, without validating them. Used to
    /// echo the boundaries onto the response alongside the labels.
    /// </summary>
    /// <param name="dayBoundaries">The caller's boundaries, or <c>null</c>/empty for the default.</param>
    /// <returns>The effective ascending day boundaries.</returns>
    public IReadOnlyList<int> ResolveBoundaries(IReadOnlyList<int>? dayBoundaries)
    {
        if (dayBoundaries is null || dayBoundaries.Count == 0)
        {
            return DefaultDayBoundaries;
        }

        return dayBoundaries;
    }

    /// <summary>
    /// Computes the whole number of days from a due date to an as-of date using the DATE parts only, so the time
    /// of day carried by either <c>DATETIMEOFFSET</c> can never shift the result (SDD-PAY-003 §2.2). An item due
    /// exactly on the as-of date yields <c>0</c>; a not-yet-due item yields a negative value.
    /// </summary>
    /// <param name="dueDate">The invoice payment due date.</param>
    /// <param name="asOfDate">The as-of date the view is computed at.</param>
    /// <returns>The days past due, which may be zero or negative.</returns>
    public static int ComputeDaysPastDue(DateTimeOffset dueDate, DateTimeOffset asOfDate)
    {
        DateOnly due = DateOnly.FromDateTime(dueDate.UtcDateTime);
        DateOnly asOf = DateOnly.FromDateTime(asOfDate.UtcDateTime);
        return asOf.DayNumber - due.DayNumber;
    }

    /// <summary>
    /// Assigns a days-past-due value to exactly one bucket index. Every value lands somewhere: a value of
    /// <c>0</c> or less lands in <c>Current</c>, and a value beyond the last boundary lands in the open-ended
    /// final bucket.
    /// </summary>
    /// <param name="buckets">The effective buckets in bucket order.</param>
    /// <param name="daysPastDue">The item's days past due.</param>
    /// <returns>The zero-based index of the owning bucket.</returns>
    public static int Assign(IReadOnlyList<AgingBucketDefinition> buckets, int daysPastDue)
    {
        ArgumentNullException.ThrowIfNull(buckets);

        if (daysPastDue <= 0)
        {
            return 0;
        }

        for (int index = 1; index < buckets.Count; index++)
        {
            int? upperBound = buckets[index].ToDaysPastDue;
            if (upperBound is null || daysPastDue <= upperBound.Value)
            {
                return index;
            }
        }

        return buckets.Count - 1;
    }

    private static Result Validate(IReadOnlyList<int> dayBoundaries)
    {
        if (dayBoundaries.Count > MaxDayBoundaryCount)
        {
            return Result.Failure(
                PaymentErrorCodes.INVALID_AGING_BUCKETS,
                $"At most {MaxDayBoundaryCount} bucket day boundaries may be supplied; {dayBoundaries.Count} were.");
        }

        for (int index = 0; index < dayBoundaries.Count; index++)
        {
            if (dayBoundaries[index] <= 0)
            {
                return Result.Failure(
                    PaymentErrorCodes.INVALID_AGING_BUCKETS,
                    "Bucket day boundaries must be strictly positive integers.");
            }

            if (index > 0 && dayBoundaries[index] <= dayBoundaries[index - 1])
            {
                return Result.Failure(
                    PaymentErrorCodes.INVALID_AGING_BUCKETS,
                    "Bucket day boundaries must be strictly ascending.");
            }
        }

        return Result.Success();
    }

    private static IReadOnlyList<AgingBucketDefinition> Compose(IReadOnlyList<int> dayBoundaries)
    {
        List<AgingBucketDefinition> buckets = new(dayBoundaries.Count + 2)
        {
            new AgingBucketDefinition
            {
                Label = CurrentBucketLabel,
                FromDaysPastDue = null,
                ToDaysPastDue = 0
            }
        };

        int lowerBound = 1;
        foreach (int boundary in dayBoundaries)
        {
            buckets.Add(new AgingBucketDefinition
            {
                Label = $"{lowerBound}-{boundary}",
                FromDaysPastDue = lowerBound,
                ToDaysPastDue = boundary
            });

            lowerBound = boundary + 1;
        }

        int lastBoundary = dayBoundaries[^1];
        buckets.Add(new AgingBucketDefinition
        {
            Label = $"{lastBoundary}+",
            FromDaysPastDue = lastBoundary + 1,
            ToDaysPastDue = null
        });

        return buckets;
    }
}
