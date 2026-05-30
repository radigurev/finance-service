using System.Collections.ObjectModel;

namespace Finance.Infrastructure.Sequences;

/// <summary>
/// The canonical registry of built-in finance sequence definitions (SDD-INFRA-003 §2.1).
/// All seven keys default to <see cref="SequenceResetPolicy.Yearly"/> with padding 6 and
/// the BG-style prefixes required by НАП. Per-country definitions arrive with SDD-CTRY-001.
/// </summary>
public static class SequenceDefinitions
{
    /// <summary>The inclusive lower bound for <see cref="SequenceDefinition.Padding"/> (SDD-INFRA-003 §3).</summary>
    public const int MinPadding = 1;

    /// <summary>The inclusive upper bound for <see cref="SequenceDefinition.Padding"/> (SDD-INFRA-003 §3).</summary>
    public const int MaxPadding = 12;

    private static readonly ReadOnlyDictionary<string, SequenceDefinition> _builtIn = BuildBuiltIn();

    /// <summary>
    /// The built-in sequence definitions keyed by <see cref="SequenceDefinition.Key"/>.
    /// </summary>
    public static IReadOnlyDictionary<string, SequenceDefinition> BuiltIn => _builtIn;

    /// <summary>
    /// Validates that every supplied definition has a <see cref="SequenceDefinition.Padding"/> within
    /// the inclusive 1..12 bound required by SDD-INFRA-003 §3. Throws so a bad definition fails fast
    /// at registration time.
    /// </summary>
    /// <param name="definitions">The sequence definitions to validate.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="definitions"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when any definition's padding is outside 1..12.</exception>
    public static void ValidatePadding(IReadOnlyDictionary<string, SequenceDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);

        foreach (SequenceDefinition definition in definitions.Values)
        {
            ValidatePadding(definition);
        }
    }

    /// <summary>
    /// Validates that a single definition's <see cref="SequenceDefinition.Padding"/> is within the
    /// inclusive 1..12 bound required by SDD-INFRA-003 §3.
    /// </summary>
    /// <param name="definition">The sequence definition to validate.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="definition"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the padding is outside 1..12.</exception>
    public static void ValidatePadding(SequenceDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        if (definition.Padding < MinPadding || definition.Padding > MaxPadding)
        {
            throw new ArgumentOutOfRangeException(
                nameof(definition),
                definition.Padding,
                $"Sequence definition '{definition.Key}' has padding {definition.Padding}, "
                + $"which is outside the required range {MinPadding}..{MaxPadding} (SDD-INFRA-003 §3).");
        }
    }

    private static ReadOnlyDictionary<string, SequenceDefinition> BuildBuiltIn()
    {
        SequenceDefinition[] definitions =
        [
            new() { Key = SequenceKeys.JournalEntry, Prefix = "JE", Padding = 6, ResetPolicy = SequenceResetPolicy.Yearly },
            new() { Key = SequenceKeys.PurchaseInvoice, Prefix = "ФПок", Padding = 6, ResetPolicy = SequenceResetPolicy.Yearly },
            new() { Key = SequenceKeys.SaleInvoice, Prefix = "ФПр", Padding = 6, ResetPolicy = SequenceResetPolicy.Yearly },
            new() { Key = SequenceKeys.CreditNote, Prefix = "КИ", Padding = 6, ResetPolicy = SequenceResetPolicy.Yearly },
            new() { Key = SequenceKeys.DebitNote, Prefix = "ДИ", Padding = 6, ResetPolicy = SequenceResetPolicy.Yearly },
            new() { Key = SequenceKeys.Payment, Prefix = "PAY", Padding = 6, ResetPolicy = SequenceResetPolicy.Yearly },
            new() { Key = SequenceKeys.Receipt, Prefix = "RCT", Padding = 6, ResetPolicy = SequenceResetPolicy.Yearly }
        ];

        Dictionary<string, SequenceDefinition> map = new(StringComparer.Ordinal);
        foreach (SequenceDefinition definition in definitions)
        {
            map.Add(definition.Key, definition);
        }

        return new ReadOnlyDictionary<string, SequenceDefinition>(map);
    }
}
