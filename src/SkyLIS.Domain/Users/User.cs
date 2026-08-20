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
    /// <summary>Consecutive failed sign-ins; the account locks at <see cref="MaxFailedLogins"/> (§4.3).</summary>
    public int FailedLoginCount { get; private set; }

    // TOTP MFA (§4.3, optional per user). MfaSecret is the ACTIVE factor; PendingMfaSecret
    // is a staged enrollment awaiting its first valid code. Re-enrolling while MFA is
    // already active only stages the new secret — the existing factor keeps enforcing
    // until confirmation, so a session-only attacker cannot silently disable MFA.
    public string? MfaSecret { get; private set; }
    public string? PendingMfaSecret { get; private set; }
    public bool MfaEnabled { get; private set; }

    public const int MaxFailedLogins = 5;

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
        FailedLoginCount = 0;
    }

    /// <summary>§4.3 brute-force guard: the fifth consecutive failure locks the account.</summary>
    public void RecordFailedLogin()
    {
        if (Status != UserStatus.Active) return;
        FailedLoginCount++;
        if (FailedLoginCount >= MaxFailedLogins)
            Lock();
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
        FailedLoginCount = 0;
    }

    public void Deactivate() => Status = UserStatus.Deactivated;

    /// <summary>
    /// Stages a TOTP enrollment. It does NOT touch the active factor or the enabled flag:
    /// an already-active MFA keeps enforcing (with the existing secret) until a new code
    /// confirms, so enrolling never weakens the account.
    /// </summary>
    public void StartMfaEnrollment(string secret)
    {
        if (string.IsNullOrWhiteSpace(secret)) throw new DomainException("An MFA secret is required.");
        if (Status == UserStatus.Deactivated)
            throw new DomainException($"User {UserName} is deactivated.");
        PendingMfaSecret = secret;
    }

    /// <summary>Promotes the pending secret after its FIRST valid code proves the authenticator works.</summary>
    public void ConfirmMfa()
    {
        if (PendingMfaSecret is null) throw new DomainException("MFA enrollment has not been started.");
        MfaSecret = PendingMfaSecret;
        PendingMfaSecret = null;
        MfaEnabled = true;
    }

    public void DisableMfa()
    {
        MfaSecret = null;
        PendingMfaSecret = null;
        MfaEnabled = false;
    }
}

public sealed record UserCreated(Guid UserId, Guid TenantId, string UserName) : DomainEvent, ITenantEvent;
