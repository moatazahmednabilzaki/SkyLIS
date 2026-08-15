using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using SkyLIS.Application.Common;

namespace SkyLIS.Api.Infrastructure;

public static class ApiServices
{
    public static IServiceCollection AddApiServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, CurrentUser>();
        services.AddProblemDetails();
        services.AddExceptionHandler<ApiExceptionHandler>();
        services.AddHealthChecks();

        // DEV AUTH ONLY: symmetric key from configuration. Production replaces this with
        // the OpenIddict authority (SRS §2.2); secrets come from the vault, never source.
        var signingKey = configuration["Auth:DevSigningKey"]
            ?? throw new InvalidOperationException("Auth:DevSigningKey is not configured.");

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = configuration["Auth:Issuer"] ?? "skylis-dev",
                    ValidateAudience = false,
                    ValidateLifetime = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
                };
            });
        services.AddAuthorization();

        return services;
    }
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
