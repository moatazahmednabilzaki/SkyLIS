using FluentValidation;
using MediatR;
using SkyLIS.Application.Common;
using SkyLIS.Domain.Common;

namespace SkyLIS.Application.Users;

/// <summary>
/// §4.3: a signed-in user changes their own password. The current password is re-verified
/// (the closest thing to re-authentication before the OIDC authority ships).
/// </summary>
public sealed record ChangePasswordCommand(string CurrentPassword, string NewPassword) : ICommand<Unit>;

internal sealed class ChangePasswordValidator : AbstractValidator<ChangePasswordCommand>
{
    public ChangePasswordValidator()
    {
        RuleFor(x => x.CurrentPassword).NotEmpty();
        RuleFor(x => x.NewPassword).NotEmpty().MinimumLength(12)
            .WithMessage("Password policy: at least 12 characters (§4.3).");
    }
}

internal sealed class ChangePasswordHandler : IRequestHandler<ChangePasswordCommand, Unit>
{
    private readonly IUserRepository _users;
    private readonly IPasswordHasher _hasher;
    private readonly ICurrentUser _currentUser;

    public ChangePasswordHandler(IUserRepository users, IPasswordHasher hasher, ICurrentUser currentUser)
    {
        _users = users;
        _hasher = hasher;
        _currentUser = currentUser;
    }

    public async Task<Unit> Handle(ChangePasswordCommand request, CancellationToken ct)
    {
        var user = await _users.GetAsync(_currentUser.UserId ?? Guid.Empty, ct)
            ?? throw new ForbiddenAccessException("Password change requires a real user account (not a dev token).");
        if (!_hasher.Verify(request.CurrentPassword, user.PasswordHash))
            throw new ForbiddenAccessException("The current password is incorrect.");
        user.SetPasswordHash(_hasher.Hash(request.NewPassword));
        return Unit.Value;
    }
}

/// <summary>P02.1: admin resets a user's password (support flow; audited).</summary>
public sealed record ResetPasswordCommand(Guid UserId, string NewPassword) : ICommand<Unit>, IRequirePermission
{
    public string Permission => "users.user.create";
}

internal sealed class ResetPasswordValidator : AbstractValidator<ResetPasswordCommand>
{
    public ResetPasswordValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.NewPassword).NotEmpty().MinimumLength(12)
            .WithMessage("Password policy: at least 12 characters (§4.3).");
    }
}

internal sealed class ResetPasswordHandler : IRequestHandler<ResetPasswordCommand, Unit>
{
    private readonly IUserRepository _users;
    private readonly IPasswordHasher _hasher;

    public ResetPasswordHandler(IUserRepository users, IPasswordHasher hasher)
    {
        _users = users;
        _hasher = hasher;
    }

    public async Task<Unit> Handle(ResetPasswordCommand request, CancellationToken ct)
    {
        var user = await _users.GetAsync(request.UserId, ct)
            ?? throw new NotFoundException("User", request.UserId);
        user.SetPasswordHash(_hasher.Hash(request.NewPassword));
        return Unit.Value;
    }
}

/// <summary>P02.1: lock / unlock / deactivate a user. Admins cannot act on themselves.</summary>
public sealed record SetUserStatusCommand(Guid UserId, string Action) : ICommand<Unit>, IRequirePermission
{
    public string Permission => "users.user.create";
}

internal sealed class SetUserStatusValidator : AbstractValidator<SetUserStatusCommand>
{
    private static readonly string[] Actions = ["lock", "unlock", "deactivate"];

    public SetUserStatusValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.Action).NotEmpty()
            .Must(a => Actions.Contains(a.ToLowerInvariant()))
            .WithMessage("Action must be one of: lock, unlock, deactivate.");
    }
}

internal sealed class SetUserStatusHandler : IRequestHandler<SetUserStatusCommand, Unit>
{
    private readonly IUserRepository _users;
    private readonly ICurrentUser _currentUser;

    public SetUserStatusHandler(IUserRepository users, ICurrentUser currentUser)
    {
        _users = users;
        _currentUser = currentUser;
    }

    public async Task<Unit> Handle(SetUserStatusCommand request, CancellationToken ct)
    {
        if (request.UserId == _currentUser.UserId)
            throw new DomainException("You cannot lock or deactivate your own account.");
        var user = await _users.GetAsync(request.UserId, ct)
            ?? throw new NotFoundException("User", request.UserId);

        switch (request.Action.ToLowerInvariant())
        {
            case "lock": user.Lock(); break;
            case "unlock": user.Unlock(); break;
            case "deactivate": user.Deactivate(); break;
        }
        return Unit.Value;
    }
}
