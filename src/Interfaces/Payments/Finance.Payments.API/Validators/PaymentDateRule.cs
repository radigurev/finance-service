namespace Finance.Payments.API.Validators;

/// <summary>
/// The shared <c>PaymentDate</c> shape rule (SDD-PAY-001 §3.1): the date MUST be present and MUST NOT be in
/// the future, with the upper bound taken from <see cref="TimeProvider.GetUtcNow"/> and evaluated at
/// whole-day granularity. Cash cannot be recorded as having moved before it moves, and a future date would
/// also draw a number from a series the books have not reached.
/// </summary>
public static class PaymentDateRule
{
    /// <summary>
    /// Determines whether the supplied payment date is present and not later than the clock's current day.
    /// </summary>
    /// <param name="paymentDate">The candidate payment date.</param>
    /// <param name="timeProvider">The clock supplying the upper bound.</param>
    /// <returns><c>true</c> when the date is set and on or before today; otherwise <c>false</c>.</returns>
    public static bool IsWithinBounds(DateTimeOffset paymentDate, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);

        if (paymentDate == default)
        {
            return false;
        }

        DateOnly today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        DateOnly candidate = DateOnly.FromDateTime(paymentDate.UtcDateTime);
        return candidate <= today;
    }
}
