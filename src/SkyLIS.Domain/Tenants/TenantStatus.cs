namespace SkyLIS.Domain.Tenants;

/// <summary>Canonical tenant lifecycle (SRS Rev 2.0, Appendix A).</summary>
public enum TenantStatus
{
    Trial = 0,
    Active = 1,
    PastDue = 2,
    Suspended = 3,
    Offboarded = 4,
}

public enum IsolationTier
{
    SharedRls = 0,
    DedicatedSchema = 1,
    DedicatedDatabase = 2,
}
