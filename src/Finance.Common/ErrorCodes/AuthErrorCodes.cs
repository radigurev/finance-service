namespace Finance.Common.ErrorCodes;

/// <summary>
/// Machine-readable error codes for authentication and authorization failures.
/// Used as the <c>title</c> field of ProblemDetails responses.
/// </summary>
public static class AuthErrorCodes
{
    /// <summary>The request did not carry a bearer token.</summary>
    public const string MISSING_TOKEN = nameof(MISSING_TOKEN);

    /// <summary>The supplied token failed validation (signature, expiry, or issuer).</summary>
    public const string INVALID_TOKEN = nameof(INVALID_TOKEN);

    /// <summary>The authenticated principal lacks the permission required for the operation.</summary>
    public const string INSUFFICIENT_PERMISSIONS = nameof(INSUFFICIENT_PERMISSIONS);
}
