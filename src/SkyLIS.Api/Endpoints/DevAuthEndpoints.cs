using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace SkyLIS.Api.Endpoints;

/// <summary>
/// DEVELOPMENT ONLY: issues signed dev JWTs so the portals can authenticate before the
/// OpenIddict authority ships. Mapped exclusively when the environment is Development —
/// never registered in QA/Staging/Production.
/// </summary>
public static class DevAuthEndpoints
{
    public sealed record DevTokenRequest(string Scope, Guid? TenantId, string? UserName);

    private static readonly string[] TenantPermissions =
    [
        "users.user.create", "users.user.read",
        "org.branch.read", "org.branch.manage",
        "catalog.catalog.read",
        "patients.patient.create", "patients.patient.read",
        "orders.visit.create", "orders.visit.read",
        "samples.sample.collect", "samples.sample.receive", "samples.sample.reject",
        "samples.sample.informPatient",
        "catalog.test.create", "catalog.test.update", "catalog.test.approve",
        "catalog.sampletype.create",
        "billing.payment.capture", "billing.invoice.adjust", "billing.refund.approve", "billing.shift.manage",
        "orders.visit.cancel",
        "results.result.enter", "results.result.validateTechnical", "results.result.validateMedical",
        "results.result.amend",
        "reports.report.render", "reports.report.deliver", "reports.report.read",
        "analytics.dashboard.read",
        "audit.trail.read",
    ];

    private static readonly string[] PlatformPermissions =
    [
        "platform.tenant.provision", "platform.tenant.read", "platform.outbox.read",
        "platform.masterdata.read", "platform.masterdata.manage",
    ];

    public static void MapDevAuthEndpoints(this RouteGroupBuilder group, IConfiguration configuration)
    {
        group.MapPost("/dev/token", (DevTokenRequest request) =>
        {
            var isPlatform = string.Equals(request.Scope, "platform", StringComparison.OrdinalIgnoreCase);
            if (!isPlatform && request.TenantId is null)
                return Results.BadRequest(new { error = "tenantId is required for tenant scope." });

            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
                new(ClaimTypes.Name, request.UserName ?? (isPlatform ? "dev-platform-operator" : "dev-tenant-user")),
                new("scope_type", isPlatform ? "platform" : "tenant"),
            };
            if (!isPlatform)
                claims.Add(new Claim("tenant_id", request.TenantId!.Value.ToString()));
            claims.AddRange((isPlatform ? PlatformPermissions : TenantPermissions)
                .Select(p => new Claim("permission", p)));

            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(configuration["Auth:DevSigningKey"]!));
            var token = new JwtSecurityToken(
                issuer: configuration["Auth:Issuer"] ?? "skylis-dev",
                claims: claims,
                expires: DateTime.UtcNow.AddHours(12),
                signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));

            return Results.Ok(new { token = new JwtSecurityTokenHandler().WriteToken(token) });
        }).AllowAnonymous().WithTags("Development");
    }
}
