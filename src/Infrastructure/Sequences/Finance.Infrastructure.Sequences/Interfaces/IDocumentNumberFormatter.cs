namespace Finance.Infrastructure.Sequences.Interfaces;

/// <summary>
/// Formatting seam for document numbers (SDD-INFRA-003 §2.6). The generator delegates all
/// prefix/padding/fiscal-year shaping here and never inlines that logic. The shipped
/// <c>DefaultDocumentNumberFormatter</c> emits the BG-style patterns of §2.1; when SDD-CTRY-001
/// is authored, a country-specific formatter MAY replace it via DI with no change to the generator.
/// </summary>
public interface IDocumentNumberFormatter
{
    /// <summary>
    /// Formats the supplied counter into a full document number for <paramref name="sequenceKey"/>.
    /// </summary>
    /// <param name="sequenceKey">The registered sequence key being formatted (e.g. <c>JE</c>).</param>
    /// <param name="periodSegment">The reset-policy date segment (e.g. <c>2026</c> for Yearly), or empty for Never.</param>
    /// <param name="counter">The freshly allocated sequential counter value (1-based).</param>
    /// <returns>The formatted document number (e.g. <c>JE-2026-000001</c>).</returns>
    string Format(string sequenceKey, string periodSegment, long counter);
}
