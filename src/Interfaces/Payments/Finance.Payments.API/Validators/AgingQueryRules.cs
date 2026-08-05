using System.Text.RegularExpressions;
using Finance.Common.Enums;

namespace Finance.Payments.API.Validators;

/// <summary>
/// The shared SHAPE rules of the SDD-PAY-003 aging query surface (§3.1). They are stated ONCE here and read by
/// both the FluentValidation request validators and <c>AgingService</c>, so the pre-binding rejection and the
/// service-level guard can never disagree about what a legal narrowing is.
/// <para>All three rules are pure and side-effect-free; the as-of rule takes its upper bound from the injected
/// clock rather than <c>DateTimeOffset.UtcNow</c> so it stays deterministic under test.</para>
/// </summary>
public static class AgingQueryRules
{
    private static readonly string[] RecognizedDirections =
    [
        nameof(InvoiceDirection.AR),
        nameof(InvoiceDirection.AP)
    ];

    private static readonly Regex CurrencyCodePattern =
        new("^[A-Z]{3}$", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>
    /// Determines whether the supplied direction is one of the two recognized ledger directions
    /// (<c>AR</c>/<c>AP</c>), compared by exact enum member NAME.
    /// </summary>
    /// <param name="direction">The candidate direction.</param>
    /// <returns><c>true</c> when the direction is recognized; otherwise <c>false</c>.</returns>
    public static bool IsRecognizedDirection(string? direction)
    {
        if (string.IsNullOrEmpty(direction))
        {
            return false;
        }

        return RecognizedDirections.Contains(direction, StringComparer.Ordinal);
    }

    /// <summary>
    /// Determines whether the supplied currency code is a three-letter uppercase ISO 4217 alphabetic code, using
    /// the same strictness the payment currency rule already applies.
    /// </summary>
    /// <param name="currencyCode">The candidate currency code.</param>
    /// <returns><c>true</c> when the code is well formed; otherwise <c>false</c>.</returns>
    public static bool IsWellFormedCurrency(string? currencyCode)
    {
        if (string.IsNullOrEmpty(currencyCode))
        {
            return false;
        }

        return CurrencyCodePattern.IsMatch(currencyCode);
    }

    /// <summary>
    /// Determines whether the supplied as-of date is on or before the clock's current UTC day. A FUTURE date would
    /// age not-yet-due documents against a calendar that has not happened, so it is rejected before any query runs
    /// (SDD-PAY-003 §2.3).
    /// </summary>
    /// <param name="asOfDate">The candidate as-of date.</param>
    /// <param name="timeProvider">The clock supplying the upper bound.</param>
    /// <returns><c>true</c> when the date is not in the future; otherwise <c>false</c>.</returns>
    public static bool IsNotInFuture(DateTimeOffset asOfDate, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);

        DateOnly today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        DateOnly candidate = DateOnly.FromDateTime(asOfDate.UtcDateTime);
        return candidate <= today;
    }
}
