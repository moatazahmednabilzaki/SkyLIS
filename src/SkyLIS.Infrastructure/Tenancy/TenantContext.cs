using SkyLIS.Application.Common;

namespace SkyLIS.Infrastructure.Tenancy;

/// <summary>
/// Scoped per request. Populated by the API's tenant-resolution middleware from trusted
/// sources ONLY (JWT tenant claim / verified host mapping) — never from a request body.
/// </summary>
public sealed class TenantContext : ITenantContext, ITenantRealm
{
    private Guid? _tenantId;

    public bool HasTenant => _tenantId.HasValue;

    public Guid TenantId => _tenantId
        ?? throw new ForbiddenAccessException("No tenant context: this operation requires a tenant scope.");

    public void Set(Guid tenantId)
    {
        if (_tenantId.HasValue && _tenantId.Value != tenantId)
            throw new InvalidOperationException("Tenant context cannot be changed within a request.");
        _tenantId = tenantId;
    }
}
