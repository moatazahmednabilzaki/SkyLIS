using FluentValidation;
using MediatR;
using SkyLIS.Application.Common;

namespace SkyLIS.Application.Users;

public sealed record MfaEnrollmentDto(string Secret, string OtpAuthUri);

/// <summary>
/// §4.3 MFA self-service. Enrollment is a two-step proof: the secret is issued, then MFA
/// only ENFORCES after the user confirms with a first valid code — a typo'd QR scan can
/// never lock someone out of their account.
/// </summary>
public sealed record EnrollMfaCommand : ICommand<MfaEnrollmentDto>;

internal sealed class EnrollMfaHandler : IRequestHandler<EnrollMfaCommand, MfaEnrollmentDto>
{
    private readonly IUserRepository _users;
    private readonly ITotpService _totp;
    private readonly ICurrentUser _currentUser;

    public EnrollMfaHandler(IUserRepository users, ITotpService totp, ICurrentUser currentUser)
    {
        _users = users;
        _totp = totp;
        _currentUser = currentUser;
    }

    public async Task<MfaEnrollmentDto> Handle(EnrollMfaCommand request, CancellationToken ct)
    {
        var user = await _users.GetAsync(_currentUser.UserId ?? Guid.Empty, ct)
            ?? throw new ForbiddenAccessException("No signed-in user.");
        var secret = _totp.GenerateSecret();
        user.StartMfaEnrollment(secret);
        return new MfaEnrollmentDto(secret, _totp.BuildOtpAuthUri(secret, user.UserName, "Sky LIS"));
    }
}

public sealed record ConfirmMfaCommand(string Code) : ICommand<Unit>;

internal sealed class ConfirmMfaValidator : AbstractValidator<ConfirmMfaCommand>
{
    public ConfirmMfaValidator()
    {
        RuleFor(x => x.Code).NotEmpty().Length(6).Matches("^[0-9]+$");
    }
}

internal sealed class ConfirmMfaHandler : IRequestHandler<ConfirmMfaCommand, Unit>
{
    private readonly IUserRepository _users;
    private readonly ITotpService _totp;
    private readonly ICurrentUser _currentUser;

    public ConfirmMfaHandler(IUserRepository users, ITotpService totp, ICurrentUser currentUser)
    {
        _users = users;
        _totp = totp;
        _currentUser = currentUser;
    }

    public async Task<Unit> Handle(ConfirmMfaCommand request, CancellationToken ct)
    {
        var user = await _users.GetAsync(_currentUser.UserId ?? Guid.Empty, ct)
            ?? throw new ForbiddenAccessException("No signed-in user.");
        if (user.PendingMfaSecret is null)
            throw new Domain.Common.DomainException("MFA enrollment has not been started.");
        if (!_totp.Verify(user.PendingMfaSecret, request.Code))
            throw new ForbiddenAccessException("The code does not match — check the authenticator and try again.");
        user.ConfirmMfa();
        return Unit.Value;
    }
}

/// <summary>Disabling MFA requires re-proving the password (an unlocked screen is not enough).</summary>
public sealed record DisableMfaCommand(string Password) : ICommand<Unit>;

internal sealed class DisableMfaValidator : AbstractValidator<DisableMfaCommand>
{
    public DisableMfaValidator()
    {
        RuleFor(x => x.Password).NotEmpty();
    }
}

internal sealed class DisableMfaHandler : IRequestHandler<DisableMfaCommand, Unit>
{
    private readonly IUserRepository _users;
    private readonly IPasswordHasher _hasher;
    private readonly ICurrentUser _currentUser;

    public DisableMfaHandler(IUserRepository users, IPasswordHasher hasher, ICurrentUser currentUser)
    {
        _users = users;
        _hasher = hasher;
        _currentUser = currentUser;
    }

    public async Task<Unit> Handle(DisableMfaCommand request, CancellationToken ct)
    {
        var user = await _users.GetAsync(_currentUser.UserId ?? Guid.Empty, ct)
            ?? throw new ForbiddenAccessException("No signed-in user.");
        if (!_hasher.Verify(request.Password, user.PasswordHash))
            throw new ForbiddenAccessException("Invalid credentials.");
        user.DisableMfa();
        return Unit.Value;
    }
}
