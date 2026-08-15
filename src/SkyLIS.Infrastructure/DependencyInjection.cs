using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SkyLIS.Application.Common;
using SkyLIS.Application.Patients;
using SkyLIS.Application.Reports;
using SkyLIS.Application.Results;
using SkyLIS.Application.Tenants;
using SkyLIS.Application.Visits;
using SkyLIS.Infrastructure.Persistence;
using SkyLIS.Infrastructure.Reports;
using SkyLIS.Infrastructure.Services;
using SkyLIS.Infrastructure.Tenancy;

namespace SkyLIS.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<TenantContext>();
        services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<TenantContext>());

        services.AddDbContext<SkyLisDbContext>((sp, options) =>
        {
            options.UseNpgsql(
                configuration.GetConnectionString("Postgres")
                    ?? throw new InvalidOperationException("Connection string 'Postgres' is not configured."),
                npgsql => npgsql.MigrationsHistoryTable("__ef_migrations", "platform"));
            options.UseSnakeCaseNamingConvention();
            // RLS: stamp the ambient tenant onto every opened connection (defense in depth
            // with the global query filters).
            options.AddInterceptors(new TenantSessionInterceptor(sp.GetRequiredService<TenantContext>()));
        });
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<SkyLisDbContext>());

        // Write side
        services.AddScoped<ITenantRepository, TenantRepository>();
        services.AddScoped<IPatientRepository, PatientRepository>();
        services.AddScoped<ILabTestRepository, LabTestRepository>();
        services.AddScoped<ISampleTypeRepository, SampleTypeRepository>();
        services.AddScoped<IVisitRepository, VisitRepository>();
        services.AddScoped<ITestResultRepository, TestResultRepository>();
        services.AddScoped<ILabReportRepository, LabReportRepository>();
        services.AddScoped<IInvoiceRepository, InvoiceRepository>();

        // Read side
        services.AddScoped<ITenantQueries, TenantQueries>();
        services.AddScoped<IPatientQueries, PatientQueries>();
        services.AddScoped<IVisitQueries, VisitQueries>();
        services.AddScoped<IResultQueries, ResultQueries>();
        services.AddScoped<IReportQueries, Reports.ReportQueries>();
        services.AddScoped<IReportVerificationQueries, ReportVerificationQueries>();
        services.AddScoped<Application.Analytics.IAnalyticsQueries, AnalyticsQueries>();
        services.AddScoped<Application.Audit.IAuditQueries, Audit.AuditQueries>();

        // Cross-cutting
        services.AddSingleton<IClock, SystemClock>();
        services.AddScoped<INumberSeriesService, NumberSeriesService>();
        services.AddSingleton<IReportRenderer, HtmlReportRenderer>();
        services.AddSingleton<INotificationSender, DevNotificationSender>();

        return services;
    }
}
