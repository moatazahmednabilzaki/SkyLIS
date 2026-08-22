using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using SkyLIS.Application.Common;

namespace SkyLIS.Api.Infrastructure;

public static class ApiServices
{
    public static IServiceCollection AddApiServices(
        this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        services.AddHttpContextAccessor();

        // Behind the portal nginx (and an optional TLS edge), the real client address arrives
        // in X-Forwarded-For. Honor it ONLY from trusted networks — the compose network, given
        // in ForwardedHeaders:KnownNetworks — so the per-IP auth rate limit and the audit trail
        // record the client, not the proxy, without letting arbitrary callers spoof an address.
        // With nothing configured (e.g. local dev) the loopback-only default stands and any
        // X-Forwarded-For from a non-loopback peer is ignored — safe, just proxy-blind.
        services.Configure<Microsoft.AspNetCore.Builder.ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor
                | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto;
            var trusted = configuration.GetSection("ForwardedHeaders:KnownNetworks").Get<string[]>();
            if (trusted is { Length: > 0 })
            {
                options.KnownIPNetworks.Clear();
                options.KnownProxies.Clear();
                foreach (var cidr in trusted)
                    options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(cidr));
                // At most two hops in front of the API: TLS edge -> portal nginx -> API.
                options.ForwardLimit = configuration.GetValue<int?>("ForwardedHeaders:ForwardLimit") ?? 2;
            }
        });

        services.AddScoped<ICurrentUser, CurrentUser>();
        services.AddScoped<IClientContext, ClientContext>();
        services.AddSingleton<TokenService>();
        // Enums cross the wire as strings (portals send/receive "SharedRls", "Female", …).
        services.ConfigureHttpJsonOptions(options =>
            options.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));
        services.AddProblemDetails();
        services.AddExceptionHandler<ApiExceptionHandler>();
        // Liveness = process up; readiness = database reachable (tagged for /health/ready).
        services.AddHealthChecks()
            .AddCheck<DatabaseHealthCheck>("database", tags: ["ready"]);

        // Signing key resolution is centralized in TokenService: production requires
        // Auth:SigningKey from the environment and fails fast without it; the checked-in
        // dev key is accepted in Development only.
        var signingKey = TokenService.ResolveSigningKey(configuration, environment);

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = configuration["Auth:Issuer"] ?? "skylis",
                    ValidateAudience = false,
                    ValidateLifetime = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
                };
                // SignalR WebSocket connections carry the JWT as access_token in the query
                // string (browsers cannot set headers on WebSocket upgrade).
                options.Events = new JwtBearerEvents
                {
                    OnMessageReceived = context =>
                    {
                        var accessToken = context.Request.Query["access_token"];
                        if (!string.IsNullOrEmpty(accessToken)
                            && context.HttpContext.Request.Path.StartsWithSegments("/hubs"))
                        {
                            context.Token = accessToken;
                        }
                        return Task.CompletedTask;
                    },
                };
            });
        services.AddAuthorization();

        services.AddSignalR();
        services.AddScoped<IRealtimeNotifier, SignalRRealtimeNotifier>();

        return services;
    }
}

/// <summary>Readiness: one cheap connectivity probe against PostgreSQL.</summary>
internal sealed class DatabaseHealthCheck : Microsoft.Extensions.Diagnostics.HealthChecks.IHealthCheck
{
    private readonly SkyLIS.Infrastructure.Persistence.SkyLisDbContext _db;
    public DatabaseHealthCheck(SkyLIS.Infrastructure.Persistence.SkyLisDbContext db) => _db = db;

    public async Task<Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult> CheckHealthAsync(
        Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckContext context, CancellationToken ct = default) =>
        await _db.Database.CanConnectAsync(ct)
            ? Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy("database reachable")
            : Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Unhealthy("database unreachable");
}

/// <summary>Client IP for the audit trail's "where" dimension (FR-SYS-001).</summary>
internal sealed class ClientContext : IClientContext
{
    private readonly IHttpContextAccessor _accessor;
    public ClientContext(IHttpContextAccessor accessor) => _accessor = accessor;
    public string? IpAddress => _accessor.HttpContext?.Connection.RemoteIpAddress?.ToString();
}

/// <summary>Caller identity materialized from JWT claims.</summary>
internal sealed class CurrentUser : ICurrentUser
{
    private readonly ClaimsPrincipal? _principal;

    public CurrentUser(IHttpContextAccessor accessor) => _principal = accessor.HttpContext?.User;

    public Guid? UserId =>
        Guid.TryParse(_principal?.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    public Guid? TenantId =>
        Guid.TryParse(_principal?.FindFirstValue("tenant_id"), out var id) ? id : null;

    public bool IsPlatformOperator =>
        _principal?.FindFirstValue("scope_type") == "platform";

    public bool HasPermission(string permission) =>
        _principal?.FindAll("permission").Any(c => c.Value == permission) == true;
}
