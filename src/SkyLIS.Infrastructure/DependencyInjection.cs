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
        services.AddScoped<ICountryPackRepository, CountryPackRepository>();
        services.AddScoped<IPlanRepository, PlanRepository>();
        services.AddScoped<IMasterTestRepository, MasterTestRepository>();
        services.AddScoped<IAttachmentRepository, AttachmentRepository>();
        services.AddScoped<IBranchRepository, BranchRepository>();
        services.AddScoped<Application.Org.ITenantSettingRepository, TenantSettingRepository>();
        services.AddScoped<IPatientRepository, PatientRepository>();
        services.AddScoped<Application.Patients.IDataSubjectRequestRepository, DataSubjectRequestRepository>();
        services.AddScoped<Application.Patients.IPatientMergeStore, PatientMergeStore>();
        services.AddScoped<ILabTestRepository, LabTestRepository>();
        services.AddScoped<ISampleTypeRepository, SampleTypeRepository>();
        services.AddScoped<IPanelRepository, PanelRepository>();
        services.AddScoped<IVisitRepository, VisitRepository>();
        services.AddScoped<ITestResultRepository, TestResultRepository>();
        services.AddScoped<ILabReportRepository, LabReportRepository>();
        services.AddScoped<IInvoiceRepository, InvoiceRepository>();
        services.AddScoped<ICreditNoteRepository, CreditNoteRepository>();
        services.AddScoped<ICashierShiftRepository, CashierShiftRepository>();

        // Read side
        services.AddScoped<ITenantQueries, TenantQueries>();
        services.AddScoped<Application.Org.IBranchQueries, BranchQueries>();
        services.AddScoped<Application.Org.ITenantSettingQueries, TenantSettingQueries>();
        services.AddScoped<Application.Org.ISetupStatusQueries, SetupStatusQueries>();
        services.AddScoped<Application.Platform.ICountryPackQueries, CountryPackQueries>();
        services.AddScoped<Application.Catalog.ICatalogQueries, CatalogQueries>();
        services.AddScoped<Application.Catalog.IPanelQueries, PanelQueries>();
        services.AddScoped<Application.Billing.IBillingQueries, BillingQueries>();
        services.AddScoped<Application.Platform.IMasterTestQueries, MasterTestQueries>();
        services.AddScoped<Application.Platform.IPlanQueries, PlanQueries>();
        services.AddScoped<Application.Platform.ITenantUserMonitorQueries, TenantUserMonitorQueries>();
        services.AddScoped<Application.Files.IAttachmentQueries, AttachmentQueries>();
        services.AddScoped<Application.Search.ISearchQueries, SearchQueries>();
        services.AddScoped<IPatientQueries, PatientQueries>();
        services.AddScoped<Application.Patients.IPatient360Queries, Patient360Queries>();
        services.AddScoped<Application.Patients.IPatientPrivacyQueries, PatientPrivacyQueries>();
        services.AddScoped<IVisitQueries, VisitQueries>();
        services.AddScoped<IResultQueries, ResultQueries>();
        services.AddScoped<Application.Worklists.IWorklistQueries, WorklistQueries>();
        services.AddScoped<IReportQueries, Reports.ReportQueries>();
        services.AddScoped<IReportVerificationQueries, ReportVerificationQueries>();
        services.AddScoped<Application.Analytics.IAnalyticsQueries, AnalyticsQueries>();
        services.AddScoped<Application.Audit.IAuditQueries, Audit.AuditQueries>();

        // Cross-cutting
        services.AddSingleton<IClock, SystemClock>();
        services.AddScoped<INumberSeriesService, NumberSeriesService>();
        services.AddSingleton<IReportRenderer, HtmlReportRenderer>();
        services.AddSingleton<INotificationSender, DevNotificationSender>();

        // Platform seed data: canonical country packs (P01.4) and plans (P01.3)
        services.AddHostedService<Platform.CountryPackSeeder>();
        services.AddHostedService<Platform.PlanSeeder>();

        // Outbox dispatch + integration consumers (at-least-once, inbox-deduplicated)
        services.AddHostedService<Outbox.OutboxDispatcher>();
        services.AddScoped<Application.Platform.IOutboxStatusQueries, Outbox.OutboxStatusQueries>();
        services.AddScoped<IUsageMeterStore, Metering.UsageMeterStore>();

        // Users & auth (M02)
        services.AddSingleton<Application.Users.IPasswordHasher, Users.Pbkdf2PasswordHasher>();
        services.AddScoped<Application.Users.IUserRepository, Users.UserRepository>();
        services.AddScoped<Application.Users.IUserQueries, Users.UserQueries>();
        // The integration handlers themselves are registered by AddApplication (they are
        // internal to the Application layer by design).

        return services;
    }
}
