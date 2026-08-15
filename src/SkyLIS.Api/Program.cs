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
builder.Services.AddApiServices(builder.Configuration);

var app = builder.Build();

app.UseSerilogRequestLogging();
app.UseExceptionHandler();
app.UseAuthentication();
app.UseMiddleware<TenantResolutionMiddleware>();
app.UseAuthorization();

app.MapHealthChecks("/health");

var api = app.MapGroup("/api/v1");
api.MapTenantEndpoints();
api.MapPatientEndpoints();
api.MapCatalogEndpoints();
api.MapVisitEndpoints();
api.MapBillingEndpoints();

app.Run();
