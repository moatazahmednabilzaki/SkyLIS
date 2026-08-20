using Microsoft.AspNetCore.RateLimiting;
using Serilog;
using SkyLIS.Api.Endpoints;
using SkyLIS.Api.Infrastructure;
using SkyLIS.Application;
using SkyLIS.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, config) => config
    .ReadFrom.Configuration(context.Configuration)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("service", "skylis-api"));

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddApiServices(builder.Configuration, builder.Environment);

// Brute-force backstop on the anonymous auth surface (layered on the §4.3 account
// lockout): per-IP fixed window, generous enough for a busy front desk.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("auth", context =>
        System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions
            {
                PermitLimit = 100,
                Window = TimeSpan.FromMinutes(5),
            }));
});

// Portal dev servers (Development only; production portals are same-origin behind the gateway).
builder.Services.AddCors(options => options.AddPolicy("portals", policy => policy
    .WithOrigins("http://localhost:4300", "http://localhost:4201")
    .AllowAnyHeader()
    .AllowAnyMethod()
    .AllowCredentials())); // SignalR negotiation

var app = builder.Build();

app.UseSerilogRequestLogging();
app.UseExceptionHandler();
if (app.Environment.IsDevelopment())
    app.UseCors("portals");
app.UseAuthentication();
app.UseMiddleware<TenantResolutionMiddleware>();
app.UseAuthorization();
app.UseRateLimiter();

// /health = overall (includes DB), /health/live = process only, /health/ready = DB probe.
app.MapHealthChecks("/health");
app.MapHealthChecks("/health/live", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = _ => false,
});
app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
});
app.MapHub<SkyLIS.Api.Infrastructure.WorklistHub>("/hubs/worklists");

var api = app.MapGroup("/api/v1");
api.MapAuthEndpoints(app.Configuration);
api.MapTenantEndpoints();
api.MapOrgEndpoints();
api.MapPlatformServiceEndpoints();
api.MapUserEndpoints();
api.MapPatientEndpoints();
api.MapCatalogEndpoints();
api.MapVisitEndpoints();
api.MapResultEndpoints();
api.MapReportEndpoints();
api.MapBillingEndpoints();
if (app.Environment.IsDevelopment())
    api.MapDevAuthEndpoints(app.Configuration);

app.Run();
