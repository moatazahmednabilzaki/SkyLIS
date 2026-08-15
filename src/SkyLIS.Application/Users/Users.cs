using FluentValidation;
using MediatR;
using SkyLIS.Application.Common;
using SkyLIS.Domain.Users;

namespace SkyLIS.Application.Users;

/// <summary>Password hashing port (PBKDF2 in Infrastructure; never plaintext at rest).</summary>
public interface IPasswordHasher
{
    string Hash(string password);
    bool Verify(string password, string hash);
}

public interface IUserRepository
{
    Task<User?> GetAsync(Guid id, CancellationToken ct = default);
    Task<User?> FindByUserNameAsync(string userName, CancellationToken ct = default);
    Task<bool> UserNameExistsAsync(string userName, CancellationToken ct = default);
    void Add(User user);
}

public sealed record UserDto(
    Guid Id, string UserName, string FullName, IReadOnlyCollection<string> Roles,
    string Status, DateTimeOffset? LastLoginAtUtc);

/// <summary>P02.1: create a tenant user with system-role assignments.</summary>
public sealed record CreateUserCommand(
    string UserName, string FullName, string Password, IReadOnlyList<string> Roles)
    : ICommand<Guid>, IRequirePermission
{
    public string Permission => "users.user.create";
}

internal sealed class CreateUserValidator : AbstractValidator<CreateUserCommand>
{
    public CreateUserValidator()
    {
        RuleFor(x => x.UserName).NotEmpty().MinimumLength(3).MaximumLength(60)
            .Matches("^[a-zA-Z0-9._-]+$").WithMessage("User name: letters, digits, dot, dash, underscore.");
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Password).NotEmpty().MinimumLength(12)
            .WithMessage("Password policy: at least 12 characters (§4.3).");
        RuleFor(x => x.Roles).NotEmpty();
        RuleForEach(x => x.Roles).Must(RoleCatalog.Exists)
            .WithMessage((_, role) => $"Unknown role '{role}'. System roles: {string.Join(", ", RoleCatalog.AllRoles)}.");
    }
}

internal sealed class CreateUserHandler : IRequestHandler<CreateUserCommand, Guid>
{
    private readonly IUserRepository _users;
    private readonly IPasswordHasher _hasher;
    private readonly ITenantContext _tenant;
    private readonly IClock _clock;

    public CreateUserHandler(IUserRepository users, IPasswordHasher hasher, ITenantContext tenant, IClock clock)
    {
        _users = users;
        _hasher = hasher;
        _tenant = tenant;
        _clock = clock;
    }

    public async Task<Guid> Handle(CreateUserCommand request, CancellationToken ct)
    {
        var userName = request.UserName.Trim().ToLowerInvariant();
        if (await _users.UserNameExistsAsync(userName, ct))
            throw new ConflictException($"User name '{userName}' already exists in this tenant.");

        var user = User.Create(
            Guid.CreateVersion7(), _tenant.TenantId, userName, request.FullName,
            _hasher.Hash(request.Password), request.Roles.ToList(), _clock.UtcNow);
        _users.Add(user);
        return user.Id;
    }
}

/// <summary>P02.1: users directory.</summary>
public sealed record ListUsersQuery : IQuery<IReadOnlyList<UserDto>>, IRequirePermission
{
    public string Permission => "users.user.read";
}

public interface IUserQueries
{
    Task<IReadOnlyList<UserDto>> ListAsync(CancellationToken ct = default);
}

internal sealed class ListUsersHandler : IRequestHandler<ListUsersQuery, IReadOnlyList<UserDto>>
{
    private readonly IUserQueries _queries;
    public ListUsersHandler(IUserQueries queries) => _queries = queries;
    public Task<IReadOnlyList<UserDto>> Handle(ListUsersQuery request, CancellationToken ct) =>
        _queries.ListAsync(ct);
}

/// <summary>
/// Real credential login (replaces dev tenant tokens). Anonymous by design; the API host
/// issues the JWT from the returned identity. Tenant resolution is explicit in dev
/// (tenant id); subdomain-based resolution arrives with the gateway (§2.4).
/// </summary>
public sealed record LoginCommand(string UserName, string Password) : ICommand<AuthenticatedUserDto>;

public sealed record AuthenticatedUserDto(
    Guid UserId, Guid TenantId, string UserName, string FullName,
    IReadOnlyCollection<string> Roles, IReadOnlyCollection<string> Permissions);

internal sealed class LoginValidator : AbstractValidator<LoginCommand>
{
    public LoginValidator()
    {
        RuleFor(x => x.UserName).NotEmpty();
        RuleFor(x => x.Password).NotEmpty();
    }
}

internal sealed class LoginHandler : IRequestHandler<LoginCommand, AuthenticatedUserDto>
{
    private readonly IUserRepository _users;
    private readonly IPasswordHasher _hasher;
    private readonly IClock _clock;

    public LoginHandler(IUserRepository users, IPasswordHasher hasher, IClock clock)
    {
        _users = users;
        _hasher = hasher;
        _clock = clock;
    }

    public async Task<AuthenticatedUserDto> Handle(LoginCommand request, CancellationToken ct)
    {
        var user = await _users.FindByUserNameAsync(request.UserName.Trim().ToLowerInvariant(), ct);
        // One indistinguishable failure for unknown user / wrong password / inactive account.
        if (user is null
            || !_hasher.Verify(request.Password, user.PasswordHash)
            || user.Status != Domain.Users.UserStatus.Active)
        {
            throw new ForbiddenAccessException("Invalid credentials.");
        }

        user.RecordLogin(_clock.UtcNow);
        return new AuthenticatedUserDto(
            user.Id, user.TenantId, user.UserName, user.FullName, user.Roles, user.Permissions());
    }
}
