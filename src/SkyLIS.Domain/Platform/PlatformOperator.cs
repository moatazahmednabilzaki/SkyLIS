using SkyLIS.Domain.Common;

namespace SkyLIS.Domain.Platform;

public enum OperatorStatus { Active = 0, Locked = 1, Deactivated = 2 }

/// <summary>
/// The permission bundle every platform operator carries (Admin Portal). Single source of
/// truth — token issuance (production login AND the dev token endpoint) reads this list.
/// </summary>
public static class PlatformPermissionCatalog
{
    public static readonly IReadOnlyList<string> All =
    [
        "platform.tenant.provision", "platform.tenant.read", "platform.tenant.manage",
        "platform.outbox.read",
        "platform.masterdata.read", "platform.masterdata.manage",
    ];
}

/// <summary>
/// Platform-owned aggregate: an Admin Portal operator account (M01). Replaces the
/// Development-only token issuance in production: operators sign in with real
/// credentials and carry the platform permission bundle. Same §4.3 brute-force
/// lockout mechanics as tenant users.
/// </summary>
public sealed class PlatformOperator : AggregateRoot
{
    public const int MaxFailedLogins = 5;

    public string UserName { get; private set; } = null!;
    public string FullName { get; private set; } = null!;
    public string PasswordHash { get; private set; } = null!;
    public OperatorStatus Status { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset? LastLoginAtUtc { get; private set; }
    public int FailedLoginCount { get; private set; }

    private PlatformOperator() { } // EF

    public static PlatformOperator Create(
        Guid id, string userName, string fullName, string passwordHash, DateTimeOffset nowUtc)
    {
        if (string.IsNullOrWhiteSpace(userName) || userName.Trim().Length < 3)
            throw new DomainException("Operator user name of at least 3 characters is required.");
        if (string.IsNullOrWhiteSpace(fullName)) throw new DomainException("Full name is required.");
        if (string.IsNullOrWhiteSpace(passwordHash)) throw new DomainException("Password hash is required.");

        return new PlatformOperator
        {
            Id = id,
            UserName = userName.Trim().ToLowerInvariant(),
            FullName = fullName.Trim(),
            PasswordHash = passwordHash,
            Status = OperatorStatus.Active,
            CreatedAtUtc = nowUtc,
        };
    }

    public void RecordLogin(DateTimeOffset nowUtc)
    {
        if (Status != OperatorStatus.Active)
            throw new DomainException($"Operator {UserName} is {Status} and cannot sign in.");
        LastLoginAtUtc = nowUtc;
        FailedLoginCount = 0;
    }

    public void RecordFailedLogin()
    {
        if (Status != OperatorStatus.Active) return;
        FailedLoginCount++;
        if (FailedLoginCount >= MaxFailedLogins)
            Status = OperatorStatus.Locked;
    }

    public void Unlock()
    {
        if (Status != OperatorStatus.Locked)
            throw new InvalidStateTransitionException(nameof(PlatformOperator), Status.ToString(), OperatorStatus.Active.ToString());
        Status = OperatorStatus.Active;
        FailedLoginCount = 0;
    }

    public void SetPasswordHash(string newPasswordHash)
    {
        if (string.IsNullOrWhiteSpace(newPasswordHash)) throw new DomainException("Password hash is required.");
        if (Status == OperatorStatus.Deactivated)
            throw new DomainException($"Operator {UserName} is deactivated.");
        PasswordHash = newPasswordHash;
    }

    public void Deactivate() => Status = OperatorStatus.Deactivated;
}
