using FluentAssertions;
using SkyLIS.Domain.Common;
using SkyLIS.Domain.Platform;
using SkyLIS.Domain.Users;
using Xunit;

namespace SkyLIS.Domain.Tests;

public class UserLockoutTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 17, 9, 0, 0, TimeSpan.Zero);

    private static User NewUser() => User.Create(
        Guid.NewGuid(), Guid.NewGuid(), "sara.hassan", "Dr. Sara Hassan", "hash",
        [RoleCatalog.TenantAdmin], Now);

    [Fact]
    public void Fifth_consecutive_failure_locks_the_account()
    {
        var user = NewUser();
        for (var i = 0; i < User.MaxFailedLogins - 1; i++)
            user.RecordFailedLogin();
        user.Status.Should().Be(UserStatus.Active, "four failures stay below the threshold");

        user.RecordFailedLogin();
        user.Status.Should().Be(UserStatus.Locked);
    }

    [Fact]
    public void Successful_login_resets_the_failure_counter()
    {
        var user = NewUser();
        user.RecordFailedLogin();
        user.RecordFailedLogin();
        user.RecordLogin(Now);
        user.FailedLoginCount.Should().Be(0);
    }

    [Fact]
    public void Unlock_resets_the_failure_counter()
    {
        var user = NewUser();
        for (var i = 0; i < User.MaxFailedLogins; i++)
            user.RecordFailedLogin();
        user.Unlock();
        user.Status.Should().Be(UserStatus.Active);
        user.FailedLoginCount.Should().Be(0);
    }

    [Fact]
    public void Locked_user_cannot_record_a_login()
    {
        var user = NewUser();
        user.Lock();
        var act = () => user.RecordLogin(Now);
        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Mfa_enforces_only_after_the_first_valid_code_confirms_enrollment()
    {
        var user = NewUser();
        user.StartMfaEnrollment("JBSWY3DPEHPK3PXP");
        user.MfaEnabled.Should().BeFalse("a typo'd QR scan must never lock the user out");

        user.ConfirmMfa();
        user.MfaEnabled.Should().BeTrue();

        user.DisableMfa();
        user.MfaEnabled.Should().BeFalse();
        user.MfaSecret.Should().BeNull();
    }

    [Fact]
    public void Confirming_mfa_without_enrollment_is_rejected()
    {
        var user = NewUser();
        var act = () => user.ConfirmMfa();
        act.Should().Throw<DomainException>().WithMessage("*enrollment*");
    }

    [Fact]
    public void Reenrollment_keeps_the_active_factor_until_a_new_code_confirms()
    {
        var user = NewUser();
        user.StartMfaEnrollment("FIRSTSECRET2345A");
        user.ConfirmMfa();

        // Re-enrolling must NOT disable the currently-active factor — a session-only
        // attacker cannot strip MFA by calling enroll (Finding 2).
        user.StartMfaEnrollment("SECONDSECRET345A");
        user.MfaEnabled.Should().BeTrue("the active factor keeps enforcing during re-enrollment");
        user.MfaSecret.Should().Be("FIRSTSECRET2345A", "the old secret still validates logins until confirmation");
        user.PendingMfaSecret.Should().Be("SECONDSECRET345A");

        user.ConfirmMfa();
        user.MfaSecret.Should().Be("SECONDSECRET345A", "confirmation promotes the pending secret");
        user.PendingMfaSecret.Should().BeNull();
    }
}

public class PlatformOperatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 17, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_normalizes_the_user_name()
    {
        var op = PlatformOperator.Create(Guid.NewGuid(), " Platform.Admin ", "Platform Admin", "hash", Now);
        op.UserName.Should().Be("platform.admin");
        op.Status.Should().Be(OperatorStatus.Active);
    }

    [Fact]
    public void Lockout_and_unlock_mirror_the_tenant_user_mechanics()
    {
        var op = PlatformOperator.Create(Guid.NewGuid(), "ops", "Ops", "hash", Now);
        for (var i = 0; i < PlatformOperator.MaxFailedLogins; i++)
            op.RecordFailedLogin();
        op.Status.Should().Be(OperatorStatus.Locked);

        op.Unlock();
        op.Status.Should().Be(OperatorStatus.Active);
        op.FailedLoginCount.Should().Be(0);
    }

    [Fact]
    public void Deactivated_operator_cannot_change_password()
    {
        var op = PlatformOperator.Create(Guid.NewGuid(), "ops", "Ops", "hash", Now);
        op.Deactivate();
        var act = () => op.SetPasswordHash("new-hash");
        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void The_platform_permission_catalog_covers_the_admin_portal_surface()
    {
        PlatformPermissionCatalog.All.Should().Contain(
            ["platform.tenant.provision", "platform.tenant.read", "platform.tenant.manage",
             "platform.outbox.read", "platform.masterdata.read", "platform.masterdata.manage"]);
    }
}
