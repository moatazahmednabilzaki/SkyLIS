using SkyLIS.Domain.Common;

namespace SkyLIS.Domain.Platform;

/// <summary>
/// Platform-owned aggregate (P01.3 / §8): a subscription plan with its entitlements.
/// Quotas are enforced at the point of consumption: user creation (MaxUsers), branch
/// opening (MaxBranches). The monthly report quota is metered (FR-SYS-011) and surfaced
/// on the usage explorer — finalized reports are never blocked (patient care first);
/// overage is a billing conversation, not a hard stop.
/// </summary>
public sealed class Plan : AggregateRoot
{
    public string Code { get; private set; } = null!;
    public string Name { get; private set; } = null!;
    public decimal MonthlyPrice { get; private set; }
    public string Currency { get; private set; } = null!;
    public int MaxUsers { get; private set; }
    public int MaxBranches { get; private set; }
    public int MonthlyReportQuota { get; private set; }
    public bool IsActive { get; private set; }

    private Plan() { } // EF

    public static Plan Create(
        Guid id, string code, string name, decimal monthlyPrice, string currency,
        int maxUsers, int maxBranches, int monthlyReportQuota)
    {
        if (string.IsNullOrWhiteSpace(code)) throw new DomainException("Plan code is required.");
        if (string.IsNullOrWhiteSpace(name)) throw new DomainException("Plan name is required.");
        if (monthlyPrice < 0) throw new DomainException("Plan price cannot be negative.");
        if (string.IsNullOrWhiteSpace(currency) || currency.Trim().Length != 3)
            throw new DomainException("Currency must be an ISO 4217 code.");
        if (maxUsers < 1 || maxBranches < 1 || monthlyReportQuota < 1)
            throw new DomainException("Plan entitlements must be at least 1.");

        return new Plan
        {
            Id = id,
            Code = code.Trim().ToUpperInvariant(),
            Name = name.Trim(),
            MonthlyPrice = decimal.Round(monthlyPrice, 2),
            Currency = currency.Trim().ToUpperInvariant(),
            MaxUsers = maxUsers,
            MaxBranches = maxBranches,
            MonthlyReportQuota = monthlyReportQuota,
            IsActive = true,
        };
    }

    public void Update(string name, decimal monthlyPrice, int maxUsers, int maxBranches, int monthlyReportQuota)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new DomainException("Plan name is required.");
        if (monthlyPrice < 0) throw new DomainException("Plan price cannot be negative.");
        if (maxUsers < 1 || maxBranches < 1 || monthlyReportQuota < 1)
            throw new DomainException("Plan entitlements must be at least 1.");
        Name = name.Trim();
        MonthlyPrice = decimal.Round(monthlyPrice, 2);
        MaxUsers = maxUsers;
        MaxBranches = maxBranches;
        MonthlyReportQuota = monthlyReportQuota;
    }

    public void Retire() => IsActive = false;
}
