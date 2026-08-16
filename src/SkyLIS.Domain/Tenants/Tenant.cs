using SkyLIS.Domain.Common;

namespace SkyLIS.Domain.Tenants;

/// <summary>
/// Platform-owned aggregate (NOT tenant-owned): one customer organization.
/// Root of the tenant lifecycle state machine: Trial → Active → PastDue → Suspended → Offboarded.
/// Concurrency: optimistic (xmin). Authorization: platform.* permissions only (Admin Portal).
/// </summary>
public sealed class Tenant : AggregateRoot
{
    private static readonly HashSet<(TenantStatus From, TenantStatus To)> AllowedTransitions =
    [
        (TenantStatus.Trial, TenantStatus.Active),
        (TenantStatus.Trial, TenantStatus.Offboarded),
        (TenantStatus.Active, TenantStatus.PastDue),
        (TenantStatus.Active, TenantStatus.Suspended),
        (TenantStatus.PastDue, TenantStatus.Active),
        (TenantStatus.PastDue, TenantStatus.Suspended),
        (TenantStatus.Suspended, TenantStatus.Active),
        (TenantStatus.Suspended, TenantStatus.Offboarded),
    ];

    public string LegalName { get; private set; } = null!;
    public string Subdomain { get; private set; } = null!;
    public string CountryCode { get; private set; } = null!;
    public string PlanCode { get; private set; } = null!;
    public IsolationTier IsolationTier { get; private set; }
    public TenantStatus Status { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public string? SuspensionReason { get; private set; }

    private Tenant() { } // EF

    public static Tenant Provision(
        Guid id, string legalName, string subdomain, string countryCode,
        string planCode, IsolationTier isolationTier,
        string adminUserName, string adminFullName, string adminPasswordHash,
        DateTimeOffset nowUtc)
    {
        if (string.IsNullOrWhiteSpace(legalName)) throw new DomainException("Tenant legal name is required.");
        if (string.IsNullOrWhiteSpace(subdomain) || subdomain.Length < 3 || !subdomain.All(c => char.IsLetterOrDigit(c) || c == '-'))
            throw new DomainException("Subdomain must be at least 3 characters: letters, digits, or hyphens.");
        if (string.IsNullOrWhiteSpace(countryCode) || countryCode.Length != 2)
            throw new DomainException("Country code must be an ISO 3166-1 alpha-2 code.");
        if (string.IsNullOrWhiteSpace(planCode)) throw new DomainException("Plan code is required.");
        if (string.IsNullOrWhiteSpace(adminUserName) || string.IsNullOrWhiteSpace(adminPasswordHash))
            throw new DomainException("The initial Tenant Admin account (P01.2 step 4) is required.");

        var tenant = new Tenant
        {
            Id = id,
            LegalName = legalName.Trim(),
            Subdomain = subdomain.ToLowerInvariant(),
            CountryCode = countryCode.ToUpperInvariant(),
            PlanCode = planCode,
            IsolationTier = isolationTier,
            Status = TenantStatus.Trial,
            CreatedAtUtc = nowUtc,
        };
        tenant.Raise(new TenantProvisioned(
            id, tenant.Subdomain, tenant.CountryCode, planCode,
            adminUserName.Trim().ToLowerInvariant(), adminFullName.Trim(), adminPasswordHash));
        return tenant;
    }

    public void Activate()
    {
        var from = Status;
        Transition(TenantStatus.Active);
        SuspensionReason = null;
        Raise(from == TenantStatus.Suspended
            ? new TenantResumed(Id)
            : new TenantActivated(Id));
    }

    public void MarkPastDue()
    {
        Transition(TenantStatus.PastDue);
        Raise(new TenantMarkedPastDue(Id));
    }

    public void Suspend(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new DomainException("A suspension reason is mandatory.");
        Transition(TenantStatus.Suspended);
        SuspensionReason = reason.Trim();
        Raise(new TenantSuspended(Id, SuspensionReason));
    }

    public void Offboard()
    {
        Transition(TenantStatus.Offboarded);
        Raise(new TenantOffboarded(Id));
    }

    /// <summary>P01.3: move the tenant to another plan (entitlements apply immediately).</summary>
    public void ChangePlan(string planCode)
    {
        if (string.IsNullOrWhiteSpace(planCode)) throw new DomainException("Plan code is required.");
        if (Status == TenantStatus.Offboarded)
            throw new InvalidStateTransitionException(nameof(Tenant), Status.ToString(), "plan change");
        PlanCode = planCode.Trim().ToUpperInvariant();
    }

    private void Transition(TenantStatus to)
    {
        if (!AllowedTransitions.Contains((Status, to)))
            throw new InvalidStateTransitionException(nameof(Tenant), Status.ToString(), to.ToString());
        Status = to;
    }
}
