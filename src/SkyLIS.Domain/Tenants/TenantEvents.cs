using SkyLIS.Domain.Common;

namespace SkyLIS.Domain.Tenants;

public sealed record TenantProvisioned(Guid TenantId, string Subdomain, string CountryCode, string PlanCode) : DomainEvent;
public sealed record TenantActivated(Guid TenantId) : DomainEvent;
public sealed record TenantMarkedPastDue(Guid TenantId) : DomainEvent;
public sealed record TenantSuspended(Guid TenantId, string Reason) : DomainEvent;
public sealed record TenantResumed(Guid TenantId) : DomainEvent;
public sealed record TenantOffboarded(Guid TenantId) : DomainEvent;
