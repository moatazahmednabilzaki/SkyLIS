using System.Reflection;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using SkyLIS.Application.Common.Behaviors;

namespace SkyLIS.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        var assembly = Assembly.GetExecutingAssembly();

        services.AddValidatorsFromAssembly(assembly, includeInternalTypes: true);

        // Integration-event consumers (dispatched by the outbox): Application owns its
        // handler registrations — they stay internal (architecture gate).
        services.AddScoped<Common.IIntegrationEventHandler<Domain.Reports.ReportFinalized>,
            IntegrationHandlers.ReportFinalizedMeteringHandler>();
        services.AddScoped<Common.IIntegrationEventHandler<Domain.Results.CriticalValueFlagged>,
            IntegrationHandlers.CriticalValueNotificationHandler>();
        services.AddScoped<Common.IIntegrationEventHandler<Domain.Tenants.TenantProvisioned>,
            IntegrationHandlers.CreateInitialAdminHandler>();
        services.AddScoped<Common.IIntegrationEventHandler<Domain.Tenants.TenantProvisioned>,
            IntegrationHandlers.CreateMainBranchHandler>();
        services.AddScoped<Common.IIntegrationEventHandler<Domain.Tenants.TenantProvisioned>,
            IntegrationHandlers.SeedCountryDefaultsHandler>();

        // Real-time worklist hints (FR-SYS-010): tenant events fan out to portal areas.
        AddRealtimeForwarder<Domain.Visits.VisitRegistered>(services, "dashboard", "results");
        AddRealtimeForwarder<Domain.Visits.SampleCollected>(services, "dashboard", "results", "phleb", "reception");
        AddRealtimeForwarder<Domain.Visits.SampleReceived>(services, "dashboard", "results");
        AddRealtimeForwarder<Domain.Visits.SampleRejected>(services, "dashboard", "results", "reception", "phleb");
        AddRealtimeForwarder<Domain.Visits.SampleReserved>(services, "reception", "phleb");
        AddRealtimeForwarder<Domain.Visits.PatientInformedOfRejection>(services, "reception");
        AddRealtimeForwarder<Domain.Results.ResultEntered>(services, "dashboard", "validation");
        AddRealtimeForwarder<Domain.Results.ResultTechnicallyValid>(services, "dashboard", "validation");
        AddRealtimeForwarder<Domain.Results.ResultMedicallyValid>(services, "dashboard", "validation", "reports");
        AddRealtimeForwarder<Domain.Results.ResultRerunOrdered>(services, "dashboard", "results", "validation");
        AddRealtimeForwarder<Domain.Results.ResultAmended>(services, "dashboard", "validation", "reports");
        AddRealtimeForwarder<Domain.Results.CriticalValueFlagged>(services, "dashboard", "critical");
        AddRealtimeForwarder<Domain.Results.CriticalValueClosed>(services, "dashboard", "critical");
        AddRealtimeForwarder<Domain.Reports.ReportRendered>(services, "dashboard", "reports");
        AddRealtimeForwarder<Domain.Reports.ReportDelivered>(services, "reports");
        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssembly(assembly);
            // Order matters: logging wraps everything; authorization before validation;
            // the unit of work commits innermost, after the handler succeeds.
            cfg.AddOpenBehavior(typeof(LoggingBehavior<,>));
            cfg.AddOpenBehavior(typeof(PermissionBehavior<,>));
            cfg.AddOpenBehavior(typeof(ValidationBehavior<,>));
            cfg.AddOpenBehavior(typeof(UnitOfWorkBehavior<,>));
        });

        return services;
    }

    private static void AddRealtimeForwarder<TEvent>(IServiceCollection services, params string[] areas)
        where TEvent : Domain.Common.ITenantEvent =>
        services.AddScoped<Common.IIntegrationEventHandler<TEvent>>(sp =>
            new Common.RealtimeForwarder<TEvent>(
                sp.GetRequiredService<Common.IRealtimeNotifier>(), areas));
}
