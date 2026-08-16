using Microsoft.Extensions.Logging;
using SkyLIS.Application.Common;
using SkyLIS.Application.Reports;
using SkyLIS.Application.Users;
using SkyLIS.Domain.Catalog;
using SkyLIS.Domain.Org;
using SkyLIS.Domain.Platform;
using SkyLIS.Domain.Reports;
using SkyLIS.Domain.Results;
using SkyLIS.Domain.Tenants;
using SkyLIS.Domain.Users;

namespace SkyLIS.Application.IntegrationHandlers;

/// <summary>
/// P01.2 step 4: the outbox consumer creating the initial Tenant Admin. Runs under the
/// new tenant's context (the dispatcher restores it from the event), so RLS admits the
/// insert. Idempotent via the inbox and the username uniqueness check.
/// </summary>
internal sealed class CreateInitialAdminHandler : IIntegrationEventHandler<TenantProvisioned>
{
    private readonly IUserRepository _users;
    private readonly IClock _clock;

    public CreateInitialAdminHandler(IUserRepository users, IClock clock)
    {
        _users = users;
        _clock = clock;
    }

    public async Task HandleAsync(TenantProvisioned domainEvent, CancellationToken ct = default)
    {
        if (await _users.UserNameExistsAsync(domainEvent.AdminUserName, ct))
            return; // already created (redelivery safety on top of the inbox)
        _users.Add(User.Create(
            Guid.CreateVersion7(), domainEvent.TenantId, domainEvent.AdminUserName,
            domainEvent.AdminFullName, domainEvent.AdminPasswordHash,
            [RoleCatalog.TenantAdmin], _clock.UtcNow));
    }
}

/// <summary>
/// P03.2: every tenant starts with its MAIN branch (visits and invoices are branch-bound).
/// Runs under the new tenant's restored context, like the initial-admin consumer.
/// </summary>
internal sealed class CreateMainBranchHandler : IIntegrationEventHandler<TenantProvisioned>
{
    private readonly IBranchRepository _branches;
    private readonly IClock _clock;

    public CreateMainBranchHandler(IBranchRepository branches, IClock clock)
    {
        _branches = branches;
        _clock = clock;
    }

    public async Task HandleAsync(TenantProvisioned domainEvent, CancellationToken ct = default)
    {
        if (await _branches.CodeExistsAsync("MAIN", ct))
            return; // redelivery safety on top of the inbox
        _branches.Add(Branch.Create(
            Guid.CreateVersion7(), domainEvent.TenantId, "MAIN", "Main Branch",
            null, null, isMain: true, _clock.UtcNow));
    }
}

/// <summary>
/// FR-TEN-040 (P01.4): seed the new tenant's sample taxonomy from its country pack so the
/// lab starts configured with local defaults instead of a blank catalog. Tenants without a
/// matching pack simply start blank — provisioning never fails on a missing pack.
/// </summary>
internal sealed class SeedCountryDefaultsHandler : IIntegrationEventHandler<TenantProvisioned>
{
    private readonly ICountryPackRepository _packs;
    private readonly ISampleTypeRepository _sampleTypes;

    public SeedCountryDefaultsHandler(ICountryPackRepository packs, ISampleTypeRepository sampleTypes)
    {
        _packs = packs;
        _sampleTypes = sampleTypes;
    }

    public async Task HandleAsync(TenantProvisioned domainEvent, CancellationToken ct = default)
    {
        var pack = await _packs.GetByCountryAsync(domainEvent.CountryCode, ct);
        if (pack is null)
            return;

        foreach (var packType in pack.SampleTypes)
        {
            if (await _sampleTypes.NameExistsAsync(packType.Name, ct))
                continue; // redelivery safety on top of the inbox
            var sampleType = SampleType.Create(
                Guid.CreateVersion7(), domainEvent.TenantId, packType.Name, packType.ContainerName);
            foreach (var condition in packType.Conditions)
                sampleType.AddCondition(
                    Guid.CreateVersion7(), condition.Name, condition.DelayMinutes, condition.CompatibilityGroup);
            _sampleTypes.Add(sampleType);
        }
    }
}

/// <summary>
/// FR-MDM-071 (P01.7): a pushed master test lands in this tenant's catalogue as
/// PendingActivation. The tenant's sample taxonomy is resolved BY NAME (country packs
/// seed canonical names); a missing sample type is created on the fly so the push never
/// fails a tenant. Activation stays local — a price must be set first.
/// </summary>
internal sealed class CreatePushedTestHandler : IIntegrationEventHandler<MasterTestPushed>
{
    private readonly ILabTestRepository _tests;
    private readonly ISampleTypeRepository _sampleTypes;

    public CreatePushedTestHandler(ILabTestRepository tests, ISampleTypeRepository sampleTypes)
    {
        _tests = tests;
        _sampleTypes = sampleTypes;
    }

    public async Task HandleAsync(MasterTestPushed domainEvent, CancellationToken ct = default)
    {
        if (await _tests.CodeExistsAsync(domainEvent.Code, ct))
            return; // the tenant already carries this code (redelivery or local definition)

        var sampleType = await _sampleTypes.GetByNameAsync(domainEvent.SampleTypeName, ct);
        if (sampleType is null)
        {
            sampleType = SampleType.Create(
                Guid.CreateVersion7(), domainEvent.TenantId, domainEvent.SampleTypeName, domainEvent.ContainerName);
            if (domainEvent.ConditionName is not null)
                sampleType.AddCondition(Guid.CreateVersion7(), domainEvent.ConditionName, null, "G1");
            _sampleTypes.Add(sampleType);
        }

        var condition = domainEvent.ConditionName is null
            ? null
            : sampleType.Conditions.FirstOrDefault(c =>
                c.Name.Equals(domainEvent.ConditionName, StringComparison.OrdinalIgnoreCase));

        _tests.Add(LabTest.CreateFromPlatformPush(
            Guid.CreateVersion7(), domainEvent.TenantId, domainEvent.Code, domainEvent.Name,
            domainEvent.Department, sampleType.Id, condition?.Id));
    }
}

/// <summary>
/// FR-SYS-011: every finalized report increments the tenant's monthly meter —
/// "one finalized report per visit" is the billing unit consumed by M01 (P01.3).
/// </summary>
internal sealed class ReportFinalizedMeteringHandler : IIntegrationEventHandler<ReportFinalized>
{
    private readonly IUsageMeterStore _meters;

    public ReportFinalizedMeteringHandler(IUsageMeterStore meters) => _meters = meters;

    public Task HandleAsync(ReportFinalized domainEvent, CancellationToken ct = default) =>
        _meters.IncrementFinalizedReportsAsync(domainEvent.TenantId, DateTimeOffset.UtcNow, ct);
}

/// <summary>
/// NFR-016: critical values trigger an immediate notification burst. Channels are behind
/// the INotificationSender port (dev sender logs; WhatsApp/SMS providers plug in later).
/// </summary>
internal sealed class CriticalValueNotificationHandler : IIntegrationEventHandler<CriticalValueFlagged>
{
    private readonly INotificationSender _sender;
    private readonly ILogger<CriticalValueNotificationHandler> _logger;

    public CriticalValueNotificationHandler(INotificationSender sender, ILogger<CriticalValueNotificationHandler> logger)
    {
        _sender = sender;
        _logger = logger;
    }

    public async Task HandleAsync(CriticalValueFlagged domainEvent, CancellationToken ct = default)
    {
        _logger.LogWarning(
            "CRITICAL VALUE burst: {TestCode} = {Value} {Unit} on visit {VisitId} (tenant {TenantId})",
            domainEvent.TestCode, domainEvent.Value, domainEvent.Unit, domainEvent.VisitId, domainEvent.TenantId);
        await _sender.SendAsync("portal", "bench+ordering-physician",
            $"CRITICAL: {domainEvent.TestCode} = {domainEvent.Value} {domainEvent.Unit}", ct);
    }
}
