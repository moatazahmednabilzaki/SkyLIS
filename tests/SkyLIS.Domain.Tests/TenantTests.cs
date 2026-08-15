using FluentAssertions;
using SkyLIS.Domain.Common;
using SkyLIS.Domain.Tenants;
using Xunit;

namespace SkyLIS.Domain.Tests;

public class TenantTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 15, 9, 0, 0, TimeSpan.Zero);

    private static Tenant NewTenant() =>
        Tenant.Provision(Guid.NewGuid(), "Cairo Care Laboratories", "cairocare", "EG", "PROFESSIONAL",
            IsolationTier.SharedRls, "admin", "Cairo Admin", "HASHED-PW", Now);

    [Fact]
    public void Provision_starts_in_trial_and_raises_event()
    {
        var tenant = NewTenant();
        tenant.Status.Should().Be(TenantStatus.Trial);
        tenant.Subdomain.Should().Be("cairocare");
        tenant.CountryCode.Should().Be("EG");
        tenant.DomainEvents.OfType<TenantProvisioned>().Should().ContainSingle();
    }

    [Theory]
    [InlineData("")]
    [InlineData("ab")]
    [InlineData("bad subdomain!")]
    public void Provision_rejects_invalid_subdomains(string subdomain)
    {
        var act = () => Tenant.Provision(Guid.NewGuid(), "X Lab", subdomain, "EG", "LITE",
            IsolationTier.SharedRls, "admin", "Admin", "HASH", Now);
        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Lifecycle_walks_trial_active_pastdue_suspended_active()
    {
        var tenant = NewTenant();
        tenant.Activate();
        tenant.Status.Should().Be(TenantStatus.Active);
        tenant.MarkPastDue();
        tenant.Status.Should().Be(TenantStatus.PastDue);
        tenant.Suspend("non-payment (dunning D7)");
        tenant.Status.Should().Be(TenantStatus.Suspended);
        tenant.SuspensionReason.Should().Be("non-payment (dunning D7)");

        tenant.Activate(); // resume
        tenant.Status.Should().Be(TenantStatus.Active);
        tenant.SuspensionReason.Should().BeNull();
        tenant.DomainEvents.OfType<TenantResumed>().Should().ContainSingle();
    }

    [Fact]
    public void Suspension_requires_a_reason()
    {
        var tenant = NewTenant();
        tenant.Activate();
        var act = () => tenant.Suspend("  ");
        act.Should().Throw<DomainException>().WithMessage("*reason*");
    }

    [Fact]
    public void Illegal_transitions_are_rejected()
    {
        var tenant = NewTenant();
        // Trial → PastDue is not an allowed edge
        var act = tenant.MarkPastDue;
        act.Should().Throw<InvalidStateTransitionException>();
    }

    [Fact]
    public void Offboarded_is_terminal()
    {
        var tenant = NewTenant();
        tenant.Offboard();
        tenant.Status.Should().Be(TenantStatus.Offboarded);
        var act = tenant.Activate;
        act.Should().Throw<InvalidStateTransitionException>();
    }
}
