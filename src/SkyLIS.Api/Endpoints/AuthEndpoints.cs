using MediatR;
using SkyLIS.Api.Infrastructure;
using SkyLIS.Application.Platform;
using SkyLIS.Application.Users;
using SkyLIS.Infrastructure.Tenancy;

namespace SkyLIS.Api.Endpoints;

public static class AuthEndpoints
{
    /// <summary>
    /// Login carries the tenant id explicitly; production resolves it from the verified
    /// subdomain at the gateway (§2.4). Here the tenant id acts as a realm for CREDENTIAL
    /// VERIFICATION only — authorization never trusts it beyond the password check, and
    /// the issued JWT carries the tenant proven by the user record.
    /// </summary>
    public sealed record LoginRequest(Guid TenantId, string UserName, string Password, string? MfaCode);
    public sealed record PlatformLoginRequest(string UserName, string Password);
    public sealed record RefreshRequest(string RefreshToken);

    public static RouteGroupBuilder MapAuthEndpoints(this RouteGroupBuilder group, IConfiguration configuration)
    {
        var auth = group.MapGroup("/auth").AllowAnonymous()
            .RequireRateLimiting("auth").WithTags("Authentication");

        auth.MapPost("/login", async (
            ISender sender, TokenService tokens, TenantContext tenantContext, LoginRequest request, CancellationToken ct) =>
        {
            tenantContext.Set(request.TenantId); // login realm; see remark above
            var result = await sender.Send(
                new LoginCommand(request.UserName, request.Password, request.MfaCode), ct);
            if (result.MfaRequired)
                return Results.Ok(new { mfaRequired = true });

            var user = result.User!;
            return Results.Ok(new
            {
                mfaRequired = false,
                token = tokens.IssueTenantToken(
                    user.UserId, user.UserName, user.TenantId, user.Roles, user.Permissions),
                refreshToken = user.RefreshToken,
                expiresInSeconds = tokens.AccessTokenSeconds,
                user.UserId,
                user.UserName,
                user.FullName,
                user.Roles,
                user.Permissions,
            });
        });

        // Production Admin Portal sign-in (M01) — replaces the Development-only dev token.
        auth.MapPost("/platform-login", async (
            ISender sender, TokenService tokens, PlatformLoginRequest request, CancellationToken ct) =>
        {
            var @operator = await sender.Send(new PlatformLoginCommand(request.UserName, request.Password), ct);
            return Results.Ok(new
            {
                token = tokens.IssuePlatformToken(@operator.OperatorId, @operator.UserName, @operator.Permissions),
                refreshToken = @operator.RefreshToken,
                expiresInSeconds = tokens.AccessTokenSeconds,
                @operator.OperatorId,
                @operator.UserName,
                @operator.FullName,
                @operator.Permissions,
            });
        });

        // Rotating refresh: the principal is RE-LOADED so role/lockout/suspension changes
        // take effect within one access-token lifetime.
        auth.MapPost("/refresh", async (
            ISender sender, TokenService tokens, RefreshRequest request, CancellationToken ct) =>
        {
            var session = await sender.Send(new RefreshSessionCommand(request.RefreshToken), ct);
            var token = session.IsPlatform
                ? tokens.IssuePlatformToken(session.PrincipalId, session.UserName, session.Permissions)
                : tokens.IssueTenantToken(
                    session.PrincipalId, session.UserName, session.TenantId!.Value,
                    session.Roles, session.Permissions);
            return Results.Ok(new
            {
                token,
                refreshToken = session.NewRefreshToken,
                expiresInSeconds = tokens.AccessTokenSeconds,
                session.UserName,
                session.FullName,
                session.Roles,
                session.Permissions,
            });
        });

        auth.MapPost("/logout", async (ISender sender, RefreshRequest request, CancellationToken ct) =>
        {
            await sender.Send(new LogoutCommand(request.RefreshToken), ct);
            return Results.NoContent();
        });

        return group;
    }
}
