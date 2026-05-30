namespace Finance.Infrastructure.Sequences.Interfaces;

/// <summary>
/// Produces gapless, formatted, unique document numbers per the НАП requirement
/// (SDD-INFRA-003 §2.2). Implementations increment a single counter row under
/// <c>UPDLOCK, HOLDLOCK</c> inside a serializable transaction and never cache.
/// </summary>
public interface ISequenceGenerator
{
    /// <summary>
    /// Allocates the next sequential number for <paramref name="sequenceKey"/> and returns it
    /// formatted by the registered <see cref="IDocumentNumberFormatter"/>.
    /// </summary>
    /// <param name="sequenceKey">A registered sequence key (e.g. <c>JE</c>, <c>PINV</c>). Non-empty.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>The formatted document number (e.g. <c>JE-2026-000001</c>).</returns>
    /// <exception cref="System.ArgumentException">Thrown when the key is empty or not registered.</exception>
    Task<string> NextAsync(string sequenceKey, CancellationToken cancellationToken);
}
