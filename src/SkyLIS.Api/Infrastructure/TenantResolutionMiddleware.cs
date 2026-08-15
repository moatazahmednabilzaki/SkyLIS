using SkyLIS.Infrastructure.Tenancy;

namespace SkyLIS.Api.Infrastructure;

/// <summary>
/// Resolves the ambient tenant from the authenticated JWT's tenant claim ONLY —
/// never from a request body or client-chosen header (EAA multi-tenancy rule).
/// Platform operators (Admin Portal) carry no tenant claim and get no tenant context.
/// </summary>
public sealed class TenantResolutionMiddleware
{
    private readonly RequestDelegate _next;

    public TenantResolutionMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context, TenantContext tenantContext)
    {
        var claim = context.User.FindFirst("tenant_id")?.Value;
        if (claim is not null && Guid.TryParse(claim, out var tenantId))
            tenantContext.Set(tenantId);

        await _next(context);
    }
}
