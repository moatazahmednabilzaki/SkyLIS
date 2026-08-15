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

// Portal dev servers (Development only; production portals are same-origin behind the gateway).
builder.Services.AddCors(options => options.AddPolicy("portals", policy => policy
    .WithOrigins("http://localhost:4300", "http://localhost:4201")
    .AllowAnyHeader()
    .AllowAnyMethod()));

var app = builder.Build();

app.UseSerilogRequestLogging();
app.UseExceptionHandler();
if (app.Environment.IsDevelopment())
    app.UseCors("portals");
app.UseAuthentication();
app.UseMiddleware<TenantResolutionMiddleware>();
app.UseAuthorization();

app.MapHealthChecks("/health");

var api = app.MapGroup("/api/v1");
api.MapTenantEndpoints();
api.MapPatientEndpoints();
api.MapCatalogEndpoints();
api.MapVisitEndpoints();
api.MapResultEndpoints();
api.MapBillingEndpoints();
if (app.Environment.IsDevelopment())
    api.MapDevAuthEndpoints(app.Configuration);

app.Run();
