namespace Finance.Payments.API.Validators;

/// <summary>
/// The single base64 <c>rowversion</c> shape predicate shared by the allocation validators
/// (SDD-PAY-002 §3.1). A malformed token is a <c>CONCURRENT_MODIFICATION</c>, exactly as a stale one is: the
/// caller round-trips the token it read, so anything else means the caller's view of the row is not the row.
/// </summary>
public static class RowVersionTokenRule
{
    /// <summary>Determines whether the supplied token is a decodable base64 string.</summary>
    /// <param name="rowVersion">The candidate base64 <c>rowversion</c> token.</param>
    /// <returns><c>true</c> when the token decodes; otherwise <c>false</c>.</returns>
    public static bool IsBase64(string? rowVersion)
    {
        if (string.IsNullOrWhiteSpace(rowVersion))
        {
            return false;
        }

        Span<byte> buffer = new byte[rowVersion.Length];
        return Convert.TryFromBase64String(rowVersion, buffer, out _);
    }
}
