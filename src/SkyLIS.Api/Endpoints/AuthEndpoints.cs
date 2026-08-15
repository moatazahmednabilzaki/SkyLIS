using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using MediatR;
using Microsoft.IdentityModel.Tokens;
using SkyLIS.Application.Users;
using SkyLIS.Infrastructure.Tenancy;

namespace SkyLIS.Api.Endpoints;

public static class AuthEndpoints
{
    /// <summary>
    /// Dev login carries the tenant id explicitly; production resolves it from the
    /// verified subdomain at the gateway (§2.4). Here the tenant id acts as a realm for
    /// CREDENTIAL VERIFICATION only — authorization never trusts it beyond the password
    /// check, and the issued JWT carries the tenant proven by the user record.
    /// </summary>
    public sealed record LoginRequest(Guid TenantId, string UserName, string Password);

    public static RouteGroupBuilder MapAuthEndpoints(this RouteGroupBuilder group, IConfiguration configuration)
    {
        group.MapPost("/auth/login", async (
            ISender sender, TenantContext tenantContext, LoginRequest request, CancellationToken ct) =>
        {
            tenantContext.Set(request.TenantId); // login realm; see remark above
            var user = await sender.Send(new LoginCommand(request.UserName, request.Password), ct);
            return Results.Ok(new
            {
                token = IssueToken(configuration, user),
                user.UserId,
                user.UserName,
                user.FullName,
                user.Roles,
                user.Permissions,
            });
        }).AllowAnonymous().WithTags("Authentication");

        return group;
    }

    private static string IssueToken(IConfiguration configuration, AuthenticatedUserDto user)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.UserId.ToString()),
            new(ClaimTypes.Name, user.UserName),
            new("scope_type", "tenant"),
            new("tenant_id", user.TenantId.ToString()),
        };
        claims.AddRange(user.Roles.Select(r => new Claim(ClaimTypes.Role, r)));
        claims.AddRange(user.Permissions.Select(p => new Claim("permission", p)));

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(configuration["Auth:DevSigningKey"]!));
        var token = new JwtSecurityToken(
            issuer: configuration["Auth:Issuer"] ?? "skylis-dev",
            claims: claims,
            expires: DateTime.UtcNow.AddHours(12),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
