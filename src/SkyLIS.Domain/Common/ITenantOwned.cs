namespace SkyLIS.Domain.Common;

/// <summary>
/// Marks a tenant-owned aggregate/entity. Every implementation is protected by the
/// PostgreSQL RLS policy and the EF global query filter (defense in depth).
/// The tenant identifier is assigned once at creation and never mutated.
/// </summary>
public interface ITenantOwned
{
    Guid TenantId { get; }
}
