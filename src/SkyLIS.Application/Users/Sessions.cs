using FluentValidation;
using MediatR;
using SkyLIS.Application.Common;

namespace SkyLIS.Application.Users;

public sealed record RefreshedSessionDto(
    bool IsPlatform, Guid PrincipalId, Guid? TenantId, string UserName, string FullName,
    IReadOnlyCollection<string> Roles, IReadOnlyCollection<string> Permissions,
    string NewRefreshToken);

/// <summary>
/// Production session renewal: validates the opaque refresh token, RE-LOADS the principal
/// (so revoked roles, locked accounts, and suspended tenants take effect at most one
/// access-token lifetime after the change), rotates the refresh token, and returns fresh
/// claims. Anonymous by design — possession of a valid refresh token IS the credential.
/// </summary>
public sealed record RefreshSessionCommand(string RefreshToken) : ICommand<RefreshedSessionDto>;

internal sealed class RefreshSessionValidator : AbstractValidator<RefreshSessionCommand>
{
    public RefreshSessionValidator()
    {
        RuleFor(x => x.RefreshToken).NotEmpty();
    }
}

internal sealed class RefreshSessionHandler : IRequestHandler<RefreshSessionCommand, RefreshedSessionDto>
{
    private readonly IRefreshTokenStore _refreshTokens;
    private readonly IUserRepository _users;
    private readonly IPlatformOperatorRepository _operators;
    private readonly ITenantRepository _tenants;
    private readonly ITenantRealm _realm;

    public RefreshSessionHandler(
        IRefreshTokenStore refreshTokens, IUserRepository users, IPlatformOperatorRepository operators,
        ITenantRepository tenants, ITenantRealm realm)
    {
        _refreshTokens = refreshTokens;
        _users = users;
        _operators = operators;
        _tenants = tenants;
        _realm = realm;
    }

    public async Task<RefreshedSessionDto> Handle(RefreshSessionCommand request, CancellationToken ct)
    {
        var info = await _refreshTokens.ValidateAsync(request.RefreshToken, ct)
            ?? throw new ForbiddenAccessException("The session has expired. Sign in again.");

        if (info.PrincipalType == IRefreshTokenStore.PlatformOperator)
        {
            var @operator = await _operators.GetAsync(info.PrincipalId, ct);
            if (@operator is null || @operator.Status != Domain.Platform.OperatorStatus.Active)
                throw new ForbiddenAccessException("The session has expired. Sign in again.");

            var rotated = await _refreshTokens.RotateAsync(
                info.TokenId, info.PrincipalId, info.PrincipalType, null, ct);
            return new RefreshedSessionDto(
                true, @operator.Id, null, @operator.UserName, @operator.FullName,
                ["PlatformOperator"], Domain.Platform.PlatformPermissionCatalog.All, rotated);
        }

        // Tenant principal: the realm comes from the VALIDATED token, never the request.
        _realm.Set(info.TenantId!.Value);
        var user = await _users.GetAsync(info.PrincipalId, ct);
        if (user is null || user.Status != Domain.Users.UserStatus.Active)
            throw new ForbiddenAccessException("The session has expired. Sign in again.");

        var tenant = await _tenants.GetAsync(user.TenantId, ct);
        if (tenant is null
            || tenant.Status is Domain.Tenants.TenantStatus.Suspended or Domain.Tenants.TenantStatus.Offboarded)
        {
            throw new ForbiddenAccessException("The session has expired. Sign in again.");
        }

        var newToken = await _refreshTokens.RotateAsync(
            info.TokenId, info.PrincipalId, info.PrincipalType, info.TenantId, ct);
        return new RefreshedSessionDto(
            false, user.Id, user.TenantId, user.UserName, user.FullName,
            user.Roles, user.Permissions(), newToken);
    }
}

/// <summary>Explicit sign-out: revokes the refresh token (idempotent; unknown tokens are ignored).</summary>
public sealed record LogoutCommand(string RefreshToken) : ICommand<Unit>;

internal sealed class LogoutHandler : IRequestHandler<LogoutCommand, Unit>
{
    private readonly IRefreshTokenStore _refreshTokens;
    public LogoutHandler(IRefreshTokenStore refreshTokens) => _refreshTokens = refreshTokens;

    public async Task<Unit> Handle(LogoutCommand request, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(request.RefreshToken))
            await _refreshTokens.RevokeAsync(request.RefreshToken, ct);
        return Unit.Value;
    }
}
