namespace Finance.Infrastructure.Services;

/// <summary>
/// Enforces "exactly one primary" semantics on collections (e.g. a counterparty's primary email)
/// per SDD-INFRA-009 §2.3.
/// </summary>
public static class PrimaryFlagHelper
{
    /// <summary>
    /// Ensures exactly one item in <paramref name="items"/> is flagged primary: when none are
    /// flagged the first is flagged; when several are flagged only the first stays; an empty
    /// collection is a no-op.
    /// </summary>
    /// <typeparam name="T">The collection item type.</typeparam>
    /// <param name="items">The collection to normalize.</param>
    /// <param name="getIsPrimary">Reads the primary flag from an item.</param>
    /// <param name="setIsPrimary">Writes the primary flag onto an item.</param>
    public static void EnsureSinglePrimary<T>(
        IList<T> items, Func<T, bool> getIsPrimary, Action<T, bool> setIsPrimary)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(getIsPrimary);
        ArgumentNullException.ThrowIfNull(setIsPrimary);

        if (items.Count == 0)
        {
            return;
        }

        bool primarySeen = false;
        foreach (T item in items)
        {
            if (getIsPrimary(item) && !primarySeen)
            {
                primarySeen = true;
            }
            else if (getIsPrimary(item))
            {
                setIsPrimary(item, false);
            }
        }

        if (!primarySeen)
        {
            setIsPrimary(items[0], true);
        }
    }
}
