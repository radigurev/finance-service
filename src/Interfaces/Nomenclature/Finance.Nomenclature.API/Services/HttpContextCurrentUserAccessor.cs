using System.Security.Claims;
using Finance.Nomenclature.API.Interfaces;

namespace Finance.Nomenclature.API.Services;

/// <summary>
/// <see cref="IHttpContextAccessor"/>-backed <see cref="ICurrentUserAccessor"/> that reads the
/// authenticated user's identity from the JWT claims established by the shared authentication
/// (SDD-INT-AUTH-001, SDD-AUDIT-001 §2.3).
/// </summary>
public sealed class HttpContextCurrentUserAccessor : ICurrentUserAccessor
{
    private const string SystemUsername = "system";

    private static readonly string[] UserIdClaimTypes =
    [
        ClaimTypes.NameIdentifier,
        "sub",
        "nameid",
        "uid"
    ];

    private static readonly string[] UsernameClaimTypes =
    [
        ClaimTypes.Name,
        "name",
        "unique_name",
        "preferred_username",
        ClaimTypes.Email,
        "email"
    ];

    private readonly IHttpContextAccessor _httpContextAccessor;

    /// <summary>Creates a new <see cref="HttpContextCurrentUserAccessor"/>.</summary>
    /// <param name="httpContextAccessor">The accessor for the ambient HTTP context.</param>
    public HttpContextCurrentUserAccessor(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    /// <inheritdoc />
    public Guid GetUserId()
    {
        ClaimsPrincipal? principal = _httpContextAccessor.HttpContext?.User;
        if (principal is null)
        {
            return Guid.Empty;
        }

        string? raw = FindFirstClaim(principal, UserIdClaimTypes);
        return Guid.TryParse(raw, out Guid userId) ? userId : Guid.Empty;
    }

    /// <inheritdoc />
    public string GetUsername()
    {
        ClaimsPrincipal? principal = _httpContextAccessor.HttpContext?.User;
        if (principal is null)
        {
            return SystemUsername;
        }

        string? username = FindFirstClaim(principal, UsernameClaimTypes);
        return string.IsNullOrWhiteSpace(username) ? SystemUsername : username;
    }

    private static string? FindFirstClaim(ClaimsPrincipal principal, IReadOnlyList<string> claimTypes)
    {
        foreach (string claimType in claimTypes)
        {
            string? value = principal.FindFirstValue(claimType);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }
}
