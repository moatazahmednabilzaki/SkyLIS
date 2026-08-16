using SkyLIS.Domain.Common;

namespace SkyLIS.Domain.Users;

public enum UserStatus { Active = 0, Locked = 1, Deactivated = 2 }

/// <summary>
/// Tenant-owned aggregate: one staff identity (M02). Passwords enter the domain only as
/// hashes (hashing is an Application/Infrastructure concern). Each staff member uses a
/// personal login — the audit trail and SoD depend on it (§4.1).
/// </summary>
public sealed class User : AggregateRoot, ITenantOwned
{
    private readonly List<string> _roles = [];

    public Guid TenantId { get; private set; }
    public string UserName { get; private set; } = null!;
    public string FullName { get; private set; } = null!;
    public string PasswordHash { get; private set; } = null!;
    public UserStatus Status { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset? LastLoginAtUtc { get; private set; }

    public IReadOnlyCollection<string> Roles => _roles.AsReadOnly();

    private User() { } // EF

    public static User Create(
        Guid id, Guid tenantId, string userName, string fullName, string passwordHash,
        IReadOnlyCollection<string> roles, DateTimeOffset nowUtc)
    {
        if (tenantId == Guid.Empty) throw new DomainException("Tenant id is required.");
        if (string.IsNullOrWhiteSpace(userName) || userName.Trim().Length < 3)
            throw new DomainException("User name of at least 3 characters is required.");
        if (string.IsNullOrWhiteSpace(fullName)) throw new DomainException("Full name is required.");
        if (string.IsNullOrWhiteSpace(passwordHash)) throw new DomainException("Password hash is required.");
        if (roles.Count == 0) throw new DomainException("A user requires at least one role.");
        var unknown = roles.Where(r => !RoleCatalog.Exists(r)).ToList();
        if (unknown.Count > 0)
            throw new DomainException($"Unknown role(s): {string.Join(", ", unknown)}. System roles are immutable (§4.1).");

        var user = new User
        {
            Id = id,
            TenantId = tenantId,
            UserName = userName.Trim().ToLowerInvariant(),
            FullName = fullName.Trim(),
            PasswordHash = passwordHash,
            Status = UserStatus.Active,
            CreatedAtUtc = nowUtc,
        };
        user._roles.AddRange(roles.Distinct());
        user.Raise(new UserCreated(id, tenantId, user.UserName));
        return user;
    }

    /// <summary>All permission codes granted through this user's roles.</summary>
    public IReadOnlyCollection<string> Permissions() =>
        _roles.SelectMany(RoleCatalog.PermissionsOf).Distinct().ToList();

    public void RecordLogin(DateTimeOffset nowUtc)
    {
        if (Status != UserStatus.Active)
            throw new DomainException($"User {UserName} is {Status} and cannot sign in.");
        LastLoginAtUtc = nowUtc;
    }

    /// <summary>Password change/reset — only ever receives a HASH (§4.3).</summary>
    public void SetPasswordHash(string newPasswordHash)
    {
        if (string.IsNullOrWhiteSpace(newPasswordHash)) throw new DomainException("Password hash is required.");
        if (Status == UserStatus.Deactivated)
            throw new DomainException($"User {UserName} is deactivated; reactivate before resetting the password.");
        PasswordHash = newPasswordHash;
    }

    public void Lock() => Status = Status == UserStatus.Deactivated
        ? throw new InvalidStateTransitionException(nameof(User), Status.ToString(), UserStatus.Locked.ToString())
        : UserStatus.Locked;

    public void Unlock()
    {
        if (Status != UserStatus.Locked)
            throw new InvalidStateTransitionException(nameof(User), Status.ToString(), UserStatus.Active.ToString());
        Status = UserStatus.Active;
    }

    public void Deactivate() => Status = UserStatus.Deactivated;
}

public sealed record UserCreated(Guid UserId, Guid TenantId, string UserName) : DomainEvent, ITenantEvent;
