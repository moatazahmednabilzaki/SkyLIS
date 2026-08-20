using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace SkyLIS.Api.Infrastructure;

/// <summary>
/// The single place access tokens are minted and the signing key is resolved.
/// Production REQUIRES Auth:SigningKey (>= 32 chars, from the environment/secret store);
/// the checked-in Auth:DevSigningKey is accepted in Development only — a production boot
/// with no real key fails fast instead of signing tokens with a public value.
/// </summary>
public sealed class TokenService
{
    private readonly SigningCredentials _credentials;
    private readonly string _issuer;
    private readonly int _accessTokenMinutes;

    public TokenService(IConfiguration configuration, IHostEnvironment environment)
    {
        var key = ResolveSigningKey(configuration, environment);
        _credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)), SecurityAlgorithms.HmacSha256);
        _issuer = configuration["Auth:Issuer"] ?? "skylis";
        _accessTokenMinutes = configuration.GetValue("Auth:AccessTokenMinutes", 60);
    }

    public int AccessTokenSeconds => _accessTokenMinutes * 60;

    public static string ResolveSigningKey(IConfiguration configuration, IHostEnvironment environment)
    {
        var key = configuration["Auth:SigningKey"];
        if (!string.IsNullOrWhiteSpace(key))
        {
            return key.Length >= 32
                ? key
                : throw new InvalidOperationException("Auth:SigningKey must be at least 32 characters.");
        }
        if (environment.IsDevelopment())
        {
            return configuration["Auth:DevSigningKey"]
                ?? throw new InvalidOperationException("Auth:DevSigningKey is not configured.");
        }
        throw new InvalidOperationException(
            "Auth:SigningKey is required outside Development. Set it via the Auth__SigningKey "
            + "environment variable (at least 32 random characters).");
    }

    public string IssueTenantToken(
        Guid userId, string userName, Guid tenantId,
        IEnumerable<string> roles, IEnumerable<string> permissions)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId.ToString()),
            new(ClaimTypes.Name, userName),
            new("scope_type", "tenant"),
            new("tenant_id", tenantId.ToString()),
        };
        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));
        claims.AddRange(permissions.Select(p => new Claim("permission", p)));
        return Write(claims);
    }

    public string IssuePlatformToken(Guid operatorId, string userName, IEnumerable<string> permissions)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, operatorId.ToString()),
            new(ClaimTypes.Name, userName),
            new("scope_type", "platform"),
        };
        claims.AddRange(permissions.Select(p => new Claim("permission", p)));
        return Write(claims);
    }

    private string Write(IEnumerable<Claim> claims) =>
        new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
            issuer: _issuer,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(_accessTokenMinutes),
            signingCredentials: _credentials));
}
