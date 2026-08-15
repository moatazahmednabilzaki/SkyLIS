using SkyLIS.Domain.Common;

namespace SkyLIS.Domain.Tenants;

/// <summary>Carries the initial admin's credentials as a HASH (never plaintext) so the
/// outbox consumer can create the first user under the new tenant's own context.</summary>
public sealed record TenantProvisioned(
    Guid TenantId, string Subdomain, string CountryCode, string PlanCode,
    string AdminUserName, string AdminFullName, string AdminPasswordHash) : DomainEvent, ITenantEvent;
public sealed record TenantActivated(Guid TenantId) : DomainEvent;
public sealed record TenantMarkedPastDue(Guid TenantId) : DomainEvent;
public sealed record TenantSuspended(Guid TenantId, string Reason) : DomainEvent;
public sealed record TenantResumed(Guid TenantId) : DomainEvent;
public sealed record TenantOffboarded(Guid TenantId) : DomainEvent;
