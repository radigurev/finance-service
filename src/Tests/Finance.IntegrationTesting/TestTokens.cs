using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace Finance.IntegrationTesting;

/// <summary>
/// Mints HS256 JWTs signed with the test secret so requests pass the real JWT Bearer validation
/// (issuer / audience / signature / lifetime) configured by <c>AddWarehouseAuthentication</c>.
/// The <c>sub</c> claim carries the integer user id the permission handler reads.
/// </summary>
public static class TestTokens
{
    /// <summary>The shared secret used to sign and validate test tokens (≥ 32 chars per config validation).</summary>
    public const string SecretKey = "finance-integration-test-signing-key-0123456789";

    /// <summary>The issuer the test host validates against.</summary>
    public const string Issuer = "Warehouse.Auth.API";

    /// <summary>The audience the test host validates against.</summary>
    public const string Audience = "Warehouse";

    /// <summary>Creates a signed bearer token string for the given user id.</summary>
    public static string Create(int userId = 1, string userName = "integration-tester")
    {
        SymmetricSecurityKey key = new(Encoding.UTF8.GetBytes(SecretKey));
        SigningCredentials credentials = new(key, SecurityAlgorithms.HmacSha256);

        Claim[] claims =
        [
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim(JwtRegisteredClaimNames.UniqueName, userName)
        ];

        JwtSecurityToken token = new(
            issuer: Issuer,
            audience: Audience,
            claims: claims,
            notBefore: DateTime.UtcNow.AddMinutes(-1),
            expires: DateTime.UtcNow.AddMinutes(30),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
