using FluentValidation;
using MediatR;
using SkyLIS.Application.Common;
using SkyLIS.Application.Users;
using SkyLIS.Domain.Platform;

namespace SkyLIS.Application.Platform;

public sealed record AuthenticatedOperatorDto(
    Guid OperatorId, string UserName, string FullName,
    IReadOnlyCollection<string> Permissions, string RefreshToken);

/// <summary>
/// Production Admin Portal sign-in (replaces the Development-only platform dev token).
/// Same posture as tenant login: indistinguishable failures, §4.3 lockout after five
/// consecutive misses, rotating refresh token issued on success.
/// </summary>
public sealed record PlatformLoginCommand(string UserName, string Password) : ICommand<AuthenticatedOperatorDto>;

internal sealed class PlatformLoginValidator : AbstractValidator<PlatformLoginCommand>
{
    public PlatformLoginValidator()
    {
        RuleFor(x => x.UserName).NotEmpty();
        RuleFor(x => x.Password).NotEmpty();
    }
}

internal sealed class PlatformLoginHandler : IRequestHandler<PlatformLoginCommand, AuthenticatedOperatorDto>
{
    private readonly IPlatformOperatorRepository _operators;
    private readonly IPasswordHasher _hasher;
    private readonly IRefreshTokenStore _refreshTokens;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;

    public PlatformLoginHandler(
        IPlatformOperatorRepository operators, IPasswordHasher hasher,
        IRefreshTokenStore refreshTokens, IUnitOfWork unitOfWork, IClock clock)
    {
        _operators = operators;
        _hasher = hasher;
        _refreshTokens = refreshTokens;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<AuthenticatedOperatorDto> Handle(PlatformLoginCommand request, CancellationToken ct)
    {
        var @operator = await _operators.FindByUserNameAsync(request.UserName.Trim().ToLowerInvariant(), ct);
        if (@operator is null || @operator.Status != OperatorStatus.Active)
            throw new ForbiddenAccessException("Invalid credentials.");
        if (!_hasher.Verify(request.Password, @operator.PasswordHash))
        {
            @operator.RecordFailedLogin();
            await _unitOfWork.SaveChangesAsync(ct);
            throw new ForbiddenAccessException("Invalid credentials.");
        }

        @operator.RecordLogin(_clock.UtcNow);
        var refreshToken = await _refreshTokens.IssueAsync(
            @operator.Id, IRefreshTokenStore.PlatformOperator, null, ct);
        return new AuthenticatedOperatorDto(
            @operator.Id, @operator.UserName, @operator.FullName,
            PlatformPermissionCatalog.All, refreshToken);
    }
}
